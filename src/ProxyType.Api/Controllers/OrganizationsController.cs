using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProxyType.Api.Contracts;
using ProxyType.Api.Data;
using ProxyType.Api.Domain;
using ProxyType.Api.Security;

namespace ProxyType.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations")]
public sealed class OrganizationsController(
    ProxyTypeDbContext dbContext,
    ICurrentScopeService scopeService) : ControllerBase
{
    [HttpGet("tree")]
    public async Task<IActionResult> Tree(CancellationToken cancellationToken)
    {
        var accessible = await scopeService.GetAccessibleOrganizationIdsAsync(cancellationToken);
        var units = await dbContext.OrganizationUnits.AsNoTracking()
            .Where(unit => accessible.Contains(unit.OrganizationUnitId))
            .OrderBy(unit => unit.UnitType).ThenBy(unit => unit.Name)
            .Select(unit => new
            {
                unit.OrganizationUnitId,
                unit.ParentOrganizationUnitId,
                unit.UnitType,
                unit.Code,
                unit.Name,
                unit.Status
            })
            .ToListAsync(cancellationToken);
        return Ok(units);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        if (!await scopeService.CanManageOrganizationAsync(request.ParentOrganizationUnitId, cancellationToken))
        {
            return Forbid();
        }

        var parent = await dbContext.OrganizationUnits.AsNoTracking()
            .SingleOrDefaultAsync(unit => unit.OrganizationUnitId == request.ParentOrganizationUnitId, cancellationToken);
        if (parent is null || !HierarchyRules.IsValidParent(request.UnitType, parent.UnitType))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(request.ParentOrganizationUnitId)] = ["Parent type does not match PLATFORM → CMF → CSF → CSP."]
            }));
        }

        if (request.UnitType == "CMF" && !scopeService.IsPlatformAdmin)
        {
            return Forbid();
        }
        if (request.UnitType == "CSF" && !scopeService.HasRole("PLATFORM_ADMIN", "CMF_ADMIN"))
        {
            return Forbid();
        }
        if (request.UnitType == "CSP" && !scopeService.HasRole("PLATFORM_ADMIN", "CMF_ADMIN", "CSF_ADMIN"))
        {
            return Forbid();
        }

        var code = request.Code.Trim().ToUpperInvariant();
        if (await dbContext.OrganizationUnits.AnyAsync(unit => unit.Code == code, cancellationToken))
        {
            return Conflict(new { message = "Organization code already exists." });
        }

        var unit = new OrganizationUnit
        {
            OrganizationUnitId = Guid.NewGuid(),
            ParentOrganizationUnitId = parent.OrganizationUnitId,
            UnitType = request.UnitType,
            Code = code,
            Name = request.Name.Trim(),
            Status = "PENDING",
            Email = request.Email?.Trim(),
            Mobile = request.Mobile?.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        dbContext.OrganizationUnits.Add(unit);
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = scopeService.UserId,
            OrganizationUnitId = parent.OrganizationUnitId,
            Action = "ORGANIZATION_CREATED",
            EntityType = "OrganizationUnit",
            EntityId = unit.OrganizationUnitId.ToString(),
            CorrelationId = Guid.NewGuid(),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { unit.Code, unit.UnitType }),
            OccurredAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Created($"/api/organizations/{unit.OrganizationUnitId}", new
        {
            unit.OrganizationUnitId,
            unit.ParentOrganizationUnitId,
            unit.UnitType,
            unit.Code,
            unit.Name,
            unit.Status
        });
    }

    [HttpGet("{organizationUnitId:guid}/users")]
    public async Task<IActionResult> Users(Guid organizationUnitId, CancellationToken cancellationToken)
    {
        var accessible = await scopeService.GetAccessibleOrganizationIdsAsync(cancellationToken);
        if (!accessible.Contains(organizationUnitId))
        {
            return Forbid();
        }

        var users = await dbContext.OrganizationMemberships.AsNoTracking()
            .Where(membership => membership.OrganizationUnitId == organizationUnitId && membership.IsActive)
            .Select(membership => new
            {
                membership.User.UserId,
                membership.User.Username,
                membership.User.DisplayName,
                membership.User.Email,
                membership.User.IsActive,
                membership.IsPrimary
            })
            .ToListAsync(cancellationToken);
        return Ok(users);
    }
}
