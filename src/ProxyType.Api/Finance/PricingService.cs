using Microsoft.EntityFrameworkCore;
using ProxyType.Api.Data;
using ProxyType.Api.Domain;

namespace ProxyType.Api.Finance;

public sealed class PricingService(ProxyTypeDbContext db)
{
    private static readonly string[] RolePriority = ["PLATFORM_ADMIN", "CMF_ADMIN", "CSF_ADMIN", "CSP_USER"];

    public async Task<decimal> ChargeAsync(Guid serviceId, Guid userId, decimal amount, CancellationToken ct = default)
    {
        var roleId = await PricingRoleIdAsync(userId, ct);
        var now = DateTime.UtcNow;
        var rules = await db.PricingRules.AsNoTracking()
            .Where(x => x.ServiceId == serviceId && x.RuleKind == "CHARGE" && x.IsActive &&
                x.AmountFrom <= amount && x.AmountTo >= amount && x.EffectiveFromUtc <= now &&
                (x.EffectiveToUtc == null || x.EffectiveToUtc > now) && (x.RoleId == roleId || x.RoleId == null))
            .ToListAsync(ct);
        var rule = SelectRule(rules, roleId, "charge slab");
        return rule is null ? 0 : Calculate(rule.CalculationType, rule.Rate, amount);
    }

    public async Task<decimal> RechargeCommissionAsync(Guid operatorId, Guid userId, decimal amount, CancellationToken ct = default)
    {
        var roleId = await PricingRoleIdAsync(userId, ct);
        var now = DateTime.UtcNow;
        var rules = await db.RechargeCommissionRules.AsNoTracking()
            .Where(x => x.RechargeOperatorId == operatorId && x.IsActive && x.EffectiveFromUtc <= now &&
                (x.EffectiveToUtc == null || x.EffectiveToUtc > now) && (x.UserId == userId || x.RoleId == roleId || (x.UserId == null && x.RoleId == null)))
            .ToListAsync(ct);
        var rule = SelectRechargeCommissionRule(rules, userId, roleId);
        return rule is null ? 0 : Calculate(rule.CalculationType, rule.Rate, amount);
    }

    public static RechargeCommissionRule? SelectRechargeCommissionRule(IReadOnlyList<RechargeCommissionRule> rules, Guid userId, Guid roleId)
    {
        var userMatches = rules.Where(x => x.UserId == userId).ToArray();
        if (userMatches.Length > 1) throw new InvalidOperationException("Multiple active user recharge commission records match this transaction.");
        return userMatches.SingleOrDefault() ?? SelectRule(rules.Where(x => x.UserId == null).ToArray(), roleId, "recharge commission");
    }

    public static decimal Calculate(string calculationType, decimal rate, decimal amount) => calculationType switch
    {
        "FIXED" => decimal.Round(rate, 4, MidpointRounding.AwayFromZero),
        "PERCENTAGE" => decimal.Round(amount * rate / 100m, 4, MidpointRounding.AwayFromZero),
        _ => throw new InvalidOperationException($"Unsupported pricing calculation type '{calculationType}'.")
    };

    private async Task<Guid> PricingRoleIdAsync(Guid userId, CancellationToken ct)
    {
        var roles = await db.UserRoles.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new { x.RoleId, x.Role.Code }).ToListAsync(ct);
        foreach (var code in RolePriority)
        {
            var role = roles.FirstOrDefault(x => x.Code == code);
            if (role is not null) return role.RoleId;
        }
        throw new InvalidOperationException("The user has no supported pricing role.");
    }

    private static T? SelectRule<T>(IReadOnlyList<T> rules, Guid roleId, string description) where T : class
    {
        static Guid? RoleId(T item) => item switch
        {
            PricingRule rule => rule.RoleId,
            RechargeCommissionRule rule => rule.RoleId,
            _ => throw new InvalidOperationException("Unsupported pricing rule.")
        };
        var matches = rules.Where(x => RoleId(x) == roleId).ToArray();
        if (matches.Length == 0) matches = rules.Where(x => RoleId(x) is null).ToArray();
        if (matches.Length > 1) throw new InvalidOperationException($"Multiple active {description} records match this transaction.");
        return matches.SingleOrDefault();
    }
}
