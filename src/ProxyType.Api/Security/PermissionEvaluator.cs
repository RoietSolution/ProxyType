namespace ProxyType.Api.Security;

public static class PermissionEvaluator
{
    public static (bool IsAllowed, string Reason) Evaluate(bool serviceActive, IEnumerable<string> pathEffects)
    {
        if (!serviceActive)
        {
            return (false, "SERVICE_INACTIVE");
        }

        var effects = pathEffects.Select(effect => effect.ToUpperInvariant()).ToArray();
        if (effects.Contains("DENY", StringComparer.Ordinal))
        {
            return (false, "ANCESTOR_OR_LOCAL_DENY");
        }

        return effects.Contains("ALLOW", StringComparer.Ordinal)
            ? (true, "INHERITED_OR_LOCAL_ALLOW")
            : (false, "NO_GRANT");
    }
}
