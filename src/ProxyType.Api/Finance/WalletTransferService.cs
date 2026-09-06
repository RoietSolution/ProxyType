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

public sealed class WalletTransferService(
    ProxyTypeDbContext dbContext,
    ICurrentScopeService scopeService,
    ServicePermissionService permissionService,
    PricingService pricingService)
{
    public async Task<WalletTransferReceiverResponse?> FindReceiverAsync(string mobile, CancellationToken cancellationToken = default)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(mobile, "^\\d{10}$")) return null;
        var accessible = await scopeService.GetAccessibleOrganizationIdsAsync(cancellationToken);
        var now = DateTime.UtcNow;
        return await dbContext.Users.AsNoTracking()
            .Where(user => user.Mobile == mobile && user.IsActive)
            .Join(dbContext.OrganizationMemberships.AsNoTracking().Where(m => m.IsActive && (m.ValidToUtc == null || m.ValidToUtc > now)),
                user => user.UserId, membership => membership.UserId, (user, membership) => new { user, membership })
            .Join(dbContext.OrganizationUnits.AsNoTracking().Where(unit => unit.Status == "ACTIVE" && accessible.Contains(unit.OrganizationUnitId)),
                item => item.membership.OrganizationUnitId, unit => unit.OrganizationUnitId,
                (item, unit) => new WalletTransferReceiverResponse(item.user.UserId, unit.OrganizationUnitId, item.user.DisplayName, item.user.Mobile!, unit.Code, unit.UnitType))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<WalletTransferResponse> TransferAsync(WalletTransferRequest request, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var senderMembership = await dbContext.OrganizationMemberships.AsNoTracking()
            .Where(m => m.UserId == scopeService.UserId && m.IsActive && (m.ValidToUtc == null || m.ValidToUtc > now))
            .OrderByDescending(m => m.IsPrimary).FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("An active organization assignment is required.");
        var accessible = await scopeService.GetAccessibleOrganizationIdsAsync(cancellationToken);
        if (!accessible.Contains(senderMembership.OrganizationUnitId)) throw new UnauthorizedAccessException("USER_INACTIVE_OR_OUT_OF_SCOPE");
        var service = await dbContext.Services.SingleOrDefaultAsync(s => s.Code == "wallet_transfer" && s.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("Wallet to Wallet is not deployed in the service catalog.");
        var decision = (await permissionService.GetEffectiveAsync(senderMembership.OrganizationUnitId, cancellationToken)).SingleOrDefault(s => s.ServiceId == service.ServiceId);
        if (decision is null || !decision.IsAllowed) throw new UnauthorizedAccessException(decision?.DecisionReason ?? "SERVICE_NOT_ASSIGNED");
        var receiver = await FindReceiverAsync(request.ReceiverMobile, cancellationToken)
            ?? throw new KeyNotFoundException("The receiver is not active or is outside your permitted hierarchy.");
        if (receiver.UserId == scopeService.UserId) throw new InvalidOperationException("Self-transfer is not allowed.");
        var existing = await dbContext.ServiceTransactions.SingleOrDefaultAsync(t => t.OrganizationUnitId == senderMembership.OrganizationUnitId && t.ClientIdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null) return await ToResponseAsync(existing, receiver.DisplayName, request.ReceiverMobile, cancellationToken);

        var charge = await pricingService.ChargeAsync(service.ServiceId, scopeService.UserId, request.Amount, cancellationToken);
        var reference = $"WTW{DateTime.UtcNow:yyyyMMddHHmmssfff}{Random.Shared.Next(100, 999)}";
        var transaction = new ServiceTransaction
        {
            ServiceTransactionId = Guid.NewGuid(), OrganizationUnitId = senderMembership.OrganizationUnitId, UserId = scopeService.UserId,
            ServiceId = service.ServiceId, CounterpartyUserId = receiver.UserId, CounterpartyOrganizationUnitId = receiver.OrganizationUnitId,
            TransactionReference = reference, ClientIdempotencyKey = request.IdempotencyKey, Status = "CREATED", Amount = request.Amount,
            ChargeAmount = charge, DebitAmount = request.Amount + charge, CreditAmount = request.Amount,
            RequestSummaryJson = JsonSerializer.Serialize(new { receiver.UserId, ReceiverMobile = request.ReceiverMobile, request.Amount }),
            RequestAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now
        };
        dbContext.ServiceTransactions.Add(transaction);
        await dbContext.SaveChangesAsync(cancellationToken);
        try
        {
            await EnsureWalletAsync(scopeService.UserId, cancellationToken);
            await EnsureWalletAsync(receiver.UserId, cancellationToken);
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"EXEC finance.PostWalletToWalletTransfer {Guid.NewGuid()}, {transaction.ServiceTransactionId}, {"WALLET_TRANSFER:" + transaction.TransactionReference}, {transaction.TransactionReference}, {scopeService.UserId}, {receiver.UserId}, {request.Amount}, {charge}", cancellationToken);
            transaction.Status = "SUCCEEDED";
        }
        catch (DbException exception)
        {
            transaction.Status = "FAILED";
            transaction.FailureCode = exception is SqlException sql && sql.Number == 51022 ? "INSUFFICIENT_BALANCE" : "TRANSFER_POSTING_FAILED";
            transaction.FailureMessage = transaction.FailureCode == "INSUFFICIENT_BALANCE" ? "Insufficient wallet balance." : "Wallet transfer could not be posted.";
            transaction.DebitAmount = 0; transaction.CreditAmount = 0;
        }
        transaction.ResponseAtUtc = DateTime.UtcNow; transaction.CompletedAtUtc = transaction.ResponseAtUtc; transaction.UpdatedAtUtc = transaction.ResponseAtUtc.Value;
        dbContext.Receipts.Add(new Receipt { ReceiptId = Guid.NewGuid(), ReceiptNumber = $"RCP-{reference}", ServiceTransactionId = transaction.ServiceTransactionId, IssuedToUserId = transaction.UserId, IssuedAtUtc = DateTime.UtcNow, SnapshotJson = JsonSerializer.Serialize(new { transaction.TransactionReference, transaction.Status, transaction.Amount, transaction.ChargeAmount, transaction.DebitAmount, Receiver = receiver.DisplayName }) });
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ToResponseAsync(transaction, receiver.DisplayName, request.ReceiverMobile, cancellationToken);
    }

    private async Task EnsureWalletAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await dbContext.LedgerAccounts.AnyAsync(a => a.UserId == userId && a.Code == $"USER:{userId}:WALLET" && a.IsActive, cancellationToken)) return;
        dbContext.LedgerAccounts.Add(new LedgerAccount { LedgerAccountId = Guid.NewGuid(), UserId = userId, Code = $"USER:{userId}:WALLET", Name = "User wallet", AccountType = "LIABILITY", NormalBalance = "C", Currency = "INR", IsActive = true, CreatedAtUtc = DateTime.UtcNow });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<WalletTransferResponse> ToResponseAsync(ServiceTransaction transaction, string receiverName, string receiverMobile, CancellationToken cancellationToken) =>
        new(transaction.ServiceTransactionId, transaction.TransactionReference, transaction.Status, receiverName, receiverMobile, transaction.Amount, transaction.ChargeAmount, transaction.DebitAmount, transaction.CreditAmount, transaction.FailureMessage, await dbContext.Receipts.Where(r => r.ServiceTransactionId == transaction.ServiceTransactionId).Select(r => r.ReceiptNumber).SingleOrDefaultAsync(cancellationToken));
}
