using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProxyType.Api.Contracts;
using ProxyType.Api.Finance;

namespace ProxyType.Api.Controllers;

[ApiController, Authorize, Route("api/services/fund-request")]
public sealed class FundRequestController(FundRequestService service) : ControllerBase
{
    [HttpGet("instructions")]
    public async Task<ActionResult<FundRequestInstructionsResponse>> Instructions(CancellationToken ct) =>
        await ExecuteAsync(() => service.InstructionsAsync(ct));

    [HttpGet("requests")]
    public async Task<ActionResult<IReadOnlyList<FundRequestResponse>>> List(CancellationToken ct) =>
        await ExecuteAsync(() => service.ListAsync(ct));

    [HttpPost("requests")]
    [RequestSizeLimit(5_500_000)]
    public async Task<ActionResult<FundRequestResponse>> Create([FromForm] FundRequestCreateRequest request, CancellationToken ct) =>
        await ExecuteAsync(() => service.CreateAsync(request, ct));

    [HttpGet("requests/{fundRequestId:guid}")]
    public async Task<ActionResult<FundRequestResponse>> Get(Guid fundRequestId, CancellationToken ct) =>
        await ExecuteAsync(() => service.GetAsync(fundRequestId, ct));

    [HttpPost("requests/{fundRequestId:guid}/review")]
    public async Task<ActionResult<FundRequestResponse>> Review(Guid fundRequestId, FundRequestReviewRequest request, CancellationToken ct) =>
        await ExecuteAsync(() => service.ReviewAsync(fundRequestId, request, ct));

    [HttpGet("requests/{fundRequestId:guid}/proof")]
    public async Task<IActionResult> Proof(Guid fundRequestId, CancellationToken ct)
    {
        try
        {
            var proof = await service.GetProofAsync(fundRequestId, ct);
            return File(proof.Content, proof.ContentType, proof.FileName, enableRangeProcessing: false);
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new ProblemDetails { Title = "Fund Request proof is not available.", Detail = ex.Message, Status = 403 }); }
        catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Title = "Fund Request is not ready.", Detail = ex.Message, Status = 409 }); }
    }

    private static async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try { return new ActionResult<T>(await action()); }
        catch (KeyNotFoundException ex) { return new NotFoundObjectResult(new ProblemDetails { Title = "Fund Request was not found.", Detail = ex.Message, Status = 404 }); }
        catch (UnauthorizedAccessException ex) { return new ObjectResult(new ProblemDetails { Title = "Fund Request is not available for this account.", Detail = ex.Message, Status = 403 }) { StatusCode = 403 }; }
        catch (InvalidOperationException ex) { return new ConflictObjectResult(new ProblemDetails { Title = "Fund Request was rejected.", Detail = ex.Message, Status = 409 }); }
    }
}
