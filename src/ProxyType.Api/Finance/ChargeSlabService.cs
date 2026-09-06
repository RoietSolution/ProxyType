using Microsoft.EntityFrameworkCore;
using ProxyType.Api.Contracts;
using ProxyType.Api.Data;
using ProxyType.Api.Domain;

namespace ProxyType.Api.Finance;

public sealed class ChargeSlabService(ProxyTypeDbContext db)
{
    private static readonly string[] PricingRoles = ["PLATFORM_ADMIN", "CMF_ADMIN", "CSF_ADMIN", "CSP_USER"];

    public async Task<PricingMetadataResponse> MetadataAsync(CancellationToken ct = default) => new(
        await db.Services.AsNoTracking().Where(x => x.IsActive && x.IsChargeable).OrderBy(x => x.Name)
            .Select(x => new PricingServiceOption(x.ServiceId, x.Code, x.Name)).ToArrayAsync(ct),
        await db.Roles.AsNoTracking().Where(x => PricingRoles.Contains(x.Code)).OrderBy(x => x.Name)
            .Select(x => new PricingRoleOption(x.RoleId, x.Code, x.Name)).ToArrayAsync(ct));

    public async Task<IReadOnlyList<ChargeSlabResponse>> ListAsync(Guid serviceId, Guid roleId, CancellationToken ct = default) =>
        await db.PricingRules.AsNoTracking()
            .Where(x => x.ServiceId == serviceId && x.RoleId == roleId && x.RuleKind == "CHARGE" && x.IsActive)
            .OrderBy(x => x.AmountFrom)
            .Select(x => new ChargeSlabResponse(x.PricingRuleId, x.ServiceId, x.Service.Name, x.RoleId!.Value,
                x.Role!.Name, x.AmountFrom, x.AmountTo, x.CalculationType, x.Rate, x.TdsRate, x.GstRate))
            .ToArrayAsync(ct);

    public async Task<ChargeSlabResponse> SaveAsync(SaveChargeSlabRequest request, CancellationToken ct = default)
    {
        if (request.AmountTo < request.AmountFrom) throw new InvalidOperationException("Amount To must be greater than or equal to Amount From.");
        var service = await db.Services.SingleOrDefaultAsync(x => x.ServiceId == request.ServiceId && x.IsActive && x.IsChargeable, ct)
            ?? throw new InvalidOperationException("Select an active chargeable service.");
        var role = await db.Roles.SingleOrDefaultAsync(x => x.RoleId == request.RoleId && PricingRoles.Contains(x.Code), ct)
            ?? throw new InvalidOperationException("Select a supported pricing role.");
        var now = DateTime.UtcNow;
        var overlaps = await db.PricingRules.AnyAsync(x => x.PricingRuleId != request.PricingRuleId && x.ServiceId == request.ServiceId &&
            x.RoleId == request.RoleId && x.RuleKind == "CHARGE" && x.IsActive && x.EffectiveToUtc == null &&
            x.AmountFrom <= request.AmountTo && request.AmountFrom <= x.AmountTo, ct);
        if (overlaps) throw new InvalidOperationException("This amount range overlaps an active charge slab.");

        PricingRule rule;
        if (request.PricingRuleId is Guid id)
        {
            rule = await db.PricingRules.SingleOrDefaultAsync(x => x.PricingRuleId == id && x.RuleKind == "CHARGE", ct)
                ?? throw new KeyNotFoundException("Charge slab was not found.");
            rule.ServiceId = request.ServiceId; rule.RoleId = request.RoleId;
        }
        else
        {
            rule = new PricingRule { PricingRuleId = Guid.NewGuid(), ServiceId = request.ServiceId, RoleId = request.RoleId,
                RuleKind = "CHARGE", EffectiveFromUtc = now, IsActive = true };
            db.PricingRules.Add(rule);
        }
        rule.AmountFrom = request.AmountFrom; rule.AmountTo = request.AmountTo;
        rule.CalculationType = request.CalculationType.ToUpperInvariant(); rule.Rate = request.Rate;
        rule.TdsRate = request.TdsRate; rule.GstRate = request.GstRate; rule.EffectiveToUtc = null; rule.IsActive = true;
        await db.SaveChangesAsync(ct);
        return new(rule.PricingRuleId, rule.ServiceId, service.Name, role.RoleId, role.Name, rule.AmountFrom, rule.AmountTo,
            rule.CalculationType, rule.Rate, rule.TdsRate, rule.GstRate);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var rule = await db.PricingRules.SingleOrDefaultAsync(x => x.PricingRuleId == id && x.RuleKind == "CHARGE" && x.IsActive, ct)
            ?? throw new KeyNotFoundException("Charge slab was not found.");
        rule.IsActive = false; rule.EffectiveToUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
