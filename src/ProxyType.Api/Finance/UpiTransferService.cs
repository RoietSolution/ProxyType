using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProxyType.Api.Contracts;
using ProxyType.Api.Data;
using ProxyType.Api.Domain;
using ProxyType.Api.Security;
using ProxyType.Api.Services;

namespace ProxyType.Api.Finance;

public sealed class UpiTransferService(
    ProxyTypeDbContext db,
    ICurrentScopeService scope,
    ServicePermissionService permissions,
    IUpiTransferProvider provider,
    IConfiguration configuration)
{
    public async Task<UpiFundingResponse> CreateOrderAsync(UpiFundingRequest request, CancellationToken ct = default)
    {
        var unitId = await PrimaryUnitAsync(ct);
        var (service, providerId) = await AuthorizedServiceAsync(unitId, ct);
        var existing = await db.ServiceTransactions.SingleOrDefaultAsync(x => x.OrganizationUnitId == unitId && x.ClientIdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null) return ToResponse(existing, await ReceiptAsync(existing.ServiceTransactionId, ct));

        var now = DateTime.UtcNow;
        var reference = $"PTU{now:yyyyMMddHHmmssfff}{Random.Shared.Next(100, 999)}";
        var tx = new ServiceTransaction
        {
            ServiceTransactionId = Guid.NewGuid(), OrganizationUnitId = unitId, UserId = scope.UserId,
            ServiceId = service.ServiceId, ProviderId = providerId, TransactionReference = reference,
            ClientIdempotencyKey = request.IdempotencyKey, Status = "CREATED", Amount = request.Amount,
            RequestSummaryJson = JsonSerializer.Serialize(new { request.Amount, PaymentUrl = (string?)null, QrCode = (string?)null }),
            RequestAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now
        };
        db.ServiceTransactions.Add(tx);
        await db.SaveChangesAsync(ct);

        UpiFundingProviderResult result;
        try { result = await provider.CreateOrderAsync(new(request.Amount, reference), ct); }
        catch (InvalidOperationException ex)
        {
            tx.Status = "FAILED"; tx.FailureCode = "PROVIDER_DISABLED"; tx.FailureMessage = "UPI funding provider is disabled.";
            tx.ResponseAtUtc = tx.UpdatedAtUtc = DateTime.UtcNow; tx.CompletedAtUtc = tx.ResponseAtUtc;
            await db.SaveChangesAsync(ct); throw new InvalidOperationException(ex.Message);
        }

        SaveOrderResult(tx, result);
        await db.SaveChangesAsync(ct);
        await EnsureReceiptAsync(tx, ct);
        db.AuditLogs.Add(new AuditLog { ActorUserId = scope.UserId, OrganizationUnitId = unitId, Action = "UPI_FUNDING_ORDER_CREATED", EntityType = "ServiceTransaction", EntityId = tx.ServiceTransactionId.ToString(), CorrelationId = Guid.NewGuid(), DetailsJson = JsonSerializer.Serialize(new { tx.Amount, tx.Status, ProviderMode = provider.Mode.ToString().ToUpperInvariant() }), OccurredAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        return ToResponse(tx, await ReceiptAsync(tx.ServiceTransactionId, ct));
    }

    public async Task<UpiFundingResponse> CheckStatusAsync(Guid transactionId, CancellationToken ct = default)
    {
        var tx = await ScopedTransactionAsync(transactionId, ct);
        if (tx.ProviderId is null) throw new InvalidOperationException("UPI funding provider route is missing.");
        var result = await provider.GetStatusAsync(new(tx.TransactionReference, tx.ProviderReference), ct);
        await ApplyProviderResultAsync(tx, result, $"STATUS-{Guid.NewGuid():N}", ct);
        return await ReloadResponseAsync(transactionId, ct);
    }

    public async Task<UpiFundingResponse> HandleMockCallbackAsync(UpiMockCallbackRequest request, string? callbackKey, CancellationToken ct = default)
    {
        var expected = configuration["UpiTransfer:MockCallbackKey"];
        if (string.IsNullOrWhiteSpace(expected) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(callbackKey ?? string.Empty)))
            throw new UnauthorizedAccessException("MOCK callback authentication failed.");
        var tx = await db.ServiceTransactions.SingleOrDefaultAsync(x => x.TransactionReference == request.TransactionReference, ct)
            ?? throw new KeyNotFoundException("Funding transaction was not found.");
        var status = request.Status.ToUpperInvariant() switch
        {
            "SUCCESS" => NormalizedTransactionStatus.Succeeded,
            "FAILED" => NormalizedTransactionStatus.Failed,
            "EXPIRED" => NormalizedTransactionStatus.Cancelled,
            _ => NormalizedTransactionStatus.Pending
        };
        var result = new UpiFundingProviderResult(status, "MOCK_" + request.Status.ToUpperInvariant(), request.ProviderReference ?? tx.ProviderReference, null, null, status == NormalizedTransactionStatus.Failed ? "Simulated payment failure." : status == NormalizedTransactionStatus.Cancelled ? "Simulated payment expiry." : null);
        await ApplyProviderResultAsync(tx, result, request.EventId, ct);
        return await ReloadResponseAsync(tx.ServiceTransactionId, ct);
    }

    private async Task ApplyProviderResultAsync(ServiceTransaction tx, UpiFundingProviderResult result, string externalEventId, CancellationToken ct)
    {
        await EnsureWalletAsync(tx.UserId, ct);
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{tx.TransactionReference}|{externalEventId}|{result.RawStatus}|{result.ProviderReference}")));
        await db.Database.ExecuteSqlInterpolatedAsync($"EXEC finance.ApplyUpiFundingProviderResult {tx.ProviderId}, {tx.ServiceTransactionId}, {tx.UserId}, {externalEventId}, {result.RawStatus}, {result.Status.ToString().ToUpperInvariant()}, {result.ProviderReference}, {result.FailureReason}, {payloadHash}, {tx.TransactionReference}", ct);
    }

    private static void SaveOrderResult(ServiceTransaction tx, UpiFundingProviderResult result)
    {
        tx.Status = result.Status == NormalizedTransactionStatus.Created ? "PENDING" : result.Status.ToString().ToUpperInvariant();
        tx.ProviderStatus = result.RawStatus; tx.ProviderReference = result.ProviderReference; tx.FailureMessage = result.FailureReason;
        tx.RequestSummaryJson = JsonSerializer.Serialize(new { tx.Amount, result.PaymentUrl, result.QrCode });
        tx.ResponseAtUtc = tx.UpdatedAtUtc = DateTime.UtcNow;
        if (result.Status is NormalizedTransactionStatus.Failed or NormalizedTransactionStatus.Cancelled) tx.CompletedAtUtc = tx.ResponseAtUtc;
    }

    private async Task EnsureReceiptAsync(ServiceTransaction tx, CancellationToken ct)
    {
        if (!await db.Receipts.AnyAsync(x => x.ServiceTransactionId == tx.ServiceTransactionId, ct))
            db.Receipts.Add(new Receipt { ReceiptId = Guid.NewGuid(), ReceiptNumber = $"RCP-{tx.TransactionReference}", ServiceTransactionId = tx.ServiceTransactionId, IssuedToUserId = tx.UserId, IssuedAtUtc = DateTime.UtcNow, SnapshotJson = JsonSerializer.Serialize(new { tx.TransactionReference, tx.Status, tx.Amount, tx.ProviderReference }) });
    }

    private async Task EnsureWalletAsync(Guid userId, CancellationToken ct)
    {
        if (!await db.LedgerAccounts.AnyAsync(x => x.UserId == userId && x.Code == $"USER:{userId}:WALLET" && x.IsActive, ct))
        {
            db.LedgerAccounts.Add(new LedgerAccount { LedgerAccountId = Guid.NewGuid(), UserId = userId, Code = $"USER:{userId}:WALLET", Name = "User wallet", AccountType = "LIABILITY", NormalBalance = "C", Currency = "INR", IsActive = true, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<(FinancialService Service, Guid ProviderId)> AuthorizedServiceAsync(Guid unitId, CancellationToken ct)
    {
        var service = await db.Services.SingleOrDefaultAsync(x => x.Code == "upi_transfer" && x.IsActive, ct) ?? throw new InvalidOperationException("UPI Add Money is not deployed in the service catalog.");
        var decision = (await permissions.GetEffectiveAsync(unitId, ct)).SingleOrDefault(x => x.ServiceId == service.ServiceId);
        if (decision is null || !decision.IsAllowed) throw new UnauthorizedAccessException(decision?.DecisionReason ?? "SERVICE_NOT_ASSIGNED");
        var providerId = await db.ServiceProviderRoutes.AsNoTracking().Where(x => x.ServiceId == service.ServiceId && x.IsEnabled && x.Provider.IsEnabled).OrderBy(x => x.Priority).Select(x => (Guid?)x.ProviderId).FirstOrDefaultAsync(ct) ?? throw new InvalidOperationException("UPI funding provider route is not configured.");
        return (service, providerId);
    }

    private async Task<Guid> PrimaryUnitAsync(CancellationToken ct)
    {
        var id = await db.OrganizationMemberships.AsNoTracking().Where(x => x.UserId == scope.UserId && x.IsActive && (x.ValidToUtc == null || x.ValidToUtc > DateTime.UtcNow)).OrderByDescending(x => x.IsPrimary).Select(x => x.OrganizationUnitId).FirstOrDefaultAsync(ct);
        return id != Guid.Empty && (await scope.GetAccessibleOrganizationIdsAsync(ct)).Contains(id) ? id : throw new UnauthorizedAccessException("USER_INACTIVE_OR_OUT_OF_SCOPE");
    }

    private async Task<ServiceTransaction> ScopedTransactionAsync(Guid id, CancellationToken ct)
    {
        var tx = await db.ServiceTransactions.SingleOrDefaultAsync(x => x.ServiceTransactionId == id, ct) ?? throw new KeyNotFoundException("Funding transaction was not found.");
        var accessible = await scope.GetAccessibleOrganizationIdsAsync(ct);
        if (!accessible.Contains(tx.OrganizationUnitId)) throw new UnauthorizedAccessException("USER_INACTIVE_OR_OUT_OF_SCOPE");
        await AuthorizedServiceAsync(tx.OrganizationUnitId, ct);
        return tx;
    }

    private async Task<UpiFundingResponse> ReloadResponseAsync(Guid id, CancellationToken ct)
    {
        var tx = await db.ServiceTransactions.AsNoTracking().SingleAsync(x => x.ServiceTransactionId == id, ct);
        return ToResponse(tx, await ReceiptAsync(id, ct));
    }

    private async Task<string?> ReceiptAsync(Guid id, CancellationToken ct) => await db.Receipts.AsNoTracking().Where(x => x.ServiceTransactionId == id).Select(x => x.ReceiptNumber).SingleOrDefaultAsync(ct);

    private UpiFundingResponse ToResponse(ServiceTransaction tx, string? receipt)
    {
        var summary = JsonSerializer.Deserialize<JsonElement>(tx.RequestSummaryJson ?? "{}");
        var paymentUrl = summary.TryGetProperty("PaymentUrl", out var url) && url.ValueKind != JsonValueKind.Null ? url.GetString() : null;
        var qrCode = summary.TryGetProperty("QrCode", out var qr) && qr.ValueKind != JsonValueKind.Null ? qr.GetString() : null;
        return new(tx.ServiceTransactionId, tx.TransactionReference, tx.Status, provider.Mode.ToString().ToUpperInvariant(), tx.ProviderReference, paymentUrl, qrCode, tx.Amount, tx.ChargeAmount, tx.CommissionAmount, tx.CreditAmount, tx.Status == "SUCCEEDED" && tx.CreditAmount > 0, tx.FailureMessage, receipt);
    }
}
