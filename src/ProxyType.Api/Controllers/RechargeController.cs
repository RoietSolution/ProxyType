using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProxyType.Api.Contracts;
using ProxyType.Api.Finance;

namespace ProxyType.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/services/recharge")]
public sealed class RechargeController(RechargeService service) : ControllerBase
{
    [HttpGet("operators")]
    public async Task<ActionResult<IReadOnlyList<RechargeOperatorResponse>>> Operators(
        [FromQuery] string type = "MOBILE",
        CancellationToken cancellationToken = default) =>
        Ok(await service.OperatorsAsync(type.Trim().ToUpperInvariant(), cancellationToken));

    [HttpPost("transactions")]
    public async Task<ActionResult<RechargeResponse>> Recharge(
        RechargeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.RechargeAsync(request, cancellationToken));
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Recharge is not available for this account.", Detail = exception.Message, Status = 403
            });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails { Title = "Recharge is not ready.", Detail = exception.Message, Status = 409 });
        }
    }
}
