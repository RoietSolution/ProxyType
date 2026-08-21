namespace ProxyType.Api.Security;

public static class HierarchyRules
{
    public static string? ExpectedParentType(string unitType) => unitType.ToUpperInvariant() switch
    {
        "PLATFORM" => null,
        "CMF" => "PLATFORM",
        "CSF" => "CMF",
        "CSP" => "CSF",
        _ => throw new ArgumentOutOfRangeException(nameof(unitType), "Unknown organization unit type.")
    };

    public static bool IsValidParent(string unitType, string? parentType) =>
        string.Equals(ExpectedParentType(unitType), parentType, StringComparison.OrdinalIgnoreCase);

    public static HashSet<Guid> DescendantIds(
        IEnumerable<(Guid Id, Guid? ParentId)> nodes,
        IEnumerable<Guid> roots)
    {
        var result = new HashSet<Guid>(roots);
        var children = nodes
            .Where(node => node.ParentId.HasValue)
            .GroupBy(node => node.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(node => node.Id).ToArray());
        var queue = new Queue<Guid>(result);

        while (queue.TryDequeue(out var current))
        {
            if (!children.TryGetValue(current, out var directChildren))
            {
                continue;
            }

            foreach (var child in directChildren)
            {
                if (result.Add(child))
                {
                    queue.Enqueue(child);
                }
            }
        }

        return result;
    }
}
