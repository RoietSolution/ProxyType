using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ProxyType.Api.Contracts;
using ProxyType.Api.Data;
using ProxyType.Api.Domain;

namespace ProxyType.Api.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;
}

public interface ITokenService
{
    Task<TokenResponse> IssueAsync(AppUser user, Guid? familyId, string? ipAddress, string? userAgent, CancellationToken cancellationToken);
    string HashRefreshToken(string token);
}

public sealed class TokenService(
    IOptions<JwtOptions> options,
    ProxyTypeDbContext dbContext) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public async Task<TokenResponse> IssueAsync(
        AppUser user,
        Guid? familyId,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        var roles = await dbContext.UserRoles.AsNoTracking()
            .Where(userRole => userRole.UserId == user.UserId)
            .Select(userRole => userRole.Role.Code)
            .OrderBy(code => code)
            .ToListAsync(cancellationToken);
        var memberships = await dbContext.OrganizationMemberships.AsNoTracking()
            .Where(membership => membership.UserId == user.UserId && membership.IsActive)
            .Select(membership => new MembershipSummary(
                membership.OrganizationUnitId,
                membership.OrganizationUnit.Code,
                membership.OrganizationUnit.Name,
                membership.OrganizationUnit.UnitType,
                membership.IsPrimary))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var accessExpiry = now.AddMinutes(_options.AccessTokenMinutes);
        var refreshExpiry = now.AddDays(_options.RefreshTokenDays);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new("name", user.Username),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(role => new Claim("role", role)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: accessExpiry,
            signingCredentials: credentials);

        var refreshValue = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
        var refresh = new RefreshToken
        {
            RefreshTokenId = Guid.NewGuid(),
            UserId = user.UserId,
            TokenHash = HashRefreshToken(refreshValue),
            FamilyId = familyId ?? Guid.NewGuid(),
            CreatedAtUtc = now,
            ExpiresAtUtc = refreshExpiry,
            CreatedByIp = ipAddress,
            UserAgent = userAgent is null ? null : userAgent[..Math.Min(userAgent.Length, 500)]
        };
        dbContext.RefreshTokens.Add(refresh);
        await dbContext.SaveChangesAsync(cancellationToken);

        var summary = new UserSummary(
            user.UserId, user.Username, user.Email, user.DisplayName,
            user.MustChangePassword, roles, memberships);
        return new TokenResponse(
            new JwtSecurityTokenHandler().WriteToken(jwt),
            refreshValue,
            accessExpiry,
            refreshExpiry,
            summary);
    }

    public string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
