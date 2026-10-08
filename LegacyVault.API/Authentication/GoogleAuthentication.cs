using LegacyVault.BLL.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace LegacyVault.API.Authentication;

public sealed class GoogleOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public bool Configured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

public static class GoogleAuthentication
{
    public const string Scheme = "Google";
    public const string BrowserCookie = "LegacyVault.GoogleBrowser";
    public const string ChallengeCookie = "LegacyVault.GoogleChallenge";
    public static CookieOptions PendingCookie => new()
    {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax,
        Path = "/api/auth/google", MaxAge = TimeSpan.FromMinutes(10), IsEssential = true
    };

    public static IServiceCollection AddGoogleEmailAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Google").Get<GoogleOptions>() ?? new GoogleOptions();
        services.AddSingleton(settings);
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<GoogleLoginOtp>();
        // Missing credentials must not prevent password login or Swagger startup.
        if (!settings.Configured) return services;
        services.AddAuthentication().AddOpenIdConnect(Scheme, options =>
        {
            options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.Authority = "https://accounts.google.com";
            options.ClientId = settings.ClientId;
            options.ClientSecret = settings.ClientSecret;
            options.CallbackPath = "/api/auth/google/oidc-callback";
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.ResponseMode = OpenIdConnectResponseMode.Query;
            options.UsePkce = true;
            options.MapInboundClaims = false;
            options.SaveTokens = false;
            options.TokenValidationParameters.ValidIssuers = new[] { "https://accounts.google.com", "accounts.google.com" };
            options.TokenValidationParameters.ValidAlgorithms = new[] { "RS256" };
            options.Scope.Clear();
            foreach (var scope in new[] { "openid", "email", "profile" }) options.Scope.Add(scope);
            options.Events.OnRedirectToIdentityProvider = context =>
            {
                context.ProtocolMessage.Prompt = "select_account";
                return Task.CompletedTask;
            };
            options.Events.OnTicketReceived = async context =>
            {
                // OIDC middleware has validated state, signature, issuer, audience, expiry and nonce.
                context.HandleResponse(); // Never sign the Google principal into the application cookie.
                string? browser = null;
                context.Properties?.Items.TryGetValue("browser", out browser);
                var email = context.Principal?.FindFirst("email")?.Value;
                if (string.IsNullOrEmpty(browser) || context.Request.Cookies[BrowserCookie] != browser ||
                    string.IsNullOrWhiteSpace(email) || !bool.TryParse(context.Principal?.FindFirst("email_verified")?.Value, out var verified) || !verified)
                {
                    context.Response.Redirect("/api/auth/google/error");
                    return;
                }
                try
                {
                    var otp = context.HttpContext.RequestServices.GetRequiredService<GoogleLoginOtp>();
                    var challenge = await otp.Request(browser, new GoogleEmailIdentity(email,
                        context.Principal?.FindFirst("name")?.Value ?? ""), context.HttpContext.RequestAborted);
                    context.Response.Cookies.Append(ChallengeCookie, challenge.ChallengeId, PendingCookie);
                    context.Response.Redirect("/api/auth/google/callback");
                }
                catch (LegacyVault.BLL.DTOs.WorkflowException ex)
                {
                    context.Response.StatusCode = ex.StatusCode;
                    await context.Response.WriteAsJsonAsync(new { status = ex.StatusCode, detail = ex.Message });
                }
            };
            options.Events.OnRemoteFailure = context =>
            {
                context.HandleResponse();
                context.Response.Redirect("/api/auth/google/error");
                return Task.CompletedTask;
            };
        });
        return services;
    }
}
