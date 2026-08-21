using System.Text.Json;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using ProxyType.Api.Contracts;
using ProxyType.Api.Data;
using ProxyType.Api.Domain;
using ProxyType.Api.Security;
using ProxyType.Api.Services;

namespace ProxyType.Api.Finance;

public sealed class FinoDmtService(
    ProxyTypeDbContext dbContext,
    ICurrentScopeService scopeService,
    ServicePermissionService permissionService,
    IFinoDmtProvider provider)
{
    public async Task<FinoDmtResponse> TransferAsync(
        FinoDmtRequest request,
        CancellationToken cancellationToken = default)
    {
        var primaryMembership = await dbContext.OrganizationMemberships.AsNoTracking()
            .Where(membership => membership.UserId == scopeService.UserId && membership.IsActive &&
                (membership.ValidToUtc == null || membership.ValidToUtc > DateTime.UtcNow))
            .OrderByDescending(membership => membership.IsPrimary)
            .Select(membership => new { membership.OrganizationUnitId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("An active organization assignment is required.");
        if (!(await scopeService.GetAccessibleOrganizationIdsAsync(cancellationToken)).Contains(primaryMembership.OrganizationUnitId))
        {
            throw new UnauthorizedAccessException("USER_INACTIVE_OR_OUT_OF_SCOPE");
        }

        var service = await dbContext.Services.SingleOrDefaultAsync(
            candidate => candidate.Code == "fino_dmt", cancellationToken)
            ?? throw new InvalidOperationException("Fino DMT is not deployed in the service catalog.");
        var providerRoute = await dbContext.ServiceProviderRoutes.AsNoTracking()
            .Where(route => route.ServiceId == service.ServiceId && route.IsEnabled && route.Provider.IsEnabled)
            .OrderBy(route => route.Priority)
            .Select(route => new { route.ProviderId })
            .FirstOrDefaultAsync(cancellationToken);
        var effective = await permissionService.GetEffectiveAsync(primaryMembership.OrganizationUnitId, cancellationToken);
        var decision = effective.SingleOrDefault(item => item.ServiceId == service.ServiceId);
        if (decision is null || !decision.IsAllowed)
        {
            throw new UnauthorizedAccessException(decision?.DecisionReason ?? "SERVICE_NOT_ASSIGNED");
        }

        var existing = await dbContext.ServiceTransactions.SingleOrDefaultAsync(
            transaction => transaction.OrganizationUnitId == primaryMembership.OrganizationUnitId &&
                transaction.ClientIdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return ToResponse(existing, await ReceiptNumberAsync(existing.ServiceTransactionId, cancellationToken));
        }

        var now = DateTime.UtcNow;
        var transactionReference = $"PTF{now:yyyyMMddHHmmssfff}{Random.Shared.Next(100, 999)}";
        var transaction = new ServiceTransaction
        {
            ServiceTransactionId = Guid.NewGuid(),
            OrganizationUnitId = primaryMembership.OrganizationUnitId,
            UserId = scopeService.UserId,
            ServiceId = service.ServiceId,
            ProviderId = providerRoute?.ProviderId,
            TransactionReference = transactionReference,
            ClientIdempotencyKey = request.IdempotencyKey,
            Status = "CREATED",
            Amount = request.Amount,
            ChargeAmount = 0,
            CommissionAmount = 0,
            TaxAmount = 0,
            DebitAmount = 0,
            CreditAmount = 0,
            RequestSummaryJson = JsonSerializer.Serialize(new
            {
                request.IfscCode,
                AccountNumber = Mask(request.AccountNumber),
                request.BeneficiaryName,
                Mobile = Mask(request.Mobile),
                request.Amount,
                request.TransferMode
            }),
            RequestAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Remarks = request.Comments
        };
        dbContext.ServiceTransactions.Add(transaction);
        await dbContext.SaveChangesAsync(cancellationToken);

        FinoDmtProviderResult providerResult;
        try
        {
            providerResult = await provider.TransferAsync(new FinoDmtProviderRequest(
                request.AccountNumber, request.IfscCode, request.BeneficiaryName, request.Mobile,
                request.Amount, request.TransferMode, request.Comments, transactionReference), cancellationToken);
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

        transaction.Status = providerResult.Status.ToString().ToUpperInvariant();
        transaction.ProviderStatus = providerResult.RawStatus;
        transaction.ProviderReference = providerResult.ProviderReference;
        transaction.FailureMessage = providerResult.FailureReason;
        transaction.ResponseAtUtc = DateTime.UtcNow;
        transaction.UpdatedAtUtc = transaction.ResponseAtUtc.Value;
        if (providerResult.Status is NormalizedTransactionStatus.Succeeded or NormalizedTransactionStatus.Failed)
        {
            transaction.CompletedAtUtc = transaction.ResponseAtUtc;
        }

        if (providerResult.Status == NormalizedTransactionStatus.Succeeded)
        {
            transaction.DebitAmount = request.Amount;
            try
            {
                await EnsureWalletAccountAsync(transaction.UserId, cancellationToken);
                await DebitWalletAsync(transaction, cancellationToken);
            }
            catch (DbException exception)
            {
                transaction.Status = "FAILED";
                var insufficientBalance = exception is SqlException sqlException && sqlException.Number == 51022
                    || exception.Message.Contains("Insufficient", StringComparison.OrdinalIgnoreCase);
                transaction.FailureCode = insufficientBalance
                    ? "INSUFFICIENT_BALANCE" : "WALLET_POSTING_FAILED";
                transaction.FailureMessage = transaction.FailureCode == "INSUFFICIENT_BALANCE"
                    ? "Insufficient wallet balance." : "Wallet posting failed.";
                transaction.DebitAmount = 0;
                transaction.CompletedAtUtc = DateTime.UtcNow;
            }
        }

        dbContext.Receipts.Add(new Receipt
        {
            ReceiptId = Guid.NewGuid(),
            ReceiptNumber = $"RCP-{transactionReference}",
            ServiceTransactionId = transaction.ServiceTransactionId,
            IssuedToUserId = transaction.UserId,
            IssuedAtUtc = DateTime.UtcNow,
            SnapshotJson = JsonSerializer.Serialize(new
            {
                transaction.TransactionReference,
                transaction.Status,
                transaction.Amount,
                transaction.ProviderReference
            })
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(transaction, await ReceiptNumberAsync(transaction.ServiceTransactionId, cancellationToken));
    }

    private async Task EnsureWalletAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        var account = await dbContext.LedgerAccounts.SingleOrDefaultAsync(
            candidate => candidate.UserId == userId && candidate.Code == $"USER:{userId}:WALLET" && candidate.IsActive,
            cancellationToken);
        if (account is null)
        {
            dbContext.LedgerAccounts.Add(new LedgerAccount
            {
                LedgerAccountId = Guid.NewGuid(),
                UserId = userId,
                Code = $"USER:{userId}:WALLET",
                Name = "User wallet",
                AccountType = "LIABILITY",
                NormalBalance = "C",
                Currency = "INR",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task DebitWalletAsync(ServiceTransaction transaction, CancellationToken cancellationToken)
    {
        var journalId = Guid.NewGuid();
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"EXEC finance.PostWalletDebit {journalId}, {transaction.ServiceTransactionId}, {"FINO-DMT:" + transaction.TransactionReference}, {transaction.TransactionReference}, {transaction.UserId}, {transaction.DebitAmount}",
            cancellationToken);
    }

    private async Task<string?> ReceiptNumberAsync(Guid transactionId, CancellationToken cancellationToken) =>
        await dbContext.Receipts.AsNoTracking()
            .Where(receipt => receipt.ServiceTransactionId == transactionId)
            .Select(receipt => receipt.ReceiptNumber)
            .SingleOrDefaultAsync(cancellationToken);

    private FinoDmtResponse ToResponse(ServiceTransaction transaction, string? receiptNumber) => new(
        transaction.ServiceTransactionId,
        transaction.TransactionReference,
        transaction.Status,
        provider.Mode.ToString().ToUpperInvariant(),
        transaction.ProviderReference,
        transaction.Amount,
        transaction.ChargeAmount,
        transaction.CommissionAmount,
        transaction.FailureMessage,
        receiptNumber);

    private static string Mask(string value) => value.Length <= 4 ? "****" : $"{new string('*', value.Length - 4)}{value[^4..]}";
}
