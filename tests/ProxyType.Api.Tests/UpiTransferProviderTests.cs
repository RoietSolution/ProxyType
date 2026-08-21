using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using ProxyType.Api.Contracts;
using ProxyType.Api.Finance;

namespace ProxyType.Api.Tests;

public sealed class UpiTransferProviderTests
{
    [Fact]
    public async Task MockOrderCreatesHostedPaymentDetailsAndDoesNotCompletePayment()
    {
        var provider = new MockUpiTransferProvider(new ConfigurationBuilder().Build());
        var result = await provider.CreateOrderAsync(new(100, "PTU-TEST-1"), CancellationToken.None);
        Assert.Equal(ProviderMode.Mock, provider.Mode);
        Assert.Equal(NormalizedTransactionStatus.Created, result.Status);
        Assert.Equal("MOCK_ORDER_CREATED", result.RawStatus);
        Assert.StartsWith("https://mock.invalid/upi/pay/", result.PaymentUrl);
    }

    [Theory]
    [InlineData("SUCCESS", NormalizedTransactionStatus.Succeeded)]
    [InlineData("FAILED", NormalizedTransactionStatus.Failed)]
    [InlineData("EXPIRED", NormalizedTransactionStatus.Cancelled)]
    [InlineData("PENDING", NormalizedTransactionStatus.Pending)]
    public async Task MockStatusSupportsDeterministicOutcomes(string configured, NormalizedTransactionStatus expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["UpiTransfer:MockStatus"] = configured }).Build();
        var result = await new MockUpiTransferProvider(configuration).GetStatusAsync(new("PTU-TEST-1", "MOCK-PTU-TEST-1"), CancellationToken.None);
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void RequestValidationRejectsInvalidAmount()
    {
        var request = new UpiFundingRequest { Amount = 0, IdempotencyKey = "idem-12345678" };
        var errors = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), errors, true));
        Assert.Contains(errors, x => x.MemberNames.Contains(nameof(UpiFundingRequest.Amount)));
    }
}
