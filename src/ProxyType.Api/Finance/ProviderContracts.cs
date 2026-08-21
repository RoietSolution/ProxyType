namespace ProxyType.Api.Finance;

public enum ProviderMode
{
    Mock,
    Test,
    Live
}

public enum NormalizedTransactionStatus
{
    Created,
    Pending,
    Succeeded,
    Failed,
    Reversed,
    Cancelled
}

public sealed record FinoDmtProviderRequest(
    string AccountNumber,
    string IfscCode,
    string BeneficiaryName,
    string Mobile,
    decimal Amount,
    string TransferMode,
    string Comments,
    string TransactionReference);

public sealed record FinoDmtProviderResult(
    NormalizedTransactionStatus Status,
    string RawStatus,
    string? ProviderReference,
    string? FailureReason);

public interface IFinoDmtProvider
{
    ProviderMode Mode { get; }
    Task<FinoDmtProviderResult> TransferAsync(FinoDmtProviderRequest request, CancellationToken cancellationToken);
}

public sealed class MockFinoDmtProvider(IConfiguration configuration) : IFinoDmtProvider
{
    public ProviderMode Mode => ProviderMode.Mock;

    public Task<FinoDmtProviderResult> TransferAsync(
        FinoDmtProviderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var simulatedStatus = configuration["FinoDmt:MockResult"]?.Trim().ToUpperInvariant();
        var result = simulatedStatus switch
        {
            "FAILED" => new FinoDmtProviderResult(NormalizedTransactionStatus.Failed, "MOCK_FAILED", null, "Simulated provider failure."),
            "PENDING" => new FinoDmtProviderResult(NormalizedTransactionStatus.Pending, "MOCK_PENDING", $"MOCK-{request.TransactionReference}", null),
            _ => new FinoDmtProviderResult(NormalizedTransactionStatus.Succeeded, "MOCK_SUCCESS", $"MOCK-{request.TransactionReference}", null)
        };
        return Task.FromResult(result);
    }
}

public sealed class DisabledLiveFinoDmtProvider : IFinoDmtProvider
{
    public ProviderMode Mode => ProviderMode.Live;

    public Task<FinoDmtProviderResult> TransferAsync(
        FinoDmtProviderRequest request,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Fino DMT LIVE provider is not configured or enabled.");
}

public sealed record UpiFundingOrderRequest(decimal Amount, string TransactionReference);
public sealed record UpiFundingStatusRequest(string TransactionReference, string? ProviderReference);
public sealed record UpiFundingProviderResult(NormalizedTransactionStatus Status, string RawStatus, string? ProviderReference, string? PaymentUrl, string? QrCode, string? FailureReason);
public interface IUpiTransferProvider
{
    ProviderMode Mode { get; }
    Task<UpiFundingProviderResult> CreateOrderAsync(UpiFundingOrderRequest request, CancellationToken cancellationToken);
    Task<UpiFundingProviderResult> GetStatusAsync(UpiFundingStatusRequest request, CancellationToken cancellationToken);
}

public sealed class MockUpiTransferProvider(IConfiguration configuration) : IUpiTransferProvider
{
    public ProviderMode Mode => ProviderMode.Mock;
    public Task<UpiFundingProviderResult> CreateOrderAsync(UpiFundingOrderRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var providerReference = $"MOCK-{request.TransactionReference}";
        var result = configuration["UpiTransfer:MockOrderResult"]?.Trim().ToUpperInvariant() switch
        {
            "FAILED" => new UpiFundingProviderResult(NormalizedTransactionStatus.Failed, "MOCK_ORDER_FAILED", providerReference, null, null, "Simulated hosted order creation failure."),
            "EXPIRED" => new UpiFundingProviderResult(NormalizedTransactionStatus.Cancelled, "MOCK_EXPIRED", providerReference, null, null, "Simulated hosted order expiry."),
            _ => new UpiFundingProviderResult(NormalizedTransactionStatus.Created, "MOCK_ORDER_CREATED", providerReference, $"https://mock.invalid/upi/pay/{request.TransactionReference}", configuration["UpiTransfer:MockQrCode"], null)
        };
        return Task.FromResult(result);
    }
    public Task<UpiFundingProviderResult> GetStatusAsync(UpiFundingStatusRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var providerReference = request.ProviderReference ?? $"MOCK-{request.TransactionReference}";
        var result = configuration["UpiTransfer:MockStatus"]?.Trim().ToUpperInvariant() switch
        {
            "SUCCESS" => new UpiFundingProviderResult(NormalizedTransactionStatus.Succeeded, "MOCK_SUCCESS", providerReference, null, null, null),
            "FAILED" => new UpiFundingProviderResult(NormalizedTransactionStatus.Failed, "MOCK_FAILED", providerReference, null, null, "Simulated payment failure."),
            "EXPIRED" => new UpiFundingProviderResult(NormalizedTransactionStatus.Cancelled, "MOCK_EXPIRED", providerReference, null, null, "Simulated payment expiry."),
            _ => new UpiFundingProviderResult(NormalizedTransactionStatus.Pending, "MOCK_PENDING", providerReference, null, null, null)
        };
        return Task.FromResult(result);
    }
}

public sealed class DisabledLiveUpiTransferProvider : IUpiTransferProvider
{
    public ProviderMode Mode => ProviderMode.Live;
    public Task<UpiFundingProviderResult> CreateOrderAsync(UpiFundingOrderRequest request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("LIVE UPI funding provider is disabled until a verified provider contract and credentials are configured.");
    public Task<UpiFundingProviderResult> GetStatusAsync(UpiFundingStatusRequest request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("LIVE UPI funding provider is disabled until a verified provider contract and credentials are configured.");
}

public sealed record RechargeProviderRequest(
    string Account,
    decimal Amount,
    string Operator,
    string RechargeType,
    string Pincode,
    decimal Latitude,
    decimal Longitude,
    string TransactionReference);

public sealed record RechargeProviderResult(
    NormalizedTransactionStatus Status,
    string RawStatus,
    string? ProviderReference,
    string? FailureReason);

public interface IRechargeProvider
{
    ProviderMode Mode { get; }
    Task<RechargeProviderResult> RechargeAsync(RechargeProviderRequest request, CancellationToken cancellationToken);
}

public sealed class MockRechargeProvider(IConfiguration configuration) : IRechargeProvider
{
    public ProviderMode Mode => ProviderMode.Mock;

    public Task<RechargeProviderResult> RechargeAsync(
        RechargeProviderRequest request,
        CancellationToken cancellationToken)
    {
        var simulatedStatus = configuration["Recharge:MockResult"]?.Trim().ToUpperInvariant();
        var result = simulatedStatus switch
        {
            "FAILED" => new RechargeProviderResult(NormalizedTransactionStatus.Failed, "FAILED", null, "Simulated recharge failure."),
            "PENDING" => new RechargeProviderResult(NormalizedTransactionStatus.Pending, "PENDING", $"MOCK-{request.TransactionReference}", null),
            _ => new RechargeProviderResult(NormalizedTransactionStatus.Succeeded, "SUCCESS", $"MOCK-{request.TransactionReference}", null)
        };
        return Task.FromResult(result);
    }
}

public sealed class DisabledLiveRechargeProvider : IRechargeProvider
{
    public ProviderMode Mode => ProviderMode.Live;

    public Task<RechargeProviderResult> RechargeAsync(
        RechargeProviderRequest request,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("LIVE Recharge provider is disabled until a verified provider contract and credentials are configured.");
}

public sealed record AepsProviderRequest(string TransactionType, string AadhaarNumber, string MobileNumber,
    string BankIin, string DeviceName, string BiometricCaptureReference, decimal? Amount, string TransactionReference);
public sealed record AepsProviderResult(NormalizedTransactionStatus Status, string RawStatus, string? ProviderReference, string? FailureReason);
public interface IAepsProvider { ProviderMode Mode { get; } Task<AepsProviderResult> ExecuteAsync(AepsProviderRequest request, CancellationToken cancellationToken); }

public sealed class MockAepsProvider(IConfiguration configuration) : IAepsProvider
{
    public ProviderMode Mode => ProviderMode.Mock;
    public Task<AepsProviderResult> ExecuteAsync(AepsProviderRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = configuration["Aeps:MockResult"]?.Trim().ToUpperInvariant() switch
        {
            "FAILED" => new AepsProviderResult(NormalizedTransactionStatus.Failed, "MOCK_FAILED", null, "Simulated AEPS provider failure."),
            "PENDING" => new AepsProviderResult(NormalizedTransactionStatus.Pending, "MOCK_PENDING", $"MOCK-{request.TransactionReference}", null),
            _ => new AepsProviderResult(NormalizedTransactionStatus.Succeeded, "MOCK_SUCCESS", $"MOCK-{request.TransactionReference}", null)
        };
        return Task.FromResult(result);
    }
}

public sealed class DisabledLiveAepsProvider : IAepsProvider
{
    public ProviderMode Mode => ProviderMode.Live;
    public Task<AepsProviderResult> ExecuteAsync(AepsProviderRequest request, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("LIVE AEPS provider is disabled until a verified provider contract and credentials are configured.");
}
