using LegacyVault.BLL.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace LegacyVault.API.Authentication;

public sealed class GoogleOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string FrontendCallbackUrl { get; set; } = "";
    public bool Configured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public void ValidateFrontendCallback(IEnumerable<string> origins)
    {
        if (string.IsNullOrEmpty(FrontendCallbackUrl)) return;
        if (!Uri.TryCreate(FrontendCallbackUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
            !origins.Contains(uri.GetLeftPart(UriPartial.Authority), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Google:FrontendCallbackUrl must be a fixed HTTPS URL on a configured Cors:Origins origin (HTTP permitted only for loopback), without query, fragment or user information.");
    }
}

public static class GoogleAuthentication
{
    public const string Scheme = "Google";
    public const string BrowserCookie = "LegacyVault.GoogleBrowser";
    public const string ChallengeCookie = "LegacyVault.GoogleChallenge";
    public const string ReturnCookie = "LegacyVault.GoogleReturn";
    public static CookieOptions PendingCookie => new()
    {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.None,
        Path = "/api/auth/google", MaxAge = TimeSpan.FromMinutes(10), IsEssential = true
    };

    public static IServiceCollection AddGoogleEmailAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Google").Get<GoogleOptions>() ?? new GoogleOptions();
        var allowedOrigins = configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
        settings.ValidateFrontendCallback(allowedOrigins);
        services.AddSingleton(settings);
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<GoogleLoginOtp>();
        services.AddSingleton<GoogleAuthorizationStore>();
        // Missing credentials must not prevent password login or Swagger startup.
        if (!settings.Configured) return services;
        services.AddAuthentication().AddOpenIdConnect(Scheme, options =>
        {
            options.SignInScheme = "GoogleTemporary";
            options.Authority = "https://accounts.google.com";
            options.ClientId = settings.ClientId;
            options.ClientSecret = settings.ClientSecret;
            options.CallbackPath = "/api/auth/google/oidc-callback";
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.ResponseMode = OpenIdConnectResponseMode.Query;
            options.UsePkce = true;
            options.MapInboundClaims = false;
            options.SaveTokens = false;
            options.CorrelationCookie.SameSite = SameSiteMode.None;
            options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
            options.CorrelationCookie.HttpOnly = true;
            options.CorrelationCookie.IsEssential = true;
            options.NonceCookie.SameSite = SameSiteMode.None;
            options.NonceCookie.SecurePolicy = CookieSecurePolicy.Always;
            options.NonceCookie.HttpOnly = true;
            options.NonceCookie.IsEssential = true;
            options.TokenValidationParameters.ValidIssuers = new[] { "https://accounts.google.com", "accounts.google.com" };
            options.TokenValidationParameters.ValidAlgorithms = new[] { "RS256" };
            options.Scope.Clear();
            foreach (var scope in new[] { "openid", "email", "profile" }) options.Scope.Add(scope);
            options.Events.OnRedirectToIdentityProvider = context =>
            {
                context.ProtocolMessage.Prompt = "select_account";
                if (!context.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
                    !context.Request.Host.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
                    context.ProtocolMessage.RedirectUri?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == true)
                {
                    context.ProtocolMessage.RedirectUri = "https://" + context.ProtocolMessage.RedirectUri["http://".Length..];
                }
                return Task.CompletedTask;
            };
            options.Events.OnTicketReceived = async context =>
            {
                // OIDC middleware has validated state, signature, issuer, audience, expiry and nonce.
                context.HandleResponse(); // Never sign the Google principal into the application cookie.
                string? browser = null;
                context.Properties?.Items.TryGetValue("browser", out browser);
                string? returnTo = null;
                context.Properties?.Items.TryGetValue("returnTo", out returnTo);
                var email = context.Principal?.FindFirst("email")?.Value;
                if (string.IsNullOrEmpty(browser) || context.Request.Cookies[BrowserCookie] != browser ||
                    string.IsNullOrWhiteSpace(email) || !bool.TryParse(context.Principal?.FindFirst("email_verified")?.Value, out var verified) || !verified)
                {
                    context.Response.Redirect("/api/auth/google/error");
                    return;
                }
                try
                {
                    var services = context.HttpContext.RequestServices;
                    var existing = await services.GetRequiredService<LegacyVault.BLL.Services.IdentityService>().ExistingGoogleLogin(email, context.HttpContext.RequestAborted);
                    string challengeId;
                    if (existing is not null)
                    {
                        challengeId = services.GetRequiredService<GoogleAuthorizationStore>()
                            .Start(browser, new(existing.UserId, email.Trim().ToLowerInvariant())).ChallengeId;
                    }
                    else
                    {
                        var challenge = await services.GetRequiredService<GoogleLoginOtp>().Request(browser, new GoogleEmailIdentity(email,
                            context.Principal?.FindFirst("name")?.Value ?? ""), context.HttpContext.RequestAborted);
                        challengeId = challenge.ChallengeId;
                    }
                    context.Response.Cookies.Append(ChallengeCookie, challengeId, PendingCookie);
                    if (!string.IsNullOrEmpty(returnTo))
                        context.Response.Cookies.Append(ReturnCookie, returnTo, PendingCookie);
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
                context.HttpContext.RequestServices.GetService<ILoggerFactory>()
                    ?.CreateLogger("GoogleAuth")
                    .LogWarning(context.Failure, "Google OIDC remote failure: {Message}", context.Failure?.Message);
                context.HandleResponse();
                context.Response.Redirect("/api/auth/google/error");
                return Task.CompletedTask;
            };
        });
        return services;
    }
}
