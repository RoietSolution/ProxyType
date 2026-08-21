using ProxyType.Api.Security;

namespace ProxyType.Api.Tests;

public sealed class HierarchyRulesTests
{
    [Theory]
    [InlineData("CMF", "PLATFORM")]
    [InlineData("CSF", "CMF")]
    [InlineData("CSP", "CSF")]
    public void AcceptsOnlyRequiredParent(string child, string parent) =>
        Assert.True(HierarchyRules.IsValidParent(child, parent));

    [Theory]
    [InlineData("CMF", "CMF")]
    [InlineData("CSF", "PLATFORM")]
    [InlineData("CSP", "CMF")]
    public void RejectsHierarchyShortcuts(string child, string parent) =>
        Assert.False(HierarchyRules.IsValidParent(child, parent));

    [Fact]
    public void DescendantScopeDoesNotLeakAcrossBranches()
    {
        var cmfA = Guid.NewGuid(); var cmfB = Guid.NewGuid();
        var csfA = Guid.NewGuid(); var csfB = Guid.NewGuid();
        var cspA = Guid.NewGuid(); var cspB = Guid.NewGuid();
        var nodes = new[] { (csfA, (Guid?)cmfA), (cspA, (Guid?)csfA), (csfB, (Guid?)cmfB), (cspB, (Guid?)csfB) };
        var scope = HierarchyRules.DescendantIds(nodes, [cmfA]);
        Assert.Equal(3, scope.Count);
        Assert.Contains(cmfA, scope); Assert.Contains(csfA, scope); Assert.Contains(cspA, scope);
        Assert.DoesNotContain(cmfB, scope); Assert.DoesNotContain(csfB, scope); Assert.DoesNotContain(cspB, scope);
    }

    [Fact]
    public void CsfScopeIncludesOnlyItsCspDescendants()
    {
        var cmf = Guid.NewGuid(); var csf = Guid.NewGuid(); var csp = Guid.NewGuid(); var siblingCsf = Guid.NewGuid(); var siblingCsp = Guid.NewGuid();
        var scope = HierarchyRules.DescendantIds(new[] { (cmf, (Guid?)null), (csf, (Guid?)cmf), (csp, (Guid?)csf), (siblingCsf, (Guid?)cmf), (siblingCsp, (Guid?)siblingCsf) }, [csf]);
        Assert.Equal(2, scope.Count);
        Assert.Contains(csf, scope); Assert.Contains(csp, scope);
        Assert.DoesNotContain(cmf, scope); Assert.DoesNotContain(siblingCsf, scope); Assert.DoesNotContain(siblingCsp, scope);
    }

    [Fact]
    public void CspScopeIsolatedToItself()
    {
        var cmf = Guid.NewGuid(); var csf = Guid.NewGuid(); var csp = Guid.NewGuid();
        var scope = HierarchyRules.DescendantIds(new[] { (cmf, (Guid?)null), (csf, (Guid?)cmf), (csp, (Guid?)csf) }, [csp]);
        Assert.Single(scope);
        Assert.Contains(csp, scope);
    }
}
