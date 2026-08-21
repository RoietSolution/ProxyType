using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProxyType.Api.Data;
using ProxyType.Api.Domain;

namespace ProxyType.Api.Services;

public sealed class BootstrapAdminService(
    ProxyTypeDbContext dbContext,
    IPasswordHasher<AppUser> passwordHasher,
    IConfiguration configuration,
    ILogger<BootstrapAdminService> logger)
{
    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        if (await dbContext.UserRoles.AnyAsync(
            userRole => userRole.Role.Code == "PLATFORM_ADMIN", cancellationToken))
        {
            return;
        }

        // Read through IConfiguration so the same secret key works with ASP.NET Core
        // User Secrets in Development and environment variables in Production.
        var password = configuration["PROXYTYPE_BOOTSTRAP_ADMIN_PASSWORD"];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "No platform administrator exists. Set PROXYTYPE_BOOTSTRAP_ADMIN_PASSWORD once to provision the bootstrap account.");
            return;
        }

        if (password.Length < 14)
        {
            throw new InvalidOperationException("Bootstrap administrator password must contain at least 14 characters.");
        }

        var role = await dbContext.Roles.SingleAsync(role => role.Code == "PLATFORM_ADMIN", cancellationToken);
        var platform = await dbContext.OrganizationUnits.SingleAsync(unit => unit.Code == "PLATFORM", cancellationToken);
        var username = configuration["BootstrapAdmin:Username"] ?? "platform.admin";
        var email = configuration["BootstrapAdmin:Email"] ?? "admin@proxytype.local";
        var user = new AppUser
        {
            UserId = Guid.NewGuid(),
            Username = username,
            Email = email,
            DisplayName = configuration["BootstrapAdmin:DisplayName"] ?? "Platform Administrator",
            SecurityStamp = Guid.NewGuid(),
            IsActive = true,
            MustChangePassword = true,
            PasswordChangedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        dbContext.Users.Add(user);
        dbContext.UserRoles.Add(new UserRole { User = user, RoleId = role.RoleId, AssignedAtUtc = DateTime.UtcNow });
        dbContext.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationMembershipId = Guid.NewGuid(),
            User = user,
            OrganizationUnitId = platform.OrganizationUnitId,
            IsPrimary = true,
            IsActive = true,
            ValidFromUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Bootstrap platform administrator {Username} was provisioned and must change password.", username);
    }
}
