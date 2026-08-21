using Microsoft.EntityFrameworkCore;
using ProxyType.Api.Contracts;
using ProxyType.Api.Data;
using ProxyType.Api.Security;

namespace ProxyType.Api.Services;

public sealed class ServicePermissionService(ProxyTypeDbContext dbContext)
{
    public async Task<IReadOnlyList<EffectiveService>> GetEffectiveAsync(
        Guid organizationUnitId,
        CancellationToken cancellationToken = default)
    {
        var allUnits = await dbContext.OrganizationUnits.AsNoTracking()
            .Select(unit => new { unit.OrganizationUnitId, unit.ParentOrganizationUnitId })
            .ToDictionaryAsync(unit => unit.OrganizationUnitId, cancellationToken);
        if (!allUnits.ContainsKey(organizationUnitId))
        {
            return [];
        }

        var path = new List<Guid>();
        Guid? cursor = organizationUnitId;
        while (cursor.HasValue && allUnits.TryGetValue(cursor.Value, out var unit) && path.Count < 5)
        {
            path.Add(unit.OrganizationUnitId);
            cursor = unit.ParentOrganizationUnitId;
        }

        var now = DateTime.UtcNow;
        var permissions = await dbContext.OrganizationServicePermissions.AsNoTracking()
            .Where(permission => path.Contains(permission.OrganizationUnitId) &&
                permission.EffectiveFromUtc <= now &&
                (permission.EffectiveToUtc == null || permission.EffectiveToUtc > now))
            .Select(permission => new
            {
                permission.OrganizationUnitId,
                permission.ServiceId,
                permission.Effect
            })
            .ToListAsync(cancellationToken);

        var services = await dbContext.Services.AsNoTracking()
            .Include(service => service.Category)
            .OrderBy(service => service.Category.SortOrder)
            .ThenBy(service => service.SortOrder)
            .ToListAsync(cancellationToken);

        return services.Select(service =>
        {
            var servicePermissions = permissions.Where(permission => permission.ServiceId == service.ServiceId).ToArray();
            var decision = PermissionEvaluator.Evaluate(service.IsActive, servicePermissions.Select(permission => permission.Effect));
            var localEffect = servicePermissions.FirstOrDefault(permission => permission.OrganizationUnitId == organizationUnitId)?.Effect;
            return new EffectiveService(
                service.ServiceId,
                service.Code,
                service.Name,
                service.Category.Name,
                decision.IsAllowed,
                decision.Reason,
                localEffect);
        }).ToArray();
    }

    public async Task<bool> HasAncestorDenyAsync(
        Guid organizationUnitId,
        Guid serviceId,
        CancellationToken cancellationToken = default)
    {
        var allUnits = await dbContext.OrganizationUnits.AsNoTracking()
            .Select(unit => new { unit.OrganizationUnitId, unit.ParentOrganizationUnitId })
            .ToDictionaryAsync(unit => unit.OrganizationUnitId, cancellationToken);
        if (!allUnits.TryGetValue(organizationUnitId, out var current))
        {
            return false;
        }

        var ancestors = new List<Guid>();
        var cursor = current.ParentOrganizationUnitId;
        while (cursor.HasValue && allUnits.TryGetValue(cursor.Value, out var unit) && ancestors.Count < 4)
        {
            ancestors.Add(unit.OrganizationUnitId);
            cursor = unit.ParentOrganizationUnitId;
        }

        var now = DateTime.UtcNow;
        return await dbContext.OrganizationServicePermissions.AsNoTracking().AnyAsync(
            permission => ancestors.Contains(permission.OrganizationUnitId) &&
                permission.ServiceId == serviceId && permission.Effect == "DENY" &&
                permission.EffectiveFromUtc <= now &&
                (permission.EffectiveToUtc == null || permission.EffectiveToUtc > now),
            cancellationToken);
    }
}
