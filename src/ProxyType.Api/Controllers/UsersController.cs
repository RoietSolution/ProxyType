using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
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
    private static readonly JsonSerializerOptions ProfileJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> ProfileExcludedProperties =
        [nameof(ChannelUserMutationRequest.Password), nameof(ChannelUserMutationRequest.AadhaarDocument),
         nameof(ChannelUserMutationRequest.UserCode), nameof(ChannelUserMutationRequest.UnitType),
         nameof(ChannelUserMutationRequest.ParentOrganizationUnitId), nameof(ChannelUserMutationRequest.IsActive)];

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

    [HttpGet("{userId:guid}/channel")]
    public async Task<ActionResult<ChannelUserDetailsResponse>> ChannelDetails(Guid userId, CancellationToken cancellationToken)
    {
        if (!await HasUserManagementRoleAsync(cancellationToken)) return Forbid();
        if (!await CanManageUserAsync(userId, cancellationToken)) return Forbid();
        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (user is null) return NotFound();
        var membership = await dbContext.OrganizationMemberships.AsNoTracking()
            .Where(item => item.UserId == userId && item.IsActive)
            .Select(item => item.OrganizationUnit).FirstOrDefaultAsync(cancellationToken);
        if (membership is null || membership.ParentOrganizationUnitId is null || !new[] { "CMF", "CSF", "CSP" }.Contains(membership.UnitType)) return NotFound();
        var roles = await dbContext.UserRoles.AsNoTracking().Where(item => item.UserId == userId).Select(item => item.Role.Code).ToArrayAsync(cancellationToken);
        var memberships = new[] { new MembershipSummary(membership.OrganizationUnitId, membership.Code, membership.Name, membership.UnitType, true) };
        var profile = await dbContext.UserProfiles.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        var responseEmail = PreferOrganizationContact(user.Email, membership.Email, true) ?? user.Email;
        var responseMobile = PreferOrganizationContact(user.Mobile, membership.Mobile);
        var responseProfile = BuildProfileResponse(profile?.ProfileJson, user.DisplayName, membership.Name);
        return Ok(new ChannelUserDetailsResponse(
            new UserListItem(user.UserId, user.Username, responseEmail, responseMobile, user.DisplayName, user.IsActive,
                user.MustChangePassword, roles, memberships, user.LegacySystem, user.LegacyId),
            membership.OrganizationUnitId, membership.ParentOrganizationUnitId.Value, membership.Code, membership.Name,
            responseProfile, profile?.AadhaarDocumentContent is not null, profile?.AadhaarDocumentFileName));
    }

    [HttpPost("channels")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<ActionResult> CreateChannel([FromForm] ChannelUserMutationRequest request, CancellationToken cancellationToken)
    {
        if (!await HasUserManagementRoleAsync(cancellationToken)) return Forbid();
        var errors = await ValidateChannelRequestAsync(request, true, false, cancellationToken);
        if (errors.Count != 0) return BadRequest(new ValidationProblemDetails(errors));

        var code = request.UserCode.Trim().ToUpperInvariant();
        if (await dbContext.Users.AnyAsync(user => user.Username == code || user.Email == request.Email || user.Mobile == request.Mobile, cancellationToken) ||
            await dbContext.OrganizationUnits.AnyAsync(unit => unit.Code == code, cancellationToken))
            return Conflict(new ProblemDetails { Title = "User already exists", Detail = "User code, email, or mobile is already in use.", Status = 409 });

        var now = DateTime.UtcNow;
        var user = new AppUser
        {
            UserId = Guid.NewGuid(), Username = code, Email = request.Email.Trim().ToLowerInvariant(), Mobile = request.Mobile.Trim(),
            DisplayName = BuildDisplayName(request), SecurityStamp = Guid.NewGuid(), IsActive = request.IsActive,
            MustChangePassword = true, PasswordChangedAtUtc = now, CreatedAtUtc = now, UpdatedAtUtc = now, CreatedByUserId = scopeService.UserId
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password!);
        var unit = new OrganizationUnit
        {
            OrganizationUnitId = Guid.NewGuid(), ParentOrganizationUnitId = request.ParentOrganizationUnitId,
            UnitType = request.UnitType, Code = code, Name = request.CompanyName.Trim(), Status = request.IsActive ? "ACTIVE" : "SUSPENDED",
            Email = user.Email, Mobile = user.Mobile, CreatedAtUtc = now, UpdatedAtUtc = now
        };
        var profile = new UserProfile { UserId = user.UserId, ProfileJson = SerializeProfile(request), CreatedAtUtc = now, UpdatedAtUtc = now };
        await SetAadhaarDocumentAsync(profile, request.AadhaarDocument, cancellationToken);

        dbContext.Users.Add(user); dbContext.OrganizationUnits.Add(unit); dbContext.UserProfiles.Add(profile);
        await AddMembershipAndRoleAsync(user, unit.OrganizationUnitId, RoleFor(request.UnitType), cancellationToken);
        AddChannelAudit("CHANNEL_USER_CREATED", user.UserId, request.ParentOrganizationUnitId, new { unit.Code, unit.UnitType });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Created($"/api/users/{user.UserId}", new { user.UserId, unit.OrganizationUnitId, unit.Code });
    }

    [HttpPut("{userId:guid}/channel")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UpdateChannel(Guid userId, [FromForm] ChannelUserMutationRequest request, CancellationToken cancellationToken)
    {
        if (!await HasUserManagementRoleAsync(cancellationToken)) return Forbid();
        if (!await CanManageUserAsync(userId, cancellationToken)) return Forbid();
        var user = await dbContext.Users.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (user is null) return NotFound();
        var membership = await dbContext.OrganizationMemberships.Include(item => item.OrganizationUnit)
            .SingleOrDefaultAsync(item => item.UserId == userId && item.IsActive && item.IsPrimary, cancellationToken);
        if (membership is null) return NotFound();
        var profile = await dbContext.UserProfiles.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        var hasDocument = profile?.AadhaarDocumentContent is not null;
        var errors = await ValidateChannelRequestAsync(request, false, hasDocument, cancellationToken);
        if (request.UnitType != membership.OrganizationUnit.UnitType)
            errors[nameof(request.UnitType)] = ["A channel user's hierarchy type cannot be changed."];
        if (errors.Count != 0) return BadRequest(new ValidationProblemDetails(errors));
        if (userId == scopeService.UserId && !request.IsActive) return BadRequest("You cannot disable your own account.");

        var code = request.UserCode.Trim().ToUpperInvariant();
        if (await dbContext.Users.AnyAsync(item => item.UserId != userId && (item.Username == code || item.Email == request.Email || item.Mobile == request.Mobile), cancellationToken) ||
            await dbContext.OrganizationUnits.AnyAsync(item => item.OrganizationUnitId != membership.OrganizationUnitId && item.Code == code, cancellationToken))
            return Conflict(new ProblemDetails { Title = "User already exists", Detail = "User code, email, or mobile is already in use.", Status = 409 });

        var now = DateTime.UtcNow;
        user.Username = code; user.Email = request.Email.Trim().ToLowerInvariant(); user.Mobile = request.Mobile.Trim();
        user.DisplayName = BuildDisplayName(request); user.IsActive = request.IsActive; user.UpdatedAtUtc = now;
        membership.OrganizationUnit.ParentOrganizationUnitId = request.ParentOrganizationUnitId;
        membership.OrganizationUnit.Code = code; membership.OrganizationUnit.Name = request.CompanyName.Trim();
        membership.OrganizationUnit.Status = request.IsActive ? "ACTIVE" : "SUSPENDED";
        membership.OrganizationUnit.Email = user.Email; membership.OrganizationUnit.Mobile = user.Mobile; membership.OrganizationUnit.UpdatedAtUtc = now;
        profile ??= new UserProfile { UserId = userId, CreatedAtUtc = now };
        profile.ProfileJson = SerializeProfile(request); profile.UpdatedAtUtc = now;
        if (dbContext.Entry(profile).State == EntityState.Detached) dbContext.UserProfiles.Add(profile);
        await SetAadhaarDocumentAsync(profile, request.AadhaarDocument, cancellationToken);
        AddChannelAudit("CHANNEL_USER_UPDATED", userId, membership.OrganizationUnitId, new { Code = code, request.UnitType });
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("{userId:guid}/aadhaar-document")]
    public async Task<IActionResult> AadhaarDocument(Guid userId, CancellationToken cancellationToken)
    {
        if (!await HasUserManagementRoleAsync(cancellationToken) || !await CanManageUserAsync(userId, cancellationToken)) return Forbid();
        var document = await dbContext.UserProfiles.AsNoTracking().Where(item => item.UserId == userId)
            .Select(item => new { item.AadhaarDocumentContent, item.AadhaarDocumentContentType, item.AadhaarDocumentFileName })
            .SingleOrDefaultAsync(cancellationToken);
        if (document?.AadhaarDocumentContent is null) return NotFound();
        return File(document.AadhaarDocumentContent, document.AadhaarDocumentContentType ?? "application/octet-stream",
            document.AadhaarDocumentFileName ?? "aadhaar-document");
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

    private async Task<Dictionary<string, string[]>> ValidateChannelRequestAsync(ChannelUserMutationRequest request, bool creating, bool hasDocument, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (creating && string.IsNullOrWhiteSpace(request.Password)) errors[nameof(request.Password)] = ["A temporary password is required."];
        if (!await CanManageAsync(request.ParentOrganizationUnitId, cancellationToken)) errors[nameof(request.ParentOrganizationUnitId)] = ["The selected parent organization is outside your permitted hierarchy."];
        var parentType = await dbContext.OrganizationUnits.AsNoTracking().Where(unit => unit.OrganizationUnitId == request.ParentOrganizationUnitId && unit.Status == "ACTIVE").Select(unit => unit.UnitType).SingleOrDefaultAsync(cancellationToken);
        if (parentType is null || !HierarchyRules.IsValidParent(request.UnitType, parentType)) errors[nameof(request.ParentOrganizationUnitId)] = ["Select a valid active parent for this user type."];
        if (!CanCreateType(request.UnitType)) errors[nameof(request.UnitType)] = ["Your role cannot manage this user type."];
        if (request.UnitType is "CMF" or "CSF")
        {
            if (string.IsNullOrWhiteSpace(request.ChannelType)) errors[nameof(request.ChannelType)] = ["Channel type is required."];
            if (string.IsNullOrWhiteSpace(request.PanNumber)) errors[nameof(request.PanNumber)] = ["PAN number is required."];
        }
        if (request.UnitType == "CMF" && string.IsNullOrWhiteSpace(request.MarginType)) errors[nameof(request.MarginType)] = ["Margin type is required."];
        if (request.UnitType == "CSP")
        {
            if (string.IsNullOrWhiteSpace(request.SkuType)) errors[nameof(request.SkuType)] = ["SKU type is required."];
            if (string.IsNullOrWhiteSpace(request.AsmName)) errors[nameof(request.AsmName)] = ["ASM name is required."];
            if (string.IsNullOrWhiteSpace(request.AadhaarNumber)) errors[nameof(request.AadhaarNumber)] = ["Aadhaar number is required."];
            if (request.Latitude is null) errors[nameof(request.Latitude)] = ["Latitude is required."];
            if (request.Longitude is null) errors[nameof(request.Longitude)] = ["Longitude is required."];
            if (request.AadhaarDocument is null && !hasDocument) errors[nameof(request.AadhaarDocument)] = ["An Aadhaar document is required."];
        }
        if (request.AadhaarDocument is { } document && !IsValidAadhaarDocument(document)) errors[nameof(request.AadhaarDocument)] = ["Upload a PDF, JPG, or PNG file no larger than 5 MB."];
        return errors;
    }

    private bool CanCreateType(string type) => type switch
    {
        "CMF" => scopeService.HasRole("PLATFORM_ADMIN"),
        "CSF" => scopeService.HasRole("PLATFORM_ADMIN", "CMF_ADMIN"),
        "CSP" => scopeService.HasRole("PLATFORM_ADMIN", "CMF_ADMIN", "CSF_ADMIN"),
        _ => false
    };

    private static string RoleFor(string type) => type switch { "CMF" => "CMF_ADMIN", "CSF" => "CSF_ADMIN", _ => "CSP_USER" };
    private static string BuildDisplayName(ChannelUserMutationRequest request) => string.Join(' ', new[] { request.Prefix, request.FirstName, request.MiddleName, request.LastName }.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));
    private static string? PreferOrganizationContact(string? userValue, string? organizationValue, bool ignorePlaceholderEmail = false)
    {
        var userValueIsUsable = !string.IsNullOrWhiteSpace(userValue) &&
            (!ignorePlaceholderEmail || !userValue.EndsWith("@invalid.local", StringComparison.OrdinalIgnoreCase));
        return userValueIsUsable ? userValue : string.IsNullOrWhiteSpace(organizationValue) ? userValue : organizationValue;
    }
    private static JsonElement BuildProfileResponse(string? profileJson, string displayName, string organizationName)
    {
        using var profileDocument = JsonDocument.Parse(string.IsNullOrWhiteSpace(profileJson) ? "{}" : profileJson);
        var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (profileDocument.RootElement.ValueKind == JsonValueKind.Object)
            foreach (var property in profileDocument.RootElement.EnumerateObject())
                values[char.ToLowerInvariant(property.Name[0]) + property.Name[1..]] = property.Value.Clone();
        var name = SplitDisplayName(displayName);
        AddProfileFallback(values, "prefix", name.Prefix);
        AddProfileFallback(values, "firstName", name.FirstName);
        AddProfileFallback(values, "middleName", name.MiddleName);
        AddProfileFallback(values, "lastName", name.LastName);
        AddProfileFallback(values, "companyName", organizationName);
        return JsonSerializer.SerializeToElement(values, ProfileJsonOptions);
    }
    private static (string? Prefix, string FirstName, string? MiddleName, string? LastName) SplitDisplayName(string displayName)
    {
        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return (null, string.Empty, null, null);
        var hasPrefix = new[] { "MR", "MRS", "MS", "M/S" }.Contains(parts[0].TrimEnd('.').ToUpperInvariant());
        var names = hasPrefix ? parts[1..] : parts;
        if (names.Length == 0) return (hasPrefix ? parts[0].TrimEnd('.') : null, string.Empty, null, null);
        return (hasPrefix ? parts[0].TrimEnd('.') : null, names[0], names.Length > 2 ? string.Join(' ', names[1..^1]) : null,
            names.Length > 1 ? names[^1] : null);
    }
    private static void AddProfileFallback(IDictionary<string, JsonElement> values, string key, string? fallback)
    {
        if (string.IsNullOrWhiteSpace(fallback)) return;
        if (values.TryGetValue(key, out var current) && current.ValueKind != JsonValueKind.Null &&
            (current.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(current.GetString()))) return;
        values[key] = JsonSerializer.SerializeToElement(fallback, ProfileJsonOptions);
    }
    private static string SerializeProfile(ChannelUserMutationRequest request)
    {
        var values = request.GetType().GetProperties().Where(property => !ProfileExcludedProperties.Contains(property.Name))
            .ToDictionary(property => char.ToLowerInvariant(property.Name[0]) + property.Name[1..], property => property.GetValue(request));
        return JsonSerializer.Serialize(values, ProfileJsonOptions);
    }
    private static bool IsValidAadhaarDocument(IFormFile document)
    {
        var extension = Path.GetExtension(document.FileName).ToLowerInvariant();
        return document.Length is > 0 and <= 5 * 1024 * 1024 && new[] { ".pdf", ".jpg", ".jpeg", ".png" }.Contains(extension) &&
            new[] { "application/pdf", "image/jpeg", "image/png" }.Contains(document.ContentType.ToLowerInvariant());
    }
    private static async Task SetAadhaarDocumentAsync(UserProfile profile, IFormFile? document, CancellationToken cancellationToken)
    {
        if (document is null) return;
        await using var stream = new MemoryStream(); await document.CopyToAsync(stream, cancellationToken);
        profile.AadhaarDocumentContent = stream.ToArray(); profile.AadhaarDocumentContentType = document.ContentType;
        profile.AadhaarDocumentFileName = Path.GetFileName(document.FileName);
    }
    private void AddChannelAudit(string action, Guid userId, Guid organizationUnitId, object details) => dbContext.AuditLogs.Add(new AuditLog
    {
        ActorUserId = scopeService.UserId, OrganizationUnitId = organizationUnitId, Action = action, EntityType = "User",
        EntityId = userId.ToString(), CorrelationId = Guid.NewGuid(), IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        DetailsJson = JsonSerializer.Serialize(details), OccurredAtUtc = DateTime.UtcNow
    });
}
