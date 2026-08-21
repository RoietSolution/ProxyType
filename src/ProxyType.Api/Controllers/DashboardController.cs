using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProxyType.Api.Data;
using ProxyType.Api.Security;
using ProxyType.Api.Services;

namespace ProxyType.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/dashboard")]
public sealed class DashboardController(
    ProxyTypeDbContext dbContext,
    ICurrentScopeService scopeService,
    ServicePermissionService permissionService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var accessible = await scopeService.GetAccessibleOrganizationIdsAsync(cancellationToken);
        var units = await dbContext.OrganizationUnits.AsNoTracking()
            .Where(unit => accessible.Contains(unit.OrganizationUnitId))
            .Select(unit => new { unit.OrganizationUnitId, unit.UnitType, unit.Status, unit.Code, unit.Name })
            .ToListAsync(cancellationToken);
        var primary = await dbContext.OrganizationMemberships.AsNoTracking()
            .Where(membership => membership.UserId == scopeService.UserId && membership.IsActive)
            .OrderByDescending(membership => membership.IsPrimary)
            .Select(membership => new
            {
                membership.OrganizationUnitId,
                membership.OrganizationUnit.Code,
                membership.OrganizationUnit.Name,
                membership.OrganizationUnit.UnitType
            }).FirstOrDefaultAsync(cancellationToken);
        var permissionTarget = primary?.OrganizationUnitId ?? units.FirstOrDefault()?.OrganizationUnitId;
        var effectiveServices = permissionTarget.HasValue
            ? await permissionService.GetEffectiveAsync(permissionTarget.Value, cancellationToken)
            : [];
        var logins = await dbContext.LoginAudits.AsNoTracking()
            .Where(audit => audit.UserId == scopeService.UserId)
            .OrderByDescending(audit => audit.OccurredAtUtc)
            .Take(5)
            .Select(audit => new { audit.Succeeded, audit.FailureCode, audit.IpAddress, audit.OccurredAtUtc })
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            Organization = primary,
            Hierarchy = new
            {
                Total = units.Count,
                Active = units.Count(unit => unit.Status == "ACTIVE"),
                Cmf = units.Count(unit => unit.UnitType == "CMF"),
                Csf = units.Count(unit => unit.UnitType == "CSF"),
                Csp = units.Count(unit => unit.UnitType == "CSP")
            },
            Services = new
            {
                Available = effectiveServices.Count(service => service.IsAllowed),
                Restricted = effectiveServices.Count(service => !service.IsAllowed),
                Items = effectiveServices.Where(service => service.IsAllowed)
            },
            RecentSecurityActivity = logins,
            GeneratedAtUtc = DateTime.UtcNow
        });
    }
}
