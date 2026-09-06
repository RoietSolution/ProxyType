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

public sealed class RechargeService(
    ProxyTypeDbContext dbContext,
    ICurrentScopeService scopeService,
    ServicePermissionService permissionService,
    IRechargeProvider provider,
    PricingService pricingService)
{
    public async Task<RechargeResponse> RechargeAsync(
        RechargeRequest request,
        CancellationToken cancellationToken = default)
    {
        var membership = await dbContext.OrganizationMemberships.AsNoTracking()
            .Where(item => item.UserId == scopeService.UserId && item.IsActive &&
                (item.ValidToUtc == null || item.ValidToUtc > DateTime.UtcNow))
            .OrderByDescending(item => item.IsPrimary)
            .Select(item => new { item.OrganizationUnitId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("An active organization assignment is required.");

        if (!(await scopeService.GetAccessibleOrganizationIdsAsync(cancellationToken)).Contains(membership.OrganizationUnitId))
        {
            throw new UnauthorizedAccessException("USER_INACTIVE_OR_OUT_OF_SCOPE");
        }

        var service = await dbContext.Services.SingleOrDefaultAsync(item => item.Code == "recharge_v1", cancellationToken)
            ?? throw new InvalidOperationException("Recharge is not deployed in the service catalog.");
        var effective = await permissionService.GetEffectiveAsync(membership.OrganizationUnitId, cancellationToken);
        var decision = effective.SingleOrDefault(item => item.ServiceId == service.ServiceId);
        if (decision is null || !decision.IsAllowed)
        {
            throw new UnauthorizedAccessException(decision?.DecisionReason ?? "SERVICE_NOT_ASSIGNED");
        }

        var operatorItem = await dbContext.RechargeOperators.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Label == request.Operator && item.Type == request.RechargeType && item.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("The selected recharge operator is not active for this recharge type.");

        var existing = await dbContext.ServiceTransactions.SingleOrDefaultAsync(
            item => item.OrganizationUnitId == membership.OrganizationUnitId && item.ClientIdempotencyKey == request.IdempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            return ToResponse(existing, await ReceiptNumberAsync(existing.ServiceTransactionId, cancellationToken));
        }

        var now = DateTime.UtcNow;
        var commissionAmount = await pricingService.RechargeCommissionAsync(operatorItem.RechargeOperatorId, scopeService.UserId, request.Amount, cancellationToken);
        var reference = $"PTR{now:yyyyMMddHHmmssfff}{Random.Shared.Next(100, 999)}";
        var transaction = new ServiceTransaction
        {
            ServiceTransactionId = Guid.NewGuid(),
            OrganizationUnitId = membership.OrganizationUnitId,
            UserId = scopeService.UserId,
            ServiceId = service.ServiceId,
            TransactionReference = reference,
            ClientIdempotencyKey = request.IdempotencyKey,
            ProviderId = await dbContext.ServiceProviderRoutes.AsNoTracking()
                .Where(route => route.ServiceId == service.ServiceId && route.IsEnabled && route.Provider.IsEnabled)
                .OrderBy(route => route.Priority).Select(route => (Guid?)route.ProviderId).FirstOrDefaultAsync(cancellationToken),
            Status = "CREATED",
            Amount = request.Amount,
            ChargeAmount = 0,
            CommissionAmount = commissionAmount,
            RequestSummaryJson = JsonSerializer.Serialize(new
            {
                Account = Mask(request.Account),
                request.Amount,
                request.Operator,
                request.RechargeType,
                request.Pincode,
                request.Latitude,
                request.Longitude
            }),
            RequestAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.ServiceTransactions.Add(transaction);
        await dbContext.SaveChangesAsync(cancellationToken);

        RechargeProviderResult result;
        try
        {
            result = await provider.RechargeAsync(new RechargeProviderRequest(
                request.Account, request.Amount, request.Operator, request.RechargeType,
                request.Pincode, request.Latitude, request.Longitude, reference), cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            transaction.Status = "FAILED";
            transaction.FailureCode = "PROVIDER_DISABLED";
            transaction.FailureMessage = exception.Message;
            transaction.ResponseAtUtc = DateTime.UtcNow;
            transaction.CompletedAtUtc = transaction.ResponseAtUtc;
            transaction.UpdatedAtUtc = transaction.ResponseAtUtc.Value;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }

        transaction.Status = result.Status.ToString().ToUpperInvariant();
        transaction.ProviderStatus = result.RawStatus;
        transaction.ProviderReference = result.ProviderReference;
        transaction.FailureMessage = result.FailureReason;
        transaction.ResponseAtUtc = DateTime.UtcNow;
        transaction.UpdatedAtUtc = transaction.ResponseAtUtc.Value;
        if (result.Status is NormalizedTransactionStatus.Succeeded or NormalizedTransactionStatus.Failed)
        {
            transaction.CompletedAtUtc = transaction.ResponseAtUtc;
        }

        if (result.Status is NormalizedTransactionStatus.Succeeded or NormalizedTransactionStatus.Pending)
        {
            if (result.Status == NormalizedTransactionStatus.Pending)
            {
                transaction.CommissionAmount = 0;
            }
            transaction.DebitAmount = request.Amount;
            transaction.CreditAmount = transaction.CommissionAmount;
            try
            {
                await EnsureWalletAccountAsync(transaction.UserId, cancellationToken);
                await DebitWalletAsync(transaction, cancellationToken);
            }
            catch (DbException exception)
            {
                transaction.Status = "FAILED";
                var insufficient = exception is SqlException sqlException && sqlException.Number == 51022 ||
                    exception.Message.Contains("Insufficient", StringComparison.OrdinalIgnoreCase);
                transaction.FailureCode = insufficient ? "INSUFFICIENT_BALANCE" : "WALLET_POSTING_FAILED";
                transaction.FailureMessage = insufficient ? "Insufficient wallet balance." : "Wallet posting failed.";
                transaction.DebitAmount = 0;
                transaction.CreditAmount = 0;
                transaction.CommissionAmount = 0;
                transaction.CompletedAtUtc = DateTime.UtcNow;
            }
        }

        dbContext.Receipts.Add(new Receipt
        {
            ReceiptId = Guid.NewGuid(),
            ReceiptNumber = $"RCP-{reference}",
            ServiceTransactionId = transaction.ServiceTransactionId,
            IssuedToUserId = transaction.UserId,
            IssuedAtUtc = DateTime.UtcNow,
            SnapshotJson = JsonSerializer.Serialize(new { transaction.TransactionReference, transaction.Status, transaction.Amount, transaction.ProviderReference })
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(transaction, await ReceiptNumberAsync(transaction.ServiceTransactionId, cancellationToken));
    }

    public async Task<IReadOnlyList<RechargeOperatorResponse>> OperatorsAsync(
        string type,
        CancellationToken cancellationToken = default) =>
        await dbContext.RechargeOperators.AsNoTracking()
            .Where(item => item.Type == type && item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => new RechargeOperatorResponse(item.Name, item.Label, item.Type, item.OperatorKey))
            .ToListAsync(cancellationToken);

    private async Task EnsureWalletAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await dbContext.LedgerAccounts.AnyAsync(item => item.UserId == userId && item.Code == $"USER:{userId}:WALLET" && item.IsActive, cancellationToken)) return;
        dbContext.LedgerAccounts.Add(new LedgerAccount
        {
            LedgerAccountId = Guid.NewGuid(), UserId = userId, Code = $"USER:{userId}:WALLET", Name = "User wallet",
            AccountType = "LIABILITY", NormalBalance = "C", Currency = "INR", IsActive = true, CreatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task DebitWalletAsync(ServiceTransaction transaction, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC finance.PostRechargeWalletDebit {Guid.NewGuid()}, {transaction.ServiceTransactionId}, {"RECHARGE:" + transaction.TransactionReference}, {transaction.TransactionReference}, {transaction.UserId}, {transaction.DebitAmount}, {transaction.CommissionAmount}",
            cancellationToken);
    }

    private async Task<string?> ReceiptNumberAsync(Guid transactionId, CancellationToken cancellationToken) =>
        await dbContext.Receipts.AsNoTracking().Where(item => item.ServiceTransactionId == transactionId)
            .Select(item => item.ReceiptNumber).SingleOrDefaultAsync(cancellationToken);

    private RechargeResponse ToResponse(ServiceTransaction transaction, string? receiptNumber) => new(
        transaction.ServiceTransactionId, transaction.TransactionReference, transaction.Status,
        provider.Mode.ToString().ToUpperInvariant(), transaction.ProviderReference, "MASKED",
        transaction.Amount, transaction.ChargeAmount, transaction.CommissionAmount, transaction.DebitAmount,
        transaction.FailureMessage, receiptNumber);

    private static string Mask(string value) => value.Length <= 4 ? "****" : $"{new string('*', value.Length - 4)}{value[^4..]}";
}
