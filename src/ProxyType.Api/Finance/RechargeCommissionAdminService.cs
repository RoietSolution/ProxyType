using Microsoft.EntityFrameworkCore;
using ProxyType.Api.Contracts;
using ProxyType.Api.Data;
using ProxyType.Api.Domain;

namespace ProxyType.Api.Finance;

public sealed class RechargeCommissionAdminService(ProxyTypeDbContext db)
{
    private static readonly string[] PricingRoles = ["PLATFORM_ADMIN", "CMF_ADMIN", "CSF_ADMIN", "CSP_USER"];

    public async Task<IReadOnlyList<PricingRoleOption>> RolesAsync(CancellationToken ct = default) =>
        await db.Roles.AsNoTracking().Where(x => PricingRoles.Contains(x.Code)).OrderBy(x => x.Name)
            .Select(x => new PricingRoleOption(x.RoleId, x.Code, x.Name)).ToArrayAsync(ct);

    public async Task<IReadOnlyList<RechargeCommissionResponse>> ListAsync(Guid roleId, CancellationToken ct = default)
    {
        var role = await db.Roles.AsNoTracking().SingleOrDefaultAsync(x => x.RoleId == roleId && PricingRoles.Contains(x.Code), ct)
            ?? throw new InvalidOperationException("Select a supported pricing role.");
        var now = DateTime.UtcNow;
        var operators = await db.RechargeOperators.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Type).ThenBy(x => x.Name).ToArrayAsync(ct);
        var rules = await db.RechargeCommissionRules.AsNoTracking().Where(x => x.RoleId == roleId && x.IsActive &&
            x.EffectiveFromUtc <= now && (x.EffectiveToUtc == null || x.EffectiveToUtc > now)).ToDictionaryAsync(x => x.RechargeOperatorId, ct);
        return operators.Select(x => rules.TryGetValue(x.RechargeOperatorId, out var rule)
            ? new RechargeCommissionResponse(x.RechargeOperatorId, x.Name, x.Label, x.Type, role.RoleId, role.Name, null, null, false, rule.CalculationType, rule.Rate)
            : new RechargeCommissionResponse(x.RechargeOperatorId, x.Name, x.Label, x.Type, role.RoleId, role.Name, null, null, false, "PERCENTAGE", 0)).ToArray();
    }

    public async Task<IReadOnlyList<RechargeCommissionResponse>> ListForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var (user, cspRole) = await CspUserAsync(userId, ct);
        var now = DateTime.UtcNow;
        var operators = await db.RechargeOperators.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Type).ThenBy(x => x.Name).ToArrayAsync(ct);
        var groupRules = await db.RechargeCommissionRules.AsNoTracking().Where(x => x.RoleId == cspRole.RoleId && x.UserId == null && x.IsActive &&
            x.EffectiveFromUtc <= now && (x.EffectiveToUtc == null || x.EffectiveToUtc > now)).ToDictionaryAsync(x => x.RechargeOperatorId, ct);
        var userRules = await db.RechargeCommissionRules.AsNoTracking().Where(x => x.UserId == userId && x.IsActive &&
            x.EffectiveFromUtc <= now && (x.EffectiveToUtc == null || x.EffectiveToUtc > now)).ToDictionaryAsync(x => x.RechargeOperatorId, ct);
        return operators.Select(op =>
        {
            userRules.TryGetValue(op.RechargeOperatorId, out var userRule);
            groupRules.TryGetValue(op.RechargeOperatorId, out var groupRule);
            var selected = userRule ?? groupRule;
            return new RechargeCommissionResponse(op.RechargeOperatorId, op.Name, op.Label, op.Type, cspRole.RoleId, cspRole.Name,
                user.UserId, user.DisplayName, userRule is not null, selected?.CalculationType ?? "PERCENTAGE", selected?.Rate ?? 0);
        }).ToArray();
    }

    public async Task<IReadOnlyList<RechargeCommissionResponse>> SaveAsync(SaveRechargeCommissionRequest request, CancellationToken ct = default)
    {
        if (request.UserId.HasValue)
        {
            if (request.RoleId.HasValue) throw new InvalidOperationException("Select either a role group or a CSP user, not both.");
            return await SaveForUserAsync(request.UserId.Value, request.Operators, ct);
        }
        if (!request.RoleId.HasValue) throw new InvalidOperationException("Select a role group or CSP user.");
        var roleId = request.RoleId.Value;
        _ = await db.Roles.SingleOrDefaultAsync(x => x.RoleId == roleId && PricingRoles.Contains(x.Code), ct)
            ?? throw new InvalidOperationException("Select a supported pricing role.");
        var operatorIds = await ValidateOperatorsAsync(request.Operators, ct);
        var existing = await db.RechargeCommissionRules.Where(x => x.RoleId == roleId && x.UserId == null && operatorIds.Contains(x.RechargeOperatorId) && x.IsActive).ToListAsync(ct);
        foreach (var value in request.Operators)
        {
            var rule = existing.SingleOrDefault(x => x.RechargeOperatorId == value.OperatorId);
            if (rule is null)
            {
                rule = new RechargeCommissionRule { RechargeCommissionRuleId = Guid.NewGuid(), RechargeOperatorId = value.OperatorId,
                    RoleId = roleId, EffectiveFromUtc = DateTime.UtcNow, IsActive = true };
                db.RechargeCommissionRules.Add(rule);
            }
            rule.CalculationType = value.CalculationType.ToUpperInvariant(); rule.Rate = value.Rate;
            rule.EffectiveToUtc = null; rule.IsActive = true;
        }
        await db.SaveChangesAsync(ct);
        return await ListAsync(roleId, ct);
    }

    private async Task<IReadOnlyList<RechargeCommissionResponse>> SaveForUserAsync(Guid userId, IReadOnlyList<RechargeCommissionValueRequest> values, CancellationToken ct)
    {
        _ = await CspUserAsync(userId, ct);
        var operatorIds = await ValidateOperatorsAsync(values, ct);
        var existing = await db.RechargeCommissionRules.Where(x => x.UserId == userId && operatorIds.Contains(x.RechargeOperatorId) && x.IsActive).ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var value in values)
        {
            var rule = existing.SingleOrDefault(x => x.RechargeOperatorId == value.OperatorId);
            if (!value.UseUserOverride)
            {
                if (rule is not null) { rule.IsActive = false; rule.EffectiveToUtc = now; }
                continue;
            }
            if (rule is null)
            {
                rule = new RechargeCommissionRule { RechargeCommissionRuleId = Guid.NewGuid(), RechargeOperatorId = value.OperatorId,
                    UserId = userId, EffectiveFromUtc = now, IsActive = true };
                db.RechargeCommissionRules.Add(rule);
            }
            rule.CalculationType = value.CalculationType.ToUpperInvariant(); rule.Rate = value.Rate;
            rule.EffectiveToUtc = null; rule.IsActive = true;
        }
        await db.SaveChangesAsync(ct);
        return await ListForUserAsync(userId, ct);
    }

    private async Task<Guid[]> ValidateOperatorsAsync(IReadOnlyList<RechargeCommissionValueRequest> values, CancellationToken ct)
    {
        if (values.Count == 0) throw new InvalidOperationException("At least one recharge operator is required.");
        if (values.Select(x => x.OperatorId).Distinct().Count() != values.Count)
            throw new InvalidOperationException("Each recharge operator may appear only once.");
        var ids = values.Select(x => x.OperatorId).ToArray();
        if (await db.RechargeOperators.CountAsync(x => ids.Contains(x.RechargeOperatorId) && x.IsActive, ct) != ids.Length)
            throw new InvalidOperationException("One or more recharge operators are invalid or inactive.");
        return ids;
    }

    private async Task<(AppUser User, AppRole Role)> CspUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.IsActive, ct)
            ?? throw new InvalidOperationException("Select an active CSP user.");
        var role = await db.UserRoles.AsNoTracking().Where(x => x.UserId == userId && x.Role.Code == "CSP_USER").Select(x => x.Role).SingleOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("The selected user is not a CSP user.");
        return (user, role);
    }
}
