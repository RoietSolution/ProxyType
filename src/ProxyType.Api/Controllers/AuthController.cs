using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ProxyType.Api.Contracts;
using ProxyType.Api.Data;
using ProxyType.Api.Domain;
using ProxyType.Api.Security;

namespace ProxyType.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    ProxyTypeDbContext dbContext,
    IPasswordHasher<AppUser> passwordHasher,
    ITokenService tokenService,
    IConfiguration configuration) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var attempted = request.Username.Trim();
        var normalized = attempted.ToUpperInvariant();
        var user = await dbContext.Users
            .SingleOrDefaultAsync(candidate =>
                candidate.Username.ToUpper() == normalized || candidate.Email.ToUpper() == normalized,
                cancellationToken);
        var now = DateTime.UtcNow;
        var failureCode = "INVALID_CREDENTIALS";

        if (user is not null && !user.IsActive)
        {
            failureCode = "ACCOUNT_DISABLED";
        }
        else if (user?.LockoutEndUtc > now)
        {
            failureCode = "ACCOUNT_LOCKED";
        }
        else if (user is not null &&
                 passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) != PasswordVerificationResult.Failed)
        {
            user.AccessFailedCount = 0;
            user.LockoutEndUtc = null;
            user.LastLoginUtc = now;
            user.UpdatedAtUtc = now;
            dbContext.LoginAudits.Add(CreateLoginAudit(user.UserId, attempted, true, null));
            await dbContext.SaveChangesAsync(cancellationToken);
            return Ok(await tokenService.IssueAsync(
                user, null, HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(), cancellationToken));
        }
        else if (user is not null)
        {
            user.AccessFailedCount++;
            var maxAttempts = configuration.GetValue("Authentication:MaxFailedAttempts", 5);
            if (user.AccessFailedCount >= maxAttempts)
            {
                user.LockoutEndUtc = now.AddMinutes(configuration.GetValue("Authentication:LockoutMinutes", 15));
                failureCode = "ACCOUNT_LOCKED";
            }
        }

        dbContext.LoginAudits.Add(CreateLoginAudit(user?.UserId, attempted, false, failureCode));
        await dbContext.SaveChangesAsync(cancellationToken);
        return Unauthorized(new ProblemDetails
        {
            Title = "Authentication failed",
            Detail = "The username or password is invalid, or the account is unavailable.",
            Status = StatusCodes.Status401Unauthorized
        });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<TokenResponse>> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        var hash = tokenService.HashRefreshToken(request.RefreshToken);
        var stored = await dbContext.RefreshTokens.Include(token => token.User)
            .SingleOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);
        var now = DateTime.UtcNow;
        if (stored is null || stored.ExpiresAtUtc <= now || !stored.User.IsActive)
        {
            return Unauthorized();
        }

        if (stored.UsedAtUtc is not null || stored.RevokedAtUtc is not null)
        {
            await dbContext.RefreshTokens
                .Where(token => token.FamilyId == stored.FamilyId && token.RevokedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAtUtc, now), cancellationToken);
            return Unauthorized();
        }

        stored.UsedAtUtc = now;
        var response = await tokenService.IssueAsync(
            stored.User, stored.FamilyId, HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(), cancellationToken);
        var replacementHash = tokenService.HashRefreshToken(response.RefreshToken);
        stored.ReplacedByTokenId = await dbContext.RefreshTokens
            .Where(token => token.TokenHash == replacementHash)
            .Select(token => (Guid?)token.RefreshTokenId)
            .SingleAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(response);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken)
    {
        var hash = tokenService.HashRefreshToken(request.RefreshToken);
        await dbContext.RefreshTokens.Where(token => token.TokenHash == hash && token.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAtUtc, DateTime.UtcNow), cancellationToken);
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserSummary>> Me(CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirst("sub")!.Value);
        var user = await dbContext.Users.AsNoTracking().SingleAsync(candidate => candidate.UserId == userId, cancellationToken);
        var roles = await dbContext.UserRoles.AsNoTracking().Where(userRole => userRole.UserId == userId)
            .Select(userRole => userRole.Role.Code).ToListAsync(cancellationToken);
        var memberships = await dbContext.OrganizationMemberships.AsNoTracking()
            .Where(membership => membership.UserId == userId && membership.IsActive)
            .Select(membership => new MembershipSummary(
                membership.OrganizationUnitId,
                membership.OrganizationUnit.Code,
                membership.OrganizationUnit.Name,
                membership.OrganizationUnit.UnitType,
                membership.IsPrimary))
            .ToListAsync(cancellationToken);
        return Ok(new UserSummary(user.UserId, user.Username, user.Email, user.DisplayName,
            user.MustChangePassword, roles, memberships));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.NewPassword, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(request.ConfirmPassword)] = ["Password confirmation does not match."]
            }));
        }

        var userId = Guid.Parse(User.FindFirst("sub")!.Value);
        var user = await dbContext.Users.SingleAsync(candidate => candidate.UserId == userId, cancellationToken);
        if (passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                [nameof(request.CurrentPassword)] = ["Current password is incorrect."]
            }));
        }

        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.SecurityStamp = Guid.NewGuid();
        user.PasswordChangedAtUtc = DateTime.UtcNow;
        user.MustChangePassword = false;
        user.UpdatedAtUtc = DateTime.UtcNow;
        await dbContext.RefreshTokens.Where(token => token.UserId == userId && token.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAtUtc, DateTime.UtcNow), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private LoginAudit CreateLoginAudit(Guid? userId, string attempted, bool succeeded, string? failureCode) => new()
    {
        UserId = userId,
        UsernameAttempted = attempted[..Math.Min(attempted.Length, 100)],
        Succeeded = succeeded,
        FailureCode = failureCode,
        IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
        UserAgent = Request.Headers.UserAgent.ToString()[..Math.Min(Request.Headers.UserAgent.ToString().Length, 500)],
        OccurredAtUtc = DateTime.UtcNow
    };
}
