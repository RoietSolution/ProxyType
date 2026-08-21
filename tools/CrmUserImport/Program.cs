using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using ProxyType.Api.Domain;
using System.Security.Cryptography;

var sourceConnection = Environment.GetEnvironmentVariable("PROXYTYPE_CRM_CONNECTION")
    ?? "Server=DESKTOP-DQ0868S;Database=CRM;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;";
var targetConnection = Environment.GetEnvironmentVariable("PROXYTYPE_DB_CONNECTION")
    ?? "Server=localhost;Database=proxytype_DB;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;";
var apply = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);
if (!apply)
{
    Console.WriteLine("DRY RUN: use --apply to import CRM DWUSER identities. Passwords are never copied; imported users require an administrator reset.");
}

await using var source = new SqlConnection(sourceConnection);
await using var target = new SqlConnection(targetConnection);
await source.OpenAsync(); await target.OpenAsync();
var roleId = await ScalarGuidAsync(target, "SELECT RoleId FROM auth.Roles WHERE Code='CSP_USER'")
    ?? throw new InvalidOperationException("CSP_USER role is missing. Apply database/001_initial.sql first.");
var hasher = new PasswordHasher<AppUser>();
var imported = 0; var active = 0;
await using var read = new SqlCommand("SELECT u.USER_ID,u.USER_CODE,u.USER_NAME,u.USER_EMAIL,u.USER_MOBILE,u.USER_STATUS,u.USER_TYPE, g.GROUP_NAME, a.REGION_ID,a.STATE_IDS FROM dbo.DWUSER u LEFT JOIN dbo.DWUSERGROUP ug ON ug.USER_ID=u.USER_ID LEFT JOIN dbo.DWGROUP g ON g.GROUP_ID=ug.GROUP_ID LEFT JOIN dbo.DWUSERAREA a ON a.USER_ID=u.USER_ID ORDER BY u.USER_ID", source);
await using var rows = await read.ExecuteReaderAsync();
while (await rows.ReadAsync())
{
    var legacyId = rows.GetInt32(0); var code = Text(rows, 1, $"USER{legacyId}"); var displayName = Text(rows, 2, code);
    var email = Text(rows, 3, $"crm-{legacyId}@import.invalid"); if (email.Length > 256) email = email[..256];
    var mobile = NullableText(rows, 4); var isActive = !rows.IsDBNull(5) && rows.GetInt32(5) == 1; var userType = rows.IsDBNull(6) ? (int?)null : rows.GetInt32(6);
    var group = NullableText(rows, 7); var region = rows.IsDBNull(8) ? (int?)null : rows.GetInt32(8); var stateIds = NullableText(rows, 9);
    if (!apply) { imported++; if (isActive) active++; continue; }
    var userId = await ScalarGuidAsync(target, "SELECT UserId FROM auth.Users WHERE LegacySystem='CRM' AND LegacyId=@legacy", new SqlParameter("@legacy", legacyId.ToString()));
    if (userId is null)
    {
        userId = Guid.NewGuid();
        var username = UniqueUsername(code, legacyId);
        if (await ScalarIntAsync(target, "SELECT COUNT(*) FROM auth.Users WHERE Username=@value", new SqlParameter("@value", username)) > 0) username = UniqueUsername($"{code}_legacy", legacyId);
        if (await ScalarIntAsync(target, "SELECT COUNT(*) FROM auth.Users WHERE Email=@value", new SqlParameter("@value", email)) > 0) email = $"crm-{legacyId}@import.invalid";
        if (mobile is not null && await ScalarIntAsync(target, "SELECT COUNT(*) FROM auth.Users WHERE Mobile=@value", new SqlParameter("@value", mobile)) > 0) mobile = null;
        var placeholder = new AppUser { UserId = userId.Value, Username = username, Email = email, DisplayName = displayName };
        var randomPassword = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + "!";
        var passwordHash = hasher.HashPassword(placeholder, randomPassword);
        await ExecuteAsync(target, "INSERT auth.Users(UserId,Username,Email,Mobile,DisplayName,PasswordHash,IsActive,MustChangePassword,LegacySystem,LegacyId) VALUES(@id,@username,@email,@mobile,@display,@hash,@active,1,'CRM',@legacy)",
            new("@id", userId), new("@username", username), new("@email", email), new("@mobile", (object?)mobile ?? DBNull.Value), new("@display", displayName), new("@hash", passwordHash), new("@active", isActive), new("@legacy", legacyId.ToString()));
        await ExecuteAsync(target, "INSERT auth.UserRoles(UserId,RoleId) VALUES(@user,@role)", new("@user", userId), new("@role", roleId));
    }
    await ExecuteAsync(target, "MERGE auth.LegacyUserImportMap AS t USING (SELECT @user UserId,@legacy LegacyUserId) AS s ON t.LegacySystem='CRM' AND t.LegacyUserId=s.LegacyUserId WHEN MATCHED THEN UPDATE SET UserId=s.UserId,LegacyUserType=@type,LegacyGroup=@group,LegacyRegionId=@region,LegacyStateIds=@states,ImportedAtUtc=SYSUTCDATETIME() WHEN NOT MATCHED THEN INSERT(UserId,LegacySystem,LegacyUserId,LegacyUserType,LegacyGroup,LegacyRegionId,LegacyStateIds) VALUES(s.UserId,'CRM',s.LegacyUserId,@type,@group,@region,@states);",
        new("@user", userId), new("@legacy", legacyId), new("@type", (object?)userType ?? DBNull.Value), new("@group", (object?)group ?? DBNull.Value), new("@region", (object?)region ?? DBNull.Value), new("@states", (object?)stateIds ?? DBNull.Value));
    imported++; if (isActive) active++;
}
await rows.DisposeAsync();
Console.WriteLine($"CRM DWUSER records processed: {imported}; active source records: {active}; mode: {(apply ? "APPLY" : "DRY RUN")}");

var cmfUnits = new Dictionary<int, Guid>();
var csfUnits = new Dictionary<int, Guid>();
var hierarchyUsers = 0;
var hierarchyMemberships = 0;
var cmfCount = 0;
var csfCount = 0;
var cspCount = 0;

if (apply)
{
    await ImportCmfCsfAsync();
    await ImportCspAsync();
}
else
{
    await using var countCommand = new SqlCommand("SELECT SUM(CASE WHEN USERTYPE=1 THEN 1 ELSE 0 END),SUM(CASE WHEN USERTYPE=2 THEN 1 ELSE 0 END) FROM dbo.CMFREG WHERE USERTYPE IN (1,2)", source);
    await using var countReader = await countCommand.ExecuteReaderAsync();
    if (await countReader.ReadAsync()) { cmfCount = countReader.IsDBNull(0) ? 0 : countReader.GetInt32(0); csfCount = countReader.IsDBNull(1) ? 0 : countReader.GetInt32(1); }
    await countReader.DisposeAsync();
    cmfCount = await ScalarIntAsync(source, "SELECT COUNT(*) FROM dbo.CMFREG WHERE USERTYPE=1");
    csfCount = await ScalarIntAsync(source, "SELECT COUNT(*) FROM dbo.CMFREG WHERE USERTYPE=2");
    cspCount = await ScalarIntAsync(source, "SELECT COUNT(DISTINCT CSP_ID) FROM dbo.CSPREG");
}

Console.WriteLine($"CRM hierarchy processed: CMF={cmfCount}; CSF={csfCount}; CSP={cspCount}; users created/reused={hierarchyUsers}; memberships created/reused={hierarchyMemberships}; mode: {(apply ? "APPLY" : "DRY RUN")}");

async Task ImportCmfCsfAsync()
{
    await using var command = new SqlCommand("SELECT CMF_ID,CMFCODE,CMFNAME,MOBILENO,EMAIL,PARENTID,CMFSTATUS,STATUS,USERTYPE FROM dbo.CMFREG WHERE USERTYPE IN (1,2) ORDER BY USERTYPE,CMF_ID", source);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var legacyId = Convert.ToInt32(reader.GetValue(0));
        var unitType = Convert.ToInt32(reader.GetValue(8)) == 1 ? "CMF" : "CSF";
        var parentId = reader.IsDBNull(5) ? (int?)null : Convert.ToInt32(reader.GetValue(5));
        var parentUnitId = unitType == "CMF" ? await ScalarGuidAsync(target, "SELECT OrganizationUnitId FROM org.OrganizationUnits WHERE UnitType='PLATFORM' AND Code='PLATFORM'") : parentId is not null && cmfUnits.TryGetValue(parentId.Value, out var cmfParent) ? cmfParent : null;
        if (unitType == "CSF" && parentUnitId is null) throw new InvalidOperationException($"CRM CSF {legacyId} has no resolvable CMF parent {parentId}.");
        var name = Text(reader, 2, $"CRM {unitType} {legacyId}");
        var email = NullableText(reader, 4);
        var mobile = NullableText(reader, 3);
        var active = string.Equals(NullableText(reader, 6), "A", StringComparison.OrdinalIgnoreCase) || string.Equals(NullableText(reader, 7), "A", StringComparison.OrdinalIgnoreCase);
        var unitId = await UpsertUnitAsync(unitType, $"CRM-{unitType}-{legacyId}", name, parentUnitId, email, mobile, active ? "ACTIVE" : "PENDING", $"CMF:{legacyId}");
        if (unitType == "CMF") cmfUnits[legacyId] = unitId; else csfUnits[legacyId] = unitId;
        var userId = await EnsureHierarchyUserAsync(unitType == "CMF" ? "CRM_CMF" : "CRM_CSF", legacyId, name, email, mobile, active, unitType == "CMF" ? "CMF_ADMIN" : "CSF_ADMIN");
        await EnsureMembershipAsync(unitId, userId);
        if (unitType == "CMF") cmfCount++; else csfCount++;
    }
}

async Task ImportCspAsync()
{
    const string sql = "WITH latest AS (SELECT CSP_ID,CSPCODE,CSPNAME,EMAIL1,MOBILENO1,CSF_ID,CMF_ID,STATUS_ID,ROW_NUMBER() OVER(PARTITION BY CSP_ID ORDER BY CSPREG_CRDATE DESC) rn FROM dbo.CSPREG) SELECT CSP_ID,CSPCODE,CSPNAME,EMAIL1,MOBILENO1,CSF_ID,CMF_ID,STATUS_ID FROM latest WHERE rn=1 ORDER BY CSP_ID";
    await using var command = new SqlCommand(sql, source);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var legacyId = Convert.ToInt32(reader.GetValue(0));
        var csfId = reader.IsDBNull(5) ? (int?)null : Convert.ToInt32(reader.GetValue(5));
        var cmfId = reader.IsDBNull(6) ? (int?)null : Convert.ToInt32(reader.GetValue(6));
        Guid parentUnitId;
        if (csfId is not null && csfId != 0 && csfUnits.TryGetValue(csfId.Value, out var csfParent)) parentUnitId = csfParent;
        else
        {
            Console.WriteLine($"WARNING: CRM CSP {legacyId} skipped because it has no valid CSF parent. CSF={csfId}; CMF={cmfId}. The target hierarchy requires PLATFORM -> CMF -> CSF -> CSP.");
            continue;
        }
        var name = Text(reader, 2, $"CRM CSP {legacyId}");
        var email = NullableText(reader, 3);
        var mobile = NullableText(reader, 4);
        // STATUS_ID semantics were not verified in the legacy application; imported CSPs stay inactive until reviewed.
        var unitId = await UpsertUnitAsync("CSP", $"CRM-CSP-{legacyId}", name, parentUnitId, email, mobile, "PENDING", $"CSP:{legacyId}");
        var userId = await EnsureHierarchyUserAsync("CRM_CSP", legacyId, name, email, mobile, false, "CSP_USER");
        await EnsureMembershipAsync(unitId, userId);
        cspCount++;
    }
}

async Task<Guid> UpsertUnitAsync(string unitType, string code, string name, Guid? parentId, string? email, string? mobile, string status, string legacyId)
{
    var existing = await ScalarGuidAsync(target, "SELECT OrganizationUnitId FROM org.OrganizationUnits WHERE LegacySystem='CRM' AND LegacyId=@legacy", new SqlParameter("@legacy", legacyId));
    if (existing is not null)
    {
        await ExecuteAsync(target, "UPDATE org.OrganizationUnits SET ParentOrganizationUnitId=@parent,Name=@name,Email=@email,Mobile=@mobile,Status=@status,UpdatedAtUtc=SYSUTCDATETIME() WHERE OrganizationUnitId=@id", new("@id", existing), new("@parent", (object?)parentId ?? DBNull.Value), new("@name", name), new("@email", (object?)email ?? DBNull.Value), new("@mobile", (object?)mobile ?? DBNull.Value), new("@status", status));
        return existing.Value;
    }
    var id = Guid.NewGuid();
    await ExecuteAsync(target, "INSERT org.OrganizationUnits(OrganizationUnitId,ParentOrganizationUnitId,UnitType,Code,Name,Status,CreatedAtUtc,UpdatedAtUtc,LegacySystem,LegacyId) VALUES(@id,@parent,@type,@code,@name,@status,SYSUTCDATETIME(),SYSUTCDATETIME(),'CRM',@legacy)", new("@id", id), new("@parent", (object?)parentId ?? DBNull.Value), new("@type", unitType), new("@code", code), new("@name", name), new("@status", status), new("@legacy", legacyId));
    return id;
}

async Task<Guid> EnsureHierarchyUserAsync(string legacySystem, int legacyId, string displayName, string? email, string? mobile, bool active, string roleCode)
{
    var existing = await ScalarGuidAsync(target, "SELECT UserId FROM auth.Users WHERE LegacySystem=@system AND LegacyId=@legacy", new("@system", legacySystem), new("@legacy", legacyId.ToString()));
    if (existing is not null) return existing.Value;
    var userId = Guid.NewGuid();
    var username = $"{legacySystem.ToLowerInvariant()}_{legacyId}";
    if (await ScalarIntAsync(target, "SELECT COUNT(*) FROM auth.Users WHERE Username=@value", new SqlParameter("@value", username)) > 0) { var suffix = Guid.NewGuid().ToString("N"); username = $"{username}_{suffix}"[..Math.Min(100, $"{username}_{suffix}".Length)]; }
    email = string.IsNullOrWhiteSpace(email) ? $"{legacySystem.ToLowerInvariant()}-{legacyId}@import.invalid" : email.Trim();
    if (email.Length > 256) email = email[..256];
    if (await ScalarIntAsync(target, "SELECT COUNT(*) FROM auth.Users WHERE Email=@value", new SqlParameter("@value", email)) > 0) email = $"{legacySystem.ToLowerInvariant()}-{legacyId}@import.invalid";
    mobile = string.IsNullOrWhiteSpace(mobile) ? null : mobile.Trim();
    if (mobile is not null && await ScalarIntAsync(target, "SELECT COUNT(*) FROM auth.Users WHERE Mobile=@value", new SqlParameter("@value", mobile)) > 0) mobile = null;
    var model = new AppUser { UserId = userId, Username = username, Email = email, DisplayName = displayName };
    var passwordHash = hasher.HashPassword(model, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    await ExecuteAsync(target, "INSERT auth.Users(UserId,Username,Email,Mobile,DisplayName,PasswordHash,IsActive,MustChangePassword,LegacySystem,LegacyId) VALUES(@id,@username,@email,@mobile,@display,@hash,@active,1,@system,@legacy)", new("@id", userId), new("@username", username), new("@email", email), new("@mobile", (object?)mobile ?? DBNull.Value), new("@display", displayName), new("@hash", passwordHash), new("@active", active), new("@system", legacySystem), new("@legacy", legacyId.ToString()));
    var roleId = await ScalarGuidAsync(target, "SELECT RoleId FROM auth.Roles WHERE Code=@role", new SqlParameter("@role", roleCode)) ?? throw new InvalidOperationException($"Role {roleCode} is missing.");
    await ExecuteAsync(target, "INSERT auth.UserRoles(UserId,RoleId) VALUES(@user,@role)", new("@user", userId), new("@role", roleId));
    hierarchyUsers++;
    return userId;
}

async Task EnsureMembershipAsync(Guid unitId, Guid userId)
{
    if (await ScalarIntAsync(target, "SELECT COUNT(*) FROM org.OrganizationMemberships WHERE OrganizationUnitId=@unit AND UserId=@user", new("@unit", unitId), new("@user", userId)) == 0)
    {
        await ExecuteAsync(target, "INSERT org.OrganizationMemberships(OrganizationMembershipId,OrganizationUnitId,UserId,IsPrimary,IsActive,ValidFromUtc) VALUES(@id,@unit,@user,1,1,SYSUTCDATETIME())", new("@id", Guid.NewGuid()), new("@unit", unitId), new("@user", userId));
        hierarchyMemberships++;
    }
    else await ExecuteAsync(target, "UPDATE org.OrganizationMemberships SET IsActive=1,IsPrimary=1 WHERE OrganizationUnitId=@unit AND UserId=@user", new("@unit", unitId), new("@user", userId));
}

static string Text(SqlDataReader reader, int ordinal, string fallback) => reader.IsDBNull(ordinal) ? fallback : reader.GetString(ordinal).Trim();
static string? NullableText(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetValue(ordinal)?.ToString()?.Trim();
static string UniqueUsername(string code, int legacyId) => $"crm_{legacyId}_{code}"[..Math.Min(100, $"crm_{legacyId}_{code}".Length)];
static async Task<int> ExecuteAsync(SqlConnection connection, string sql, params SqlParameter[] parameters) { await using var command = new SqlCommand(sql, connection); command.Parameters.AddRange(parameters); return await command.ExecuteNonQueryAsync(); }
static async Task<Guid?> ScalarGuidAsync(SqlConnection connection, string sql, params SqlParameter[] parameters) { await using var command = new SqlCommand(sql, connection); command.Parameters.AddRange(parameters); var value = await command.ExecuteScalarAsync(); return value is Guid guid ? guid : value is null || value == DBNull.Value ? null : Guid.Parse(value.ToString()!); }
static async Task<int> ScalarIntAsync(SqlConnection connection, string sql, params SqlParameter[] parameters) { await using var command = new SqlCommand(sql, connection); command.Parameters.AddRange(parameters); return Convert.ToInt32(await command.ExecuteScalarAsync()); }
