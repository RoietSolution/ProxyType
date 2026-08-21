using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ProxyType.Api.Contracts;
using ProxyType.Api.Data;
using ProxyType.Api.Domain;
using ProxyType.Api.Security;
using ProxyType.Api.Services;

namespace ProxyType.Api.Finance;

public sealed class AepsService(ProxyTypeDbContext db, ICurrentScopeService scope, ServicePermissionService permissions, IAepsProvider provider)
{
    public async Task<IReadOnlyList<AepsBankResponse>> BanksAsync(CancellationToken ct = default) =>
        await db.AepsBanks.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name)
            .Select(x => new AepsBankResponse(x.Iin, x.Name)).ToListAsync(ct);

    public async Task<AepsResponse> ExecuteAsync(AepsRequest request, CancellationToken ct = default)
    {
        if (request.TransactionType == "CASH_WITHDRAWAL" && (!request.Amount.HasValue || request.Amount <= 0))
            throw new ArgumentException("Amount is required for cash withdrawal.");
        if (request.TransactionType != "CASH_WITHDRAWAL" && request.Amount.HasValue)
            throw new ArgumentException("Amount is only supported for cash withdrawal.");

        var membership = await db.OrganizationMemberships.AsNoTracking()
            .Where(x => x.UserId == scope.UserId && x.IsActive && (x.ValidToUtc == null || x.ValidToUtc > DateTime.UtcNow))
            .OrderByDescending(x => x.IsPrimary).Select(x => new { x.OrganizationUnitId }).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("An active organization assignment is required.");
        if (!(await scope.GetAccessibleOrganizationIdsAsync(ct)).Contains(membership.OrganizationUnitId))
            throw new UnauthorizedAccessException("USER_INACTIVE_OR_OUT_OF_SCOPE");

        var service = await db.Services.SingleOrDefaultAsync(x => x.Code == "aeps", ct)
            ?? throw new InvalidOperationException("AEPS is not deployed in the service catalog.");
        var effective = await permissions.GetEffectiveAsync(membership.OrganizationUnitId, ct);
        var decision = effective.SingleOrDefault(x => x.ServiceId == service.ServiceId);
        if (decision is null || !decision.IsAllowed)
            throw new UnauthorizedAccessException(decision?.DecisionReason ?? "SERVICE_NOT_ASSIGNED");
        var bank = await db.AepsBanks.SingleOrDefaultAsync(x => x.Iin == request.BankIin && x.IsActive, ct)
            ?? throw new ArgumentException("The selected AEPS bank is not active.");

        var existing = await db.ServiceTransactions.SingleOrDefaultAsync(x =>
            x.OrganizationUnitId == membership.OrganizationUnitId && x.ClientIdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null) return await ToResponseAsync(existing, ct);

        var now = DateTime.UtcNow;
        var reference = $"PTA{now:yyyyMMddHHmmssfff}{Random.Shared.Next(100, 999)}";
        var transaction = new ServiceTransaction
        {
            ServiceTransactionId = Guid.NewGuid(), OrganizationUnitId = membership.OrganizationUnitId,
            UserId = scope.UserId, ServiceId = service.ServiceId, TransactionReference = reference,
            ClientIdempotencyKey = request.IdempotencyKey, Status = "CREATED", Amount = request.Amount ?? 0,
            RequestSummaryJson = JsonSerializer.Serialize(new { request.TransactionType, Aadhaar = Mask(request.AadhaarNumber), Mobile = Mask(request.MobileNumber), request.BankIin, request.DeviceName }),
            RequestAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now
        };
        db.ServiceTransactions.Add(transaction);
        db.AepsTransactionDetails.Add(new AepsTransactionDetail
        {
            AepsTransactionDetailId = Guid.NewGuid(), ServiceTransactionId = transaction.ServiceTransactionId,
            AepsBankId = bank.AepsBankId, TransactionType = request.TransactionType,
            MaskedAadhaar = Mask(request.AadhaarNumber), MaskedMobile = Mask(request.MobileNumber), DeviceName = request.DeviceName,
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync(ct);

        AepsProviderResult result;
        try
        {
            result = await provider.ExecuteAsync(new AepsProviderRequest(request.TransactionType, request.AadhaarNumber,
                request.MobileNumber, request.BankIin, request.DeviceName, request.BiometricCaptureReference, request.Amount, reference), ct);
        }
        catch (InvalidOperationException ex)
        {
            transaction.Status = "FAILED"; transaction.FailureCode = "PROVIDER_DISABLED"; transaction.FailureMessage = ex.Message;
            transaction.ResponseAtUtc = DateTime.UtcNow; transaction.CompletedAtUtc = transaction.ResponseAtUtc; transaction.UpdatedAtUtc = transaction.ResponseAtUtc.Value;
            await db.SaveChangesAsync(ct); throw;
        }

        transaction.Status = result.Status.ToString().ToUpperInvariant(); transaction.ProviderStatus = result.RawStatus;
        transaction.ProviderReference = result.ProviderReference; transaction.FailureMessage = result.FailureReason;
        transaction.ResponseAtUtc = DateTime.UtcNow; transaction.UpdatedAtUtc = transaction.ResponseAtUtc.Value;
        if (result.Status is NormalizedTransactionStatus.Succeeded or NormalizedTransactionStatus.Failed) transaction.CompletedAtUtc = transaction.ResponseAtUtc;

        if (result.Status == NormalizedTransactionStatus.Succeeded && request.TransactionType == "CASH_WITHDRAWAL")
        {
            transaction.CreditAmount = request.Amount!.Value;
            try
            {
                await EnsureAepsAccountAsync(scope.UserId, ct);
                await db.Database.ExecuteSqlInterpolatedAsync($"EXEC finance.PostAepsWalletCredit {Guid.NewGuid()}, {transaction.ServiceTransactionId}, {"AEPS:" + reference}, {reference}, {scope.UserId}, {request.Amount.Value}", ct);
            }
            catch (DbException ex)
            {
                transaction.Status = "FAILED"; transaction.FailureCode = ex is SqlException sql && sql.Number == 51022 ? "WALLET_POSTING_FAILED" : "WALLET_POSTING_FAILED";
                transaction.FailureMessage = "AEPS wallet posting failed."; transaction.CreditAmount = 0; transaction.CompletedAtUtc = DateTime.UtcNow;
            }
        }
        db.Receipts.Add(new Receipt { ReceiptId = Guid.NewGuid(), ReceiptNumber = $"RCP-{reference}", ServiceTransactionId = transaction.ServiceTransactionId,
            IssuedToUserId = scope.UserId, IssuedAtUtc = DateTime.UtcNow,
            SnapshotJson = JsonSerializer.Serialize(new { transaction.TransactionReference, request.TransactionType, transaction.Status, Aadhaar = Mask(request.AadhaarNumber), request.Amount, transaction.ProviderReference }) });
        db.AuditLogs.Add(new AuditLog { ActorUserId = scope.UserId, OrganizationUnitId = membership.OrganizationUnitId, Action = "AEPS_TRANSACTION", EntityType = "ServiceTransaction", EntityId = transaction.ServiceTransactionId.ToString(), CorrelationId = Guid.NewGuid(), DetailsJson = JsonSerializer.Serialize(new { request.TransactionType, Aadhaar = Mask(request.AadhaarNumber), Status = transaction.Status }), OccurredAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        return await ToResponseAsync(transaction, ct);
    }

    private async Task EnsureAepsAccountAsync(Guid userId, CancellationToken ct)
    {
        if (await db.LedgerAccounts.AnyAsync(x => x.UserId == userId && x.Code == $"USER:{userId}:AEPS" && x.IsActive, ct)) return;
        db.LedgerAccounts.Add(new LedgerAccount { LedgerAccountId = Guid.NewGuid(), UserId = userId, Code = $"USER:{userId}:AEPS", Name = "User AEPS wallet", AccountType = "LIABILITY", NormalBalance = "C", Currency = "INR", IsActive = true, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
    }

    private async Task<AepsResponse> ToResponseAsync(ServiceTransaction tx, CancellationToken ct) => new(tx.ServiceTransactionId, tx.TransactionReference,
        await db.AepsTransactionDetails.Where(x => x.ServiceTransactionId == tx.ServiceTransactionId).Select(x => x.TransactionType).SingleAsync(ct), tx.Status,
        provider.Mode.ToString().ToUpperInvariant(), tx.ProviderReference,
        await db.AepsTransactionDetails.Where(x => x.ServiceTransactionId == tx.ServiceTransactionId).Select(x => x.MaskedAadhaar).SingleAsync(ct), tx.Amount == 0 ? null : tx.Amount,
        tx.ChargeAmount, tx.CommissionAmount, tx.FailureMessage, await db.Receipts.Where(x => x.ServiceTransactionId == tx.ServiceTransactionId).Select(x => x.ReceiptNumber).SingleOrDefaultAsync(ct));

    private static string Mask(string value) => value.Length <= 4 ? "****" : $"{new string('*', value.Length - 4)}{value[^4..]}";
}
