using Microsoft.Extensions.Configuration;
using ProxyType.Api.Finance;

namespace ProxyType.Api.Tests;

public sealed class RechargeProviderTests
{
    [Fact]
    public async Task MockProviderDefaultsToSuccessWithoutExternalCall()
    {
        var provider = new MockRechargeProvider(new ConfigurationBuilder().Build());
        var result = await provider.RechargeAsync(Request(), CancellationToken.None);

        Assert.Equal(ProviderMode.Mock, provider.Mode);
        Assert.Equal(NormalizedTransactionStatus.Succeeded, result.Status);
        Assert.StartsWith("MOCK-", result.ProviderReference);
    }

    [Theory]
    [InlineData("PENDING", NormalizedTransactionStatus.Pending)]
    [InlineData("FAILED", NormalizedTransactionStatus.Failed)]
    public async Task MockProviderSupportsDeterministicResults(string status, NormalizedTransactionStatus expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Recharge:MockResult"] = status })
            .Build();

        var result = await new MockRechargeProvider(configuration).RechargeAsync(Request(), CancellationToken.None);

        Assert.Equal(expected, result.Status);
    }

    private static RechargeProviderRequest Request() => new(
        "9876543210", 100, "airtel", "MOBILE", "244001", 0, 0, "PTR-TEST-1");
}
