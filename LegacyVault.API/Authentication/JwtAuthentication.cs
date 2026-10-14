using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Security;
using LegacyVault.BLL.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace LegacyVault.API.Authentication;

public sealed class JwtSettings
{
    public string Issuer { get; set; } = "LegacyVault";
    public string Audience { get; set; } = "DigitalVaultFrontend";
    public string SigningKey { get; set; } = "";
    public int LifetimeMinutes { get; set; } = 15;
    public SymmetricSecurityKey Key()
    {
        if (Encoding.UTF8.GetByteCount(SigningKey) < 32 || string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience) || LifetimeMinutes is < 1 or > 30)
            throw new InvalidOperationException("Configure Jwt issuer, audience, signing key (at least 32 random bytes) and lifetime (1-30 minutes).");
        return new(Encoding.UTF8.GetBytes(SigningKey));
    }
}
public sealed class JwtTokenIssuer(JwtSettings settings) : IAccessTokenIssuer
{
    public TokenDto Issue(LoginDto login)
    {
        var id = Guid.NewGuid().ToString("N");
        var claims = new List<Claim> { new("sub", login.UserId.ToString()), new("jti", id), new("vault_session", id) };
        claims.AddRange(login.Roles.Select(x => new Claim("role", x)));
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience, claims, now, now.AddMinutes(settings.LifetimeMinutes), new SigningCredentials(settings.Key(), SecurityAlgorithms.HmacSha256));
        return new(login.UserId, login.Roles, new JwtSecurityTokenHandler().WriteToken(token), "Bearer", settings.LifetimeMinutes * 60);
    }
}
public static class JwtAuthentication
{
    public static IServiceCollection AddVaultJwt(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Jwt").Get<JwtSettings>() ?? new();
        var key = settings.Key();
        services.AddSingleton(settings);
        services.AddSingleton<IAccessTokenIssuer, JwtTokenIssuer>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<LoginFactorStore>();
        services.AddScoped<PasswordAuthenticationService>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new()
            {
                ValidateIssuer = true, ValidIssuer = settings.Issuer, ValidateAudience = true, ValidAudience = settings.Audience,
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                ValidateIssuerSigningKey = true, IssuerSigningKey = key, ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                ClockSkew = TimeSpan.FromSeconds(15), RoleClaimType = "role", NameClaimType = "sub"
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    if (!int.TryParse(context.Principal?.FindFirst("sub")?.Value, out var id) ||
                        string.IsNullOrEmpty(context.Principal.FindFirst("vault_session")?.Value))
                    { context.Fail("Invalid identity."); return; }
                    try
                    {
                        var me = await context.HttpContext.RequestServices.GetRequiredService<IdentityService>().Me(id, context.HttpContext.RequestAborted);
                        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim("sub", id.ToString()),
                            new Claim("vault_session", context.Principal.FindFirst("vault_session")!.Value) }.Concat(me.Roles.Select(x => new Claim("role", x)));
                        context.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme, "sub", "role"));
                    }
                    catch (WorkflowException) { context.Fail("Account unavailable."); }
                }
            };
        });
        return services;
    }
}
