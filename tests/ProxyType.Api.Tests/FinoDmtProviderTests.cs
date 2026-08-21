using Microsoft.Extensions.Configuration;
using ProxyType.Api.Contracts;
using ProxyType.Api.Finance;
using System.ComponentModel.DataAnnotations;

namespace ProxyType.Api.Tests;

public sealed class FinoDmtProviderTests
{
    [Fact]
    public async Task MockProviderReturnsSuccessWithoutExternalCall()
    {
        var provider = new MockFinoDmtProvider(new ConfigurationBuilder().Build());
        var result = await provider.TransferAsync(Request(), CancellationToken.None);

        Assert.Equal(ProviderMode.Mock, provider.Mode);
        Assert.Equal(NormalizedTransactionStatus.Succeeded, result.Status);
        Assert.StartsWith("MOCK-", result.ProviderReference);
    }

    [Theory]
    [InlineData("PENDING", NormalizedTransactionStatus.Pending)]
    [InlineData("FAILED", NormalizedTransactionStatus.Failed)]
    public async Task MockProviderSupportsDeterministicNonSuccessResults(string status, NormalizedTransactionStatus expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["FinoDmt:MockResult"] = status })
            .Build();
        var result = await new MockFinoDmtProvider(configuration).TransferAsync(Request(), CancellationToken.None);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void RequestValidationRejectsUnsafeAmountAndIfsc()
    {
        var request = new FinoDmtRequest("123456789", "INVALID", "Test User", "9876543210", 100, "IMPS", "test", "idem-123456");
        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(request, new ValidationContext(request), results, true);

        Assert.False(valid);
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(FinoDmtRequest.IfscCode)));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(FinoDmtRequest.Amount)));
    }

    private static FinoDmtProviderRequest Request() => new(
        "123456789", "ABCD0123456", "Test User", "9876543210", 1000, "IMPS", "test", "PT-TEST-1");
}
