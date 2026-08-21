using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProxyType.Api.Contracts;
using ProxyType.Api.Data;
using ProxyType.Api.Domain;

namespace ProxyType.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/services")]
public sealed class ServicesController(ProxyTypeDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) => Ok(
        await dbContext.Services.AsNoTracking()
            .OrderBy(service => service.Category.SortOrder).ThenBy(service => service.SortOrder)
            .Select(service => new
            {
                service.ServiceId,
                service.Code,
                service.Name,
                service.Description,
                Category = service.Category.Name,
                service.IsActive,
                service.IsChargeable,
                service.IsCommissionable,
                service.RequiresKyc,
                service.SortOrder
            }).ToListAsync(cancellationToken));

    [HttpPost]
    [Authorize(Roles = "PLATFORM_ADMIN")]
    public async Task<IActionResult> Create(CreateServiceRequest request, CancellationToken cancellationToken)
    {
        var categoryExists = await dbContext.ServiceCategories.AnyAsync(
            category => category.ServiceCategoryId == request.ServiceCategoryId, cancellationToken);
        if (!categoryExists)
        {
            return BadRequest(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["serviceCategoryId"] = ["Category does not exist."] }));
        }

        var code = request.Code.Trim().ToLowerInvariant();
        if (await dbContext.Services.AnyAsync(service => service.Code == code, cancellationToken))
        {
            return Conflict(new { message = "Service code already exists." });
        }

        var service = new FinancialService
        {
            ServiceId = Guid.NewGuid(),
            ServiceCategoryId = request.ServiceCategoryId,
            Code = code,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsActive = request.IsActive,
            IsChargeable = request.IsChargeable,
            IsCommissionable = request.IsCommissionable,
            RequiresKyc = request.RequiresKyc,
            SortOrder = request.SortOrder,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        dbContext.Services.Add(service);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Created($"/api/services/{service.ServiceId}", new { service.ServiceId, service.Code, service.Name });
    }

    [HttpPut("{serviceId:guid}")]
    [Authorize(Roles = "PLATFORM_ADMIN")]
    public async Task<IActionResult> Update(Guid serviceId, UpdateServiceRequest request, CancellationToken cancellationToken)
    {
        var service = await dbContext.Services.SingleOrDefaultAsync(candidate => candidate.ServiceId == serviceId, cancellationToken);
        if (service is null)
        {
            return NotFound();
        }

        service.Name = request.Name.Trim();
        service.Description = request.Description?.Trim();
        service.IsActive = request.IsActive;
        service.IsChargeable = request.IsChargeable;
        service.IsCommissionable = request.IsCommissionable;
        service.RequiresKyc = request.RequiresKyc;
        service.SortOrder = request.SortOrder;
        service.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
