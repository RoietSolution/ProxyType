using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ProxyType.Api.Contracts;
using ProxyType.Api.Finance;

namespace ProxyType.Api.Controllers;

[ApiController, Authorize(Roles = "PLATFORM_ADMIN"), Route("api/charge-slabs")]
public sealed class ChargeSlabsController(ChargeSlabService service) : ControllerBase
{
    [HttpGet("metadata")]
    public async Task<ActionResult<PricingMetadataResponse>> Metadata(CancellationToken ct) => Ok(await service.MetadataAsync(ct));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ChargeSlabResponse>>> List(Guid serviceId, Guid roleId, CancellationToken ct) =>
        Ok(await service.ListAsync(serviceId, roleId, ct));

    [HttpPost]
    public async Task<ActionResult<ChargeSlabResponse>> Save(SaveChargeSlabRequest request, CancellationToken ct)
    {
        try { return Ok(await service.SaveAsync(request, ct)); }
        catch (KeyNotFoundException ex) { return NotFound(new ProblemDetails { Title = "Charge slab was not found.", Detail = ex.Message, Status = 404 }); }
        catch (InvalidOperationException ex) { return Conflict(new ProblemDetails { Title = "Charge slab could not be saved.", Detail = ex.Message, Status = 409 }); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try { await service.DeleteAsync(id, ct); return NoContent(); }
        catch (KeyNotFoundException ex) { return NotFound(new ProblemDetails { Title = "Charge slab was not found.", Detail = ex.Message, Status = 404 }); }
    }
}
