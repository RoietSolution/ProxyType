using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProxyType.Api.Contracts;
using ProxyType.Api.Data;
using ProxyType.Api.Domain;
using ProxyType.Api.Security;

namespace ProxyType.Api.Controllers;

[ApiController, Authorize, Route("api/user-documents")]
public sealed class UserDocumentsController(
    ProxyTypeDbContext dbContext,
    ICurrentScopeService scopeService) : ControllerBase
{
    [HttpGet("owners")]
    public async Task<ActionResult<UserDocumentOwner[]>> Owners(
        [FromQuery] string unitType = "CSP", [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        unitType = unitType.Trim().ToUpperInvariant();
        if (!new[] { "CMF", "CSF", "CSP" }.Contains(unitType)) return BadRequest("unitType must be CMF, CSF, or CSP.");
        var accessible = await scopeService.GetAccessibleOrganizationIdsAsync(cancellationToken);
        var query = dbContext.OrganizationMemberships.AsNoTracking()
            .Where(membership => membership.IsActive && membership.OrganizationUnit.UnitType == unitType &&
                accessible.Contains(membership.OrganizationUnitId));
        if (!HasManagementRole()) query = query.Where(membership => membership.UserId == scopeService.UserId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim();
            query = query.Where(membership => membership.User.Username.Contains(value) ||
                membership.User.DisplayName.Contains(value) || membership.User.Email.Contains(value) ||
                (membership.User.Mobile != null && membership.User.Mobile.Contains(value)) ||
                membership.OrganizationUnit.Code.Contains(value) || membership.OrganizationUnit.Name.Contains(value));
        }
        var owners = await query.OrderBy(membership => membership.User.DisplayName).Take(250)
            .Select(membership => new UserDocumentOwner(membership.UserId, membership.User.Username,
                membership.User.DisplayName, membership.User.Email, membership.User.Mobile,
                membership.OrganizationUnit.UnitType, membership.OrganizationUnit.Code))
            .ToArrayAsync(cancellationToken);
        return Ok(owners);
    }

    [HttpGet("users/{userId:guid}")]
    public async Task<ActionResult<UserDocumentItem[]>> List(Guid userId, CancellationToken cancellationToken)
    {
        if (!await CanAccessUserAsync(userId, cancellationToken)) return Forbid();
        var documents = await dbContext.UserDocuments.AsNoTracking()
            .Where(document => document.UserId == userId)
            .OrderByDescending(document => document.CreatedAtUtc)
            .Select(document => new UserDocumentItem(document.UserDocumentId, document.DocumentType,
                document.FileName, document.FileSize,
                dbContext.Users.Where(user => user.UserId == document.UploadedByUserId).Select(user => user.DisplayName).FirstOrDefault() ?? "Unknown",
                document.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);
        return Ok(documents);
    }

    [HttpPost("users/{userId:guid}")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<ActionResult> Upload(Guid userId, [FromForm] UploadUserDocumentRequest request, CancellationToken cancellationToken)
    {
        if (!await CanAccessUserAsync(userId, cancellationToken)) return Forbid();
        var document = request.Document!;
        if (!IsValidDocument(document)) return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            [nameof(request.Document)] = ["Upload a JPG, JPEG, or PDF file no larger than 1 MB. Double extensions are not allowed."]
        }));
        var fileName = Path.GetFileName(document.FileName);
        if (await dbContext.UserDocuments.AnyAsync(item => item.UserId == userId && item.FileName == fileName, cancellationToken))
            return Conflict(new ProblemDetails { Title = "Duplicate document", Detail = "A document with this filename already exists for the selected user.", Status = 409 });
        await using var stream = new MemoryStream(); await document.CopyToAsync(stream, cancellationToken);
        var item = new UserDocument
        {
            UserDocumentId = Guid.NewGuid(), UserId = userId, DocumentType = request.DocumentType,
            FileName = fileName, ContentType = document.ContentType.ToLowerInvariant(), FileSize = document.Length,
            Content = stream.ToArray(), UploadedByUserId = scopeService.UserId, CreatedAtUtc = DateTime.UtcNow
        };
        dbContext.UserDocuments.Add(item);
        dbContext.AuditLogs.Add(new AuditLog
        {
            ActorUserId = scopeService.UserId, Action = "USER_DOCUMENT_UPLOADED", EntityType = "UserDocument",
            EntityId = item.UserDocumentId.ToString(), CorrelationId = Guid.NewGuid(),
            DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { item.UserId, item.DocumentType, item.FileName, item.FileSize }),
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(), OccurredAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return Created($"/api/user-documents/users/{userId}/{item.UserDocumentId}", new { item.UserDocumentId });
    }

    [HttpGet("users/{userId:guid}/{documentId:guid}/download")]
    public async Task<IActionResult> Download(Guid userId, Guid documentId, CancellationToken cancellationToken)
    {
        if (!await CanAccessUserAsync(userId, cancellationToken)) return Forbid();
        var document = await dbContext.UserDocuments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.UserId == userId && item.UserDocumentId == documentId, cancellationToken);
        if (document is null) return NotFound();
        return File(document.Content, document.ContentType, document.FileName);
    }

    private bool HasManagementRole() => scopeService.HasRole("PLATFORM_ADMIN", "CMF_ADMIN", "CSF_ADMIN");
    private async Task<bool> CanAccessUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == scopeService.UserId) return true;
        if (!HasManagementRole()) return false;
        var accessible = await scopeService.GetAccessibleOrganizationIdsAsync(cancellationToken);
        return await dbContext.OrganizationMemberships.AsNoTracking().AnyAsync(membership =>
            membership.UserId == userId && membership.IsActive && accessible.Contains(membership.OrganizationUnitId), cancellationToken);
    }
    private static bool IsValidDocument(IFormFile document)
    {
        var fileName = Path.GetFileName(document.FileName);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        if (document.Length is <= 0 or > 1024 * 1024 || baseName.Contains('.') ||
            !new[] { ".jpg", ".jpeg", ".pdf" }.Contains(extension) ||
            !new[] { "image/jpeg", "application/pdf" }.Contains(document.ContentType.ToLowerInvariant())) return false;
        Span<byte> signature = stackalloc byte[4];
        using var stream = document.OpenReadStream();
        if (stream.Read(signature) < 4) return false;
        var isPdf = signature.SequenceEqual(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        var isJpeg = signature[0] == 0xff && signature[1] == 0xd8 && signature[2] == 0xff;
        return (extension == ".pdf" && isPdf) ||
            (new[] { ".jpg", ".jpeg" }.Contains(extension) && isJpeg);
    }
}
