using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProxyType.Api.Contracts;
using ProxyType.Api.Finance;

namespace ProxyType.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/services/fino-dmt")]
public sealed class FinoDmtController(FinoDmtService service) : ControllerBase
{
    [HttpPost("transfers")]
    public async Task<ActionResult<FinoDmtResponse>> Transfer(
        FinoDmtRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.TransferAsync(request, cancellationToken));
        }
        catch (UnauthorizedAccessException exception)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Title = "Fino DMT is not available for this account.",
                Detail = exception.Message,
                Status = StatusCodes.Status403Forbidden
            });
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Fino DMT is not ready.",
                Detail = exception.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
    }
}
