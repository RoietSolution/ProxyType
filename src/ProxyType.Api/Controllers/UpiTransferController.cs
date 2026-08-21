using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProxyType.Api.Contracts;
using ProxyType.Api.Finance;

namespace ProxyType.Api.Controllers;

[ApiController, Authorize, Route("api/services/upi-transfer")]
public sealed class UpiTransferController(UpiTransferService service) : ControllerBase
{
    [HttpPost("orders")]
    public async Task<ActionResult<UpiFundingResponse>> CreateOrder(UpiFundingRequest request, CancellationToken ct)
    {
        try { return Ok(await service.CreateOrderAsync(request, ct)); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new ProblemDetails { Title = "UPI Add Money is not available for this account.", Detail = ex.Message, Status = 403 }); }
        catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Title = "UPI Add Money is not ready.", Detail = ex.Message, Status = 409 }); }
    }

    [HttpGet("orders/{transactionId:guid}/status")]
    public async Task<ActionResult<UpiFundingResponse>> Status(Guid transactionId, CancellationToken ct)
    {
        try { return Ok(await service.CheckStatusAsync(transactionId, ct)); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new ProblemDetails { Title = "UPI Add Money is not available for this account.", Detail = ex.Message, Status = 403 }); }
        catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Title = "UPI Add Money status is not ready.", Detail = ex.Message, Status = 409 }); }
    }

    // MOCK-only callback seam. A live provider must replace the shared-secret check with signature verification.
    [AllowAnonymous, HttpPost("callbacks/mock")]
    public async Task<ActionResult<UpiFundingResponse>> MockCallback(UpiMockCallbackRequest request, CancellationToken ct)
    {
        try { return Ok(await service.HandleMockCallbackAsync(request, Request.Headers["X-Mock-Callback-Key"].FirstOrDefault(), ct)); }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new ProblemDetails { Title = "Callback authentication failed.", Detail = ex.Message, Status = 401 }); }
        catch (KeyNotFoundException) { return NotFound(); }
    }
}
