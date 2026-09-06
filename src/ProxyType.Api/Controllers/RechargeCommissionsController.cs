using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProxyType.Api.Contracts;
using ProxyType.Api.Finance;

namespace ProxyType.Api.Controllers;

[ApiController, Authorize(Roles = "PLATFORM_ADMIN"), Route("api/recharge-commissions")]
public sealed class RechargeCommissionsController(RechargeCommissionAdminService service) : ControllerBase
{
    [HttpGet("roles")]
    public async Task<ActionResult<IReadOnlyList<PricingRoleOption>>> Roles(CancellationToken ct) => Ok(await service.RolesAsync(ct));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RechargeCommissionResponse>>> List(Guid? roleId, Guid? userId, CancellationToken ct)
    {
        try
        {
            if (userId.HasValue && roleId.HasValue) return BadRequest(new ProblemDetails { Title = "Invalid commission scope.", Detail = "Select either a role group or a CSP user.", Status = 400 });
            if (userId.HasValue) return Ok(await service.ListForUserAsync(userId.Value, ct));
            if (roleId.HasValue) return Ok(await service.ListAsync(roleId.Value, ct));
            return BadRequest(new ProblemDetails { Title = "Commission scope is required.", Status = 400 });
        }
        catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Title = "Recharge commissions are not available.", Detail = ex.Message, Status = 409 }); }
    }

    [HttpPut]
    public async Task<ActionResult<IReadOnlyList<RechargeCommissionResponse>>> Save(SaveRechargeCommissionRequest request, CancellationToken ct)
    {
        try { return Ok(await service.SaveAsync(request, ct)); }
        catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Title = "Recharge commissions could not be saved.", Detail = ex.Message, Status = 409 }); }
    }
}
