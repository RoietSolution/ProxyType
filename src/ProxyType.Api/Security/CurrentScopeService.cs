using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ProxyType.Api.Data;

namespace ProxyType.Api.Security;

public interface ICurrentScopeService
{
    Guid UserId { get; }
    bool IsPlatformAdmin { get; }
    bool HasRole(params string[] roles);
    Task<HashSet<Guid>> GetAccessibleOrganizationIdsAsync(CancellationToken cancellationToken = default);
    Task<bool> CanManageOrganizationAsync(Guid organizationUnitId, CancellationToken cancellationToken = default);
}

public sealed class CurrentScopeService(
    IHttpContextAccessor httpContextAccessor,
    ProxyTypeDbContext dbContext) : ICurrentScopeService
{
    private ClaimsPrincipal User => httpContextAccessor.HttpContext?.User
        ?? throw new InvalidOperationException("No active HTTP user context.");

    public Guid UserId => Guid.Parse(User.FindFirstValue("sub")
        ?? throw new InvalidOperationException("Authenticated user identifier is missing."));

    public bool IsPlatformAdmin => User.IsInRole("PLATFORM_ADMIN");

    public bool HasRole(params string[] roles) => roles.Any(User.IsInRole);

    public async Task<HashSet<Guid>> GetAccessibleOrganizationIdsAsync(CancellationToken cancellationToken = default)
    {
        if (!await dbContext.Users.AsNoTracking().AnyAsync(user => user.UserId == UserId && user.IsActive, cancellationToken))
        {
            return [];
        }

        var nodes = await dbContext.OrganizationUnits.AsNoTracking()
            .Select(unit => new { Id = unit.OrganizationUnitId, ParentId = unit.ParentOrganizationUnitId })
            .ToListAsync(cancellationToken);

        if (IsPlatformAdmin)
        {
            return nodes.Select(node => node.Id).ToHashSet();
        }

        var now = DateTime.UtcNow;
        var roots = await dbContext.OrganizationMemberships.AsNoTracking()
            .Where(membership => membership.UserId == UserId && membership.IsActive &&
                (membership.ValidToUtc == null || membership.ValidToUtc > now))
            .Select(membership => membership.OrganizationUnitId)
            .ToListAsync(cancellationToken);

        return HierarchyRules.DescendantIds(nodes.Select(node => (node.Id, node.ParentId)), roots);
    }

    public async Task<bool> CanManageOrganizationAsync(Guid organizationUnitId, CancellationToken cancellationToken = default)
    {
        if (IsPlatformAdmin)
        {
            return true;
        }

        if (!HasRole("CMF_ADMIN", "CSF_ADMIN"))
        {
            return false;
        }

        var accessible = await GetAccessibleOrganizationIdsAsync(cancellationToken);
        return accessible.Contains(organizationUnitId);
    }
}
