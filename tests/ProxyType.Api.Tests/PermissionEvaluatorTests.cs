using ProxyType.Api.Security;

namespace ProxyType.Api.Tests;

public sealed class PermissionEvaluatorTests
{
    [Fact]
    public void AnyDenyOverridesLocalOrInheritedAllow()
    {
        var decision = PermissionEvaluator.Evaluate(true, ["ALLOW", "DENY", "ALLOW"]);
        Assert.False(decision.IsAllowed);
        Assert.Equal("ANCESTOR_OR_LOCAL_DENY", decision.Reason);
    }

    [Fact]
    public void ActiveServiceNeedsExplicitGrant() =>
        Assert.Equal((false, "NO_GRANT"), PermissionEvaluator.Evaluate(true, []));

    [Fact]
    public void InactiveServiceCannotBeGranted() =>
        Assert.Equal((false, "SERVICE_INACTIVE"), PermissionEvaluator.Evaluate(false, ["ALLOW"]));
}
