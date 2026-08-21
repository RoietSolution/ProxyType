using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProxyType.Api.Contracts;
using ProxyType.Api.Data;
using ProxyType.Api.Domain;
using ProxyType.Api.Security;
using ProxyType.Api.Services;

namespace ProxyType.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations/{organizationUnitId:guid}/services")]
public sealed class ServicePermissionsController(
    ProxyTypeDbContext dbContext,
    ICurrentScopeService scopeService,
    ServicePermissionService permissionService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid organizationUnitId, CancellationToken cancellationToken)
    {
        var accessible = await scopeService.GetAccessibleOrganizationIdsAsync(cancellationToken);
        if (!accessible.Contains(organizationUnitId))
        {
            return Forbid();
        }

        var result = await permissionService.GetEffectiveAsync(organizationUnitId, cancellationToken);
        return result.Count == 0 ? NotFound() : Ok(result);
    }

    [HttpPut("{serviceId:guid}")]
    public async Task<IActionResult> Set(
        Guid organizationUnitId,
        Guid serviceId,
        SetServicePermissionRequest request,
        CancellationToken cancellationToken)
    {
        if (!await scopeService.CanManageOrganizationAsync(organizationUnitId, cancellationToken))
        {
            return Forbid();
        }

        if (!await dbContext.Services.AnyAsync(service => service.ServiceId == serviceId, cancellationToken))
        {
            return NotFound();
        }

        if (request.Effect == "ALLOW" &&
            await permissionService.HasAncestorDenyAsync(organizationUnitId, serviceId, cancellationToken))
        {
            return Conflict(new { message = "A parent organization denies this service. Remove that denial before granting it to a child." });
        }

        var now = DateTime.UtcNow;
        var current = await dbContext.OrganizationServicePermissions.SingleOrDefaultAsync(
            permission => permission.OrganizationUnitId == organizationUnitId &&
                permission.ServiceId == serviceId && permission.EffectiveToUtc == null,
            cancellationToken);
        if (current is not null)
        {
            current.EffectiveToUtc = now;
        }

        dbContext.OrganizationServicePermissions.Add(new OrganizationServicePermission
        {
            OrganizationServicePermissionId = Guid.NewGuid(),
            OrganizationUnitId = organizationUnitId,
            ServiceId = serviceId,
            Effect = request.Effect,
            Reason = request.Reason?.Trim(),
            EffectiveFromUtc = now,
            CreatedAtUtc = now,
            CreatedByUserId = scopeService.UserId
        });
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = scopeService.UserId,
            OrganizationUnitId = organizationUnitId,
            Action = "SERVICE_PERMISSION_SET",
            EntityType = "Service",
            EntityId = serviceId.ToString(),
            CorrelationId = Guid.NewGuid(),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { request.Effect, request.Reason }),
            OccurredAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok((await permissionService.GetEffectiveAsync(organizationUnitId, cancellationToken))
            .Single(service => service.ServiceId == serviceId));
    }
}
