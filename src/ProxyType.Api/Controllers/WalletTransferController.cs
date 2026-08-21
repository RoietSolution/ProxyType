using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProxyType.Api.Contracts;
using ProxyType.Api.Finance;

namespace ProxyType.Api.Controllers;

[ApiController, Authorize, Route("api/services/wallet-to-wallet")]
public sealed class WalletTransferController(WalletTransferService service) : ControllerBase
{
    [HttpGet("receivers")]
    public async Task<ActionResult<WalletTransferReceiverResponse>> Receiver([FromQuery] string mobile, CancellationToken cancellationToken) =>
        await service.FindReceiverAsync(mobile.Trim(), cancellationToken) is { } receiver ? Ok(receiver) : NotFound(new ProblemDetails { Title = "Receiver not found or outside your permitted hierarchy.", Status = 404 });

    [HttpPost("transfers")]
    public async Task<ActionResult<WalletTransferResponse>> Transfer(WalletTransferRequest request, CancellationToken cancellationToken)
    {
        try { return Ok(await service.TransferAsync(request, cancellationToken)); }
        catch (UnauthorizedAccessException e) { return StatusCode(403, new ProblemDetails { Title = "Wallet transfer is not available.", Detail = e.Message, Status = 403 }); }
        catch (KeyNotFoundException e) { return NotFound(new ProblemDetails { Title = "Receiver unavailable.", Detail = e.Message, Status = 404 }); }
        catch (InvalidOperationException e) { return Conflict(new ProblemDetails { Title = "Wallet transfer rejected.", Detail = e.Message, Status = 409 }); }
    }
}
