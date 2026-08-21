using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProxyType.Api.Contracts;
using ProxyType.Api.Data;
using ProxyType.Api.Domain;
using ProxyType.Api.Security;

namespace ProxyType.Api.Controllers;

[ApiController, Authorize, Route("api/users")]
public sealed class UsersController(
    ProxyTypeDbContext dbContext,
    ICurrentScopeService scopeService,
    IPasswordHasher<AppUser> passwordHasher) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<UserPageResponse>> List(
        [FromQuery] string? search, [FromQuery] bool? active, [FromQuery] Guid? organizationUnitId,
        [FromQuery] string? unitType, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        if (!await HasUserManagementRoleAsync(cancellationToken)) return Forbid();
        page = Math.Clamp(page, 1, 10000); pageSize = new[] { 25, 50, 100 }.Contains(pageSize) ? pageSize : 50;
        if (!string.IsNullOrWhiteSpace(unitType) && !new[] { "CMF", "CSF", "CSP" }.Contains(unitType.Trim().ToUpperInvariant())) return BadRequest("unitType must be CMF, CSF, or CSP.");
        var accessible = await scopeService.GetAccessibleOrganizationIdsAsync(cancellationToken);
        var query = dbContext.Users.AsNoTracking().AsQueryable();
        var isPlatformAdmin = await IsPlatformAdminAsync(cancellationToken);
        if (!isPlatformAdmin) query = query.Where(u => dbContext.OrganizationMemberships.Any(m => m.UserId == u.UserId && m.IsActive && accessible.Contains(m.OrganizationUnitId)));
        if (organizationUnitId.HasValue) query = query.Where(u => dbContext.OrganizationMemberships.Any(m => m.UserId == u.UserId && m.IsActive && m.OrganizationUnitId == organizationUnitId.Value));
        if (!string.IsNullOrWhiteSpace(unitType)) { var type = unitType.Trim().ToUpperInvariant(); query = query.Where(u => dbContext.OrganizationMemberships.Any(m => m.UserId == u.UserId && m.IsActive && m.OrganizationUnit.UnitType == type && (isPlatformAdmin || accessible.Contains(m.OrganizationUnitId)))); }
        if (active.HasValue) query = query.Where(u => u.IsActive == active.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim();
            query = query.Where(u => u.Username.Contains(value) || u.DisplayName.Contains(value) || u.Email.Contains(value) || (u.Mobile != null && u.Mobile.Contains(value)));
        }
        var total = await query.CountAsync(cancellationToken);
        var users = await query.OrderBy(u => u.DisplayName).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var ids = users.Select(x => x.UserId).ToArray();
        var roles = await dbContext.UserRoles.AsNoTracking().Where(r => ids.Contains(r.UserId)).Select(r => new { r.UserId, r.Role.Code }).ToListAsync(cancellationToken);
        var memberships = await dbContext.OrganizationMemberships.AsNoTracking().Where(m => ids.Contains(m.UserId) && m.IsActive).Select(m => new { m.UserId, m.OrganizationUnit }).ToListAsync(cancellationToken);
        var items = users.Select(user =>
        {
            var userMemberships = memberships.Where(m => m.UserId == user.UserId).Select(m => new MembershipSummary(m.OrganizationUnit.OrganizationUnitId, m.OrganizationUnit.Code, m.OrganizationUnit.Name, m.OrganizationUnit.UnitType, true)).ToArray();
            return new UserListItem(user.UserId, user.Username, user.Email, user.Mobile, user.DisplayName, user.IsActive, user.MustChangePassword, roles.Where(r => r.UserId == user.UserId).Select(r => r.Code).ToArray(), userMemberships, user.LegacySystem, user.LegacyId);
        }).ToArray();
        return Ok(new UserPageResponse(items, page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize)));
    }

    [HttpGet("{userId:guid}")]
    public async Task<ActionResult<UserListItem>> Details(Guid userId, CancellationToken cancellationToken)
    {
        if (!await HasUserManagementRoleAsync(cancellationToken)) return Forbid();
        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(u => u.UserId == userId, cancellationToken);
        if (user is null) return NotFound();
        if (!await CanManageUserAsync(userId, cancellationToken)) return Forbid();
        var roles = await dbContext.UserRoles.AsNoTracking().Where(r => r.UserId == userId).Select(r => r.Role.Code).ToArrayAsync(cancellationToken);
        var memberships = await dbContext.OrganizationMemberships.AsNoTracking().Where(m => m.UserId == userId && m.IsActive).Select(m => new MembershipSummary(m.OrganizationUnit.OrganizationUnitId, m.OrganizationUnit.Code, m.OrganizationUnit.Name, m.OrganizationUnit.UnitType, m.IsPrimary)).ToArrayAsync(cancellationToken);
        return Ok(new UserListItem(user.UserId, user.Username, user.Email, user.Mobile, user.DisplayName, user.IsActive, user.MustChangePassword, roles, memberships, user.LegacySystem, user.LegacyId));
    }

    [HttpPost]
    public async Task<ActionResult> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        if (!await HasUserManagementRoleAsync(cancellationToken)) return Forbid();
        if (!await CanManageAsync(request.OrganizationUnitId, cancellationToken)) return Forbid();
        if (!await dbContext.OrganizationUnits.AnyAsync(u => u.OrganizationUnitId == request.OrganizationUnitId && u.Status == "ACTIVE", cancellationToken)) return BadRequest("Organization unit is not active.");
        if (await dbContext.Users.AnyAsync(u => u.Username == request.Username || u.Email == request.Email || (request.Mobile != null && u.Mobile == request.Mobile), cancellationToken)) return Conflict("Username, email, or mobile is already in use.");
        var user = new AppUser { UserId = Guid.NewGuid(), Username = request.Username.Trim(), Email = request.Email.Trim(), Mobile = request.Mobile?.Trim(), DisplayName = request.DisplayName.Trim(), IsActive = true, MustChangePassword = false, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        dbContext.Users.Add(user);
        await AddMembershipAndRoleAsync(user, request.OrganizationUnitId, request.Role, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Created($"/api/users/{user.UserId}", new { user.UserId });
    }

    [HttpPut("{userId:guid}")]
    public async Task<IActionResult> Update(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        if (!await HasUserManagementRoleAsync(cancellationToken)) return Forbid();
        var user = await dbContext.Users.SingleOrDefaultAsync(u => u.UserId == userId, cancellationToken);
        if (user is null) return NotFound();
        if (!await CanManageUserAsync(userId, cancellationToken) || !await CanManageAsync(request.OrganizationUnitId, cancellationToken)) return Forbid();
        if (await dbContext.Users.AnyAsync(u => u.UserId != userId && (u.Email == request.Email || (request.Mobile != null && u.Mobile == request.Mobile)), cancellationToken)) return Conflict("Email or mobile is already in use.");
        user.Email = request.Email.Trim(); user.Mobile = request.Mobile?.Trim(); user.DisplayName = request.DisplayName.Trim(); user.IsActive = request.IsActive; user.UpdatedAtUtc = DateTime.UtcNow;
        var memberships = await dbContext.OrganizationMemberships.Where(m => m.UserId == userId && m.IsActive).ToListAsync(cancellationToken);
        foreach (var membership in memberships) membership.IsActive = false;
        var oldRoles = await dbContext.UserRoles.Where(r => r.UserId == userId).ToListAsync(cancellationToken);
        dbContext.UserRoles.RemoveRange(oldRoles);
        await AddMembershipAndRoleAsync(user, request.OrganizationUnitId, request.Role, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> Disable(Guid userId, CancellationToken cancellationToken)
    {
        if (!await HasUserManagementRoleAsync(cancellationToken)) return Forbid();
        if (userId == scopeService.UserId) return BadRequest("You cannot disable your own account.");
        var user = await dbContext.Users.SingleOrDefaultAsync(u => u.UserId == userId, cancellationToken);
        if (user is null) return NotFound();
        if (!await CanManageUserAsync(userId, cancellationToken)) return Forbid();
        user.IsActive = false; user.UpdatedAtUtc = DateTime.UtcNow; await dbContext.SaveChangesAsync(cancellationToken); return NoContent();
    }

    [HttpPost("{userId:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid userId, ResetUserPasswordRequest request, CancellationToken cancellationToken)
    {
        if (!await HasUserManagementRoleAsync(cancellationToken)) return Forbid();
        var user = await dbContext.Users.SingleOrDefaultAsync(u => u.UserId == userId, cancellationToken);
        if (user is null) return NotFound();
        if (!await CanManageUserAsync(userId, cancellationToken)) return Forbid();
        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword); user.MustChangePassword = true; user.PasswordChangedAtUtc = DateTime.UtcNow; user.SecurityStamp = Guid.NewGuid(); user.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken); return NoContent();
    }

    private async Task<bool> CanManageAsync(Guid organizationUnitId, CancellationToken cancellationToken) =>
        (await HasUserManagementRoleAsync(cancellationToken)) && (await scopeService.GetAccessibleOrganizationIdsAsync(cancellationToken)).Contains(organizationUnitId);

    private async Task<bool> CanManageUserAsync(Guid userId, CancellationToken cancellationToken) =>
        (await IsPlatformAdminAsync(cancellationToken)) || (await dbContext.OrganizationMemberships.AsNoTracking().Where(m => m.UserId == userId && m.IsActive).Select(m => m.OrganizationUnitId).ToListAsync(cancellationToken)).Intersect(await scopeService.GetAccessibleOrganizationIdsAsync(cancellationToken)).Any();

    private Task<bool> IsPlatformAdminAsync(CancellationToken cancellationToken) =>
        dbContext.UserRoles.AsNoTracking().AnyAsync(r => r.UserId == scopeService.UserId && r.Role.Code == "PLATFORM_ADMIN", cancellationToken);

    private Task<bool> HasUserManagementRoleAsync(CancellationToken cancellationToken) =>
        dbContext.UserRoles.AsNoTracking().AnyAsync(r => r.UserId == scopeService.UserId && (r.Role.Code == "PLATFORM_ADMIN" || r.Role.Code == "CMF_ADMIN" || r.Role.Code == "CSF_ADMIN"), cancellationToken);

    private async Task AddMembershipAndRoleAsync(AppUser user, Guid organizationUnitId, string roleCode, CancellationToken cancellationToken)
    {
        var role = await dbContext.Roles.SingleAsync(r => r.Code == roleCode, cancellationToken);
        dbContext.OrganizationMemberships.Add(new OrganizationMembership { OrganizationMembershipId = Guid.NewGuid(), UserId = user.UserId, OrganizationUnitId = organizationUnitId, IsPrimary = true, IsActive = true, ValidFromUtc = DateTime.UtcNow });
        dbContext.UserRoles.Add(new UserRole { UserId = user.UserId, RoleId = role.RoleId, AssignedAtUtc = DateTime.UtcNow, AssignedByUserId = scopeService.UserId });
    }
}
