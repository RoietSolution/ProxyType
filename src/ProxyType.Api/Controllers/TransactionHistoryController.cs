using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProxyType.Api.Contracts;
using ProxyType.Api.Data;
using ProxyType.Api.Security;

namespace ProxyType.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/transactions")]
public sealed class TransactionHistoryController(
    ProxyTypeDbContext dbContext,
    ICurrentScopeService scopeService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ServiceTransactionHistoryItem>>> Get(
        [FromQuery] string? service,
        [FromQuery] string? status,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var accessible = await scopeService.GetAccessibleOrganizationIdsAsync(cancellationToken);
        page = Math.Clamp(page, 1, 10000);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = dbContext.ServiceTransactions.AsNoTracking()
            .Join(dbContext.Services.AsNoTracking(), transaction => transaction.ServiceId, item => item.ServiceId,
                (transaction, item) => new { transaction, service = item })
            .Where(row => accessible.Contains(row.transaction.OrganizationUnitId));

        if (!string.IsNullOrWhiteSpace(service)) query = query.Where(row => row.service.Code == service);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(row => row.transaction.Status == status.ToUpper());
        if (fromUtc.HasValue) query = query.Where(row => row.transaction.CreatedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(row => row.transaction.CreatedAtUtc < toUtc.Value);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(row => row.transaction.TransactionReference.Contains(search) ||
                (row.transaction.ProviderReference != null && row.transaction.ProviderReference.Contains(search)));

        var rows = await query.OrderByDescending(row => row.transaction.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(row => new ServiceTransactionHistoryItem(
                row.transaction.ServiceTransactionId,
                row.transaction.TransactionReference,
                row.service.Name,
                dbContext.Providers.Where(provider => provider.ProviderId == row.transaction.ProviderId).Select(provider => provider.Name).FirstOrDefault(),
                row.transaction.Amount,
                row.transaction.ChargeAmount,
                row.transaction.CommissionAmount,
                row.transaction.Status,
                row.transaction.ProviderReference,
                row.transaction.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        return Ok(rows);
    }
}
