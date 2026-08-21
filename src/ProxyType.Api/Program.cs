using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProxyType.Api.Data;
using ProxyType.Api.Domain;
using ProxyType.Api.Finance;
using ProxyType.Api.Security;
using ProxyType.Api.Services;

var builder = WebApplication.CreateBuilder(args);
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var signingKey = jwtSection["SigningKey"];
if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey must be provided through protected configuration and contain at least 32 bytes.");
}

builder.Services.Configure<JwtOptions>(jwtSection);
builder.Services.AddDbContext<ProxyTypeDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("ProxyType"), sql => sql.EnableRetryOnFailure()));
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ICurrentScopeService, CurrentScopeService>();
builder.Services.AddScoped<BootstrapAdminService>();
builder.Services.AddScoped<ServicePermissionService>();
builder.Services.AddScoped<FundRequestService>();
builder.Services.AddScoped<FinoDmtService>();
builder.Services.AddScoped<UpiTransferService>();
builder.Services.AddScoped<RechargeService>();
builder.Services.AddScoped<AepsService>();
builder.Services.AddScoped<WalletTransferService>();
var finoDmtMode = builder.Configuration["FinoDmt:ProviderMode"]
    ?? (builder.Environment.IsDevelopment() ? "MOCK" : "LIVE");
if (string.Equals(finoDmtMode, "LIVE", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IFinoDmtProvider, DisabledLiveFinoDmtProvider>();
}
else
{
    builder.Services.AddScoped<IFinoDmtProvider, MockFinoDmtProvider>();
}
var upiTransferMode = builder.Configuration["UpiTransfer:ProviderMode"] ?? "MOCK";
if (string.Equals(upiTransferMode, "LIVE", StringComparison.OrdinalIgnoreCase)) builder.Services.AddScoped<IUpiTransferProvider, DisabledLiveUpiTransferProvider>();
else builder.Services.AddScoped<IUpiTransferProvider, MockUpiTransferProvider>();
var rechargeMode = builder.Configuration["Recharge:ProviderMode"]
    ?? (builder.Environment.IsDevelopment() ? "MOCK" : "LIVE");
if (string.Equals(rechargeMode, "LIVE", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IRechargeProvider, DisabledLiveRechargeProvider>();
}
else
{
    builder.Services.AddScoped<IRechargeProvider, MockRechargeProvider>();
}
var aepsMode = builder.Configuration["Aeps:ProviderMode"] ?? (builder.Environment.IsDevelopment() ? "MOCK" : "LIVE");
if (string.Equals(aepsMode, "LIVE", StringComparison.OrdinalIgnoreCase)) builder.Services.AddScoped<IAepsProvider, DisabledLiveAepsProvider>();
else builder.Services.AddScoped<IAepsProvider, MockAepsProvider>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();
builder.Services.AddControllers();
builder.Services.AddHealthChecks();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    });
builder.Services.AddAuthorization();

var origins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("web", policy =>
    policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors("web");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<BootstrapAdminService>().EnsureAsync();
}

await app.RunAsync();

public partial class Program;
