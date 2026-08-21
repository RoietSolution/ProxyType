using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProxyType.Api.Contracts;
using ProxyType.Api.Finance;

namespace ProxyType.Api.Controllers;

[ApiController, Authorize, Route("api/services/aeps")]
public sealed class AepsController(AepsService service) : ControllerBase
{
    [HttpGet("banks")] public async Task<ActionResult<IReadOnlyList<AepsBankResponse>>> Banks(CancellationToken ct) => Ok(await service.BanksAsync(ct));
    [HttpPost("transactions")]
    public async Task<ActionResult<AepsResponse>> Execute(AepsRequest request, CancellationToken ct)
    {
        try { return Ok(await service.ExecuteAsync(request, ct)); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new ProblemDetails { Title = "AEPS is not available for this account.", Detail = ex.Message, Status = 403 }); }
        catch (ArgumentException ex) { return UnprocessableEntity(new ProblemDetails { Title = "Invalid AEPS request.", Detail = ex.Message, Status = 422 }); }
        catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Title = "AEPS is not ready.", Detail = ex.Message, Status = 409 }); }
    }
}
