using ProxyType.Api.Finance;
using ProxyType.Api.Domain;

namespace ProxyType.Api.Tests;

public sealed class PricingServiceTests
{
    [Theory]
    [InlineData("FIXED", 7, 1000, 7)]
    [InlineData("PERCENTAGE", 1.5, 200, 3)]
    [InlineData("PERCENTAGE", 0.15, 999, 1.4985)]
    public void CalculateSupportsLegacyFixedAndPercentageRules(string type, decimal rate, decimal amount, decimal expected) =>
        Assert.Equal(expected, PricingService.Calculate(type, rate, amount));

    [Fact]
    public void CalculateRejectsUnknownCalculationType() =>
        Assert.Throws<InvalidOperationException>(() => PricingService.Calculate("TIERED", 1, 100));

    [Fact]
    public void UserRechargeCommissionOverridesCspGroupRule()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var group = new RechargeCommissionRule { RoleId = roleId, CalculationType = "PERCENTAGE", Rate = 1.5m };
        var user = new RechargeCommissionRule { UserId = userId, CalculationType = "FIXED", Rate = 7m };

        Assert.Same(user, PricingService.SelectRechargeCommissionRule([group, user], userId, roleId));
    }

    [Fact]
    public void RechargeCommissionFallsBackToCspGroupRule()
    {
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var group = new RechargeCommissionRule { RoleId = roleId, CalculationType = "PERCENTAGE", Rate = 1.5m };

        Assert.Same(group, PricingService.SelectRechargeCommissionRule([group], userId, roleId));
    }
}
