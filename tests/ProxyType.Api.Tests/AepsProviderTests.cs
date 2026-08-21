using Microsoft.Extensions.Configuration;
using ProxyType.Api.Contracts;
using ProxyType.Api.Finance;
using System.ComponentModel.DataAnnotations;

namespace ProxyType.Api.Tests;

public sealed class AepsProviderTests
{
    [Fact]
    public async Task MockProviderDefaultsToSuccessAndNeverNeedsExternalCall()
    {
        var provider = new MockAepsProvider(new ConfigurationBuilder().Build());
        var result = await provider.ExecuteAsync(Request(), CancellationToken.None);
        Assert.Equal(ProviderMode.Mock, provider.Mode);
        Assert.Equal(NormalizedTransactionStatus.Succeeded, result.Status);
        Assert.StartsWith("MOCK-", result.ProviderReference);
    }

    [Theory]
    [InlineData("FAILED", NormalizedTransactionStatus.Failed)]
    [InlineData("PENDING", NormalizedTransactionStatus.Pending)]
    public async Task MockProviderSupportsDeterministicOutcomes(string configured, NormalizedTransactionStatus expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Aeps:MockResult"] = configured }).Build();
        var result = await new MockAepsProvider(configuration).ExecuteAsync(Request(), CancellationToken.None);
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void RequestValidationRejectsInvalidAadhaarAndType()
    {
        var request = new AepsRequest { TransactionType = "INVALID", AadhaarNumber = "123", MobileNumber = "123", BankIin = "", DeviceName = "d", BiometricCaptureReference = "ref", IdempotencyKey = "idem-12345678" };
        var errors = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), errors, true));
        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(AepsRequest.AadhaarNumber)));
        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(AepsRequest.TransactionType)));
    }

    private static AepsProviderRequest Request() => new("CASH_WITHDRAWAL", "123456789012", "9876543210", "607152", "MockDevice", "capture-ref", 500, "PTA-TEST-1");
}
