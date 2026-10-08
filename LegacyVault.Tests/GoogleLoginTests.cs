using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using LegacyVault.API.Authentication;
using LegacyVault.API.Controllers;
using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Security;
using LegacyVault.BLL.Services;
using LegacyVault.DAL.Context;
using LegacyVault.DAL.Repositories;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

internal static class GoogleLoginTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var clock = new TestClock();
        var sender = new FakeSender();
        var otp = new GoogleLoginOtp(sender, clock);
        var challenge = await otp.Request("browser-a", new(" Google@Example.test ", "Google User"), default);
        async Task Rejected(Func<Task> action, int status, string name)
        {
            try { await action(); throw new Exception("Expected rejection: " + name); }
            catch (WorkflowException ex) { check(ex.StatusCode == status, name); }
        }
        await Rejected(() => otp.Request("browser-b", new("google@example.test", "User"), default), 429, "Google OTP cooldown is per normalized email across browsers");
        await Rejected(() => otp.Verify("browser-b", challenge.ChallengeId, sender.Code, default), 400, "Google OTP cannot be used by another browser");
        clock.Advance(TimeSpan.FromSeconds(61));
        var renewed = await otp.Resend("browser-a", challenge.ChallengeId, default);
        await Rejected(() => otp.Verify("browser-a", challenge.ChallengeId, sender.Code, default), 400, "resending Google OTP invalidates the old challenge");
        var verified = await otp.Verify("browser-a", renewed.ChallengeId, sender.Code, default);
        check(verified.Email == "google@example.test", "Google OTP returns only the server-held verified identity");
        await Rejected(() => otp.Verify("browser-a", renewed.ChallengeId, sender.Code, default), 400, "Google OTP cannot be replayed");
        var attempts = await otp.Request("browser-c", new("attempts@example.test", "User"), default);
        var correct = sender.Code;
        var wrong = correct == "000000" ? "000001" : "000000";
        for (var i = 0; i < 5; i++)
            await Rejected(() => otp.Verify("browser-c", attempts.ChallengeId, wrong, default), 400, "Google OTP rejects wrong attempt " + (i + 1));
        await Rejected(() => otp.Verify("browser-c", attempts.ChallengeId, correct, default), 400, "Google OTP attempt limit blocks even a correct code");
        var expired = await otp.Request("browser-d", new("expired@example.test", "User"), default);
        clock.Advance(TimeSpan.FromMinutes(6));
        await Rejected(() => otp.Verify("browser-d", expired.ChallengeId, sender.Code, default), 400, "Google OTP expires after five minutes");
        var race = await otp.Request("browser-e", new("race@example.test", "User"), default);
        var raceCode = sender.Code;
        async Task<bool> Consume()
        {
            try { await otp.Verify("browser-e", race.ChallengeId, raceCode, default); return true; }
            catch (WorkflowException) { return false; }
        }
        check((await Task.WhenAll(Consume(), Consume())).Count(x => x) == 1, "concurrent Google OTP verification can succeed only once");
        var duplicateRepo = new FakeRepository { DuplicateOnSave = true };
        await Rejected(() => new IdentityService(duplicateRepo).GoogleLogin("concurrent@example.test", "User", default), 409, "concurrent Google account creation reports conflict without issuing login");

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction("sysdatetime", () => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fffffff"));
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-google-key" };
        var backchannel = new GoogleBackchannel(key);
        using var tlsKey = RSA.Create(2048);
        var certRequest = new CertificateRequest("CN=localhost", tlsKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generatedCertificate = certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Windows Schannel needs an imported private key for a TLS server certificate.
        using var certificate = new X509Certificate2(generatedCertificate.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.UserKeySet);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Google:ClientId"] = "test-client", ["Google:ClientSecret"] = "test-secret" });
        builder.Services.AddControllers().AddApplicationPart(typeof(GoogleAuthController).Assembly);
        builder.Services.AddDbContext<LegacyVaultDbContext>(o => o.UseSqlite(connection));
        builder.Services.AddScoped<IVaultRepository, VaultRepository>();
        builder.Services.AddScoped<IdentityService>();
        builder.Services.AddScoped<VaultService>();
        builder.Services.AddSingleton(new SecurityOptions());
        builder.Services.AddSingleton<DocumentProtection>();
        builder.Services.AddSingleton<LegacyVault.DAL.Storage.IDocumentStore, FakeStore>();
        builder.Services.AddSingleton<IOtpSender>(sender);
        builder.Services.AddSingleton<OtpService>();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
        {
            o.Cookie.Name = "LegacyVault.Session";
            o.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
            o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        });
        builder.Services.AddGoogleEmailAuthentication(builder.Configuration);
        string? oidcFailure = null;
        builder.Services.PostConfigure<OpenIdConnectOptions>(GoogleAuthentication.Scheme, o =>
        {
            o.Backchannel = new HttpClient(backchannel);
            o.ConfigurationManager = new Microsoft.IdentityModel.Protocols.ConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(
                "https://accounts.google.com/.well-known/openid-configuration",
                new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfigurationRetriever(),
                new Microsoft.IdentityModel.Protocols.HttpDocumentRetriever(o.Backchannel));
            var failureHandler = o.Events.OnRemoteFailure;
            o.Events.OnRemoteFailure = context => { oidcFailure = context.Failure?.Message; return failureHandler(context); };
        });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(o =>
        {
            foreach (var policy in new[] { "login", "otp" })
                o.AddPolicy(policy, _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test"));
        });
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            try
            {
                if (context.Request.Method == "POST" && context.Request.Headers["X-Vault-Request"] != "1")
                    throw new WorkflowException(400, "Mutation header required.");
                await next(context);
            }
            catch (WorkflowException ex) { context.Response.StatusCode = ex.StatusCode; await context.Response.WriteAsJsonAsync(new { detail = ex.Message }); }
        });
        app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.MapControllers();
        await using (var scope = app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<LegacyVaultDbContext>().Database.EnsureCreatedAsync();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer(), ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(address) };
        client.DefaultRequestHeaders.Add("X-Vault-Request", "1");

        async Task<(string Id, string Code)> StartGoogle()
        {
            var start = await client.GetAsync("/api/auth/google/start");
            check(start.StatusCode == HttpStatusCode.Redirect, "Google start redirects to provider");
            var query = QueryHelpers.ParseQuery(start.Headers.Location!.Query);
            check(query["prompt"] == "select_account" && query["response_type"] == "code" && query.ContainsKey("code_challenge"), "Google uses account selection, authorization code and PKCE");
            backchannel.Nonce = query["nonce"].ToString();
            var callback = await client.GetAsync("/api/auth/google/oidc-callback?code=test-code&state=" + Uri.EscapeDataString(query["state"].ToString()));
            if (callback.StatusCode != HttpStatusCode.Redirect || callback.Headers.Location?.OriginalString != "/api/auth/google/callback")
                throw new Exception("OIDC callback failed: " + oidcFailure + " " + await callback.Content.ReadAsStringAsync());
            var id = handler.CookieContainer.GetCookies(new Uri(client.BaseAddress!, "/api/auth/google/callback"))[GoogleAuthentication.ChallengeCookie]!.Value;
            var page = await client.GetStringAsync("/api/auth/google/callback");
            check(page.Contains(id) && !page.Contains(sender.Code), "callback shows challenge ID without exposing OTP");
            return (id, sender.Code);
        }

        var first = await StartGoogle();
        check(!handler.CookieContainer.GetCookies(client.BaseAddress!).Cast<Cookie>().Any(c => c.Name == "LegacyVault.Session"), "Google callback does not grant application login before OTP");
        await using (var scope = app.Services.CreateAsyncScope())
            check(!await scope.ServiceProvider.GetRequiredService<LegacyVaultDbContext>().Users.AnyAsync(), "Google callback creates no account before OTP");
        using var outsider = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator }) { BaseAddress = client.BaseAddress };
        outsider.DefaultRequestHeaders.Add("X-Vault-Request", "1");
        check((await outsider.PostAsJsonAsync("/api/auth/google/otp/verify", new { challengeId = first.Id, code = first.Code })).StatusCode == HttpStatusCode.BadRequest, "HTTP Google verification requires the initiating browser cookie");
        check((await client.PostAsJsonAsync("/api/auth/google/otp/verify", new { challengeId = first.Id, code = first.Code, role = "Admin" })).StatusCode == HttpStatusCode.BadRequest, "Google verification rejects caller-supplied roles");
        var response = await client.PostAsJsonAsync("/api/auth/google/otp/verify", new { challengeId = first.Id, code = first.Code });
        var account = (await response.Content.ReadFromJsonAsync<LoginDto>())!;
        check(response.StatusCode == HttpStatusCode.OK && account.Roles.SequenceEqual(new[] { "Owner" }), "Google OTP creates an owner and issues application login");
        check((await client.PostAsJsonAsync("/api/owner/vaults", new { name = "Google Vault" })).StatusCode == HttpStatusCode.Created, "Google application cookie authorizes existing owner APIs");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LegacyVaultDbContext>();
            check(await db.Users.CountAsync() == 1 && !await db.Authentications.AnyAsync(), "Google account uses existing schema without a fake password");
            check((await scope.ServiceProvider.GetRequiredService<IdentityService>().GoogleLogin(" NEWGOOGLE@EXAMPLE.TEST ", "Changed name", default)).UserId == account.UserId, "Google login reuses the existing account by normalized email");
        }
        check((await client.PostAsJsonAsync("/api/auth/login", new { email = "newgoogle@example.test", password = "Registration123!" })).StatusCode == HttpStatusCode.Unauthorized, "Google-only user cannot log in with an invented password");
        check((await client.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode == HttpStatusCode.NoContent, "Google session works with existing logout");
        check((await client.PostAsJsonAsync("/api/auth/google/otp/verify", new { challengeId = first.Id, code = first.Code })).StatusCode == HttpStatusCode.BadRequest, "HTTP Google OTP replay is rejected");

        backchannel.EmailVerified = false;
        var invalidStart = await client.GetAsync("/api/auth/google/start");
        var invalidQuery = QueryHelpers.ParseQuery(invalidStart.Headers.Location!.Query);
        backchannel.Nonce = invalidQuery["nonce"].ToString();
        var invalid = await client.GetAsync("/api/auth/google/oidc-callback?code=test-code&state=" + Uri.EscapeDataString(invalidQuery["state"].ToString()));
        check(invalid.Headers.Location?.OriginalString == "/api/auth/google/error", "OIDC rejects an unverified Google email");
        backchannel.EmailVerified = true;
        var tampered = await client.GetAsync("/api/auth/google/oidc-callback?code=test-code&state=tampered");
        check(tampered.Headers.Location?.OriginalString == "/api/auth/google/error", "OIDC rejects tampered state");
        var nonceStart = await client.GetAsync("/api/auth/google/start");
        var nonceQuery = QueryHelpers.ParseQuery(nonceStart.Headers.Location!.Query);
        backchannel.Nonce = "wrong-nonce";
        var badNonce = await client.GetAsync("/api/auth/google/oidc-callback?code=test-code&state=" + Uri.EscapeDataString(nonceQuery["state"].ToString()));
        check(badNonce.Headers.Location?.OriginalString == "/api/auth/google/error", "OIDC rejects invalid token nonce");
        foreach (var fault in new[] { "audience", "expiry", "signature" })
        {
            var tokenStart = await client.GetAsync("/api/auth/google/start");
            var tokenQuery = QueryHelpers.ParseQuery(tokenStart.Headers.Location!.Query);
            backchannel.Nonce = tokenQuery["nonce"].ToString();
            backchannel.Fault = fault;
            var rejected = await client.GetAsync("/api/auth/google/oidc-callback?code=test-code&state=" + Uri.EscapeDataString(tokenQuery["state"].ToString()));
            check(rejected.Headers.Location?.OriginalString == "/api/auth/google/error", "OIDC rejects invalid token " + fault);
        }
        backchannel.Fault = "";

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityService>();
            var db = scope.ServiceProvider.GetRequiredService<LegacyVaultDbContext>();
            var registered = await identity.Register(new RegisterInput { FullName = "Password Owner", Email = "password@example.test", Password = "Registration123!", ConfirmPassword = "Registration123!" }, default);
            var user = await db.Users.Include(x => x.Roles).Include(x => x.Authentications).SingleAsync(x => x.UserId == registered.UserId);
            var hash = user.Authentications.Single().PasswordHash;
            user.Roles.Add(new LegacyVault.DAL.Entities.Role { RoleName = "Beneficiary" });
            await db.SaveChangesAsync();
            var existing = await identity.GoogleLogin(user.Email, "Different Name", default);
            check(existing.UserId == registered.UserId && existing.Roles.Contains("Beneficiary") && user.Authentications.Single().PasswordHash == hash, "Google preserves existing password, identity and beneficiary role");
            check((await identity.Login(user.Email, "Registration123!", default)).UserId == registered.UserId, "password login remains available after Google login");
            user.Status = "Inactive"; await db.SaveChangesAsync();
            await Rejected(() => identity.GoogleLogin(user.Email, "User", default), 403, "Google rejects inactive users");
            user.Status = "Active";
            user.Authentications.Single().LockedUntil = DateTime.UtcNow.AddMinutes(10); await db.SaveChangesAsync();
            await Rejected(() => identity.GoogleLogin(user.Email, "User", default), 403, "Google does not bypass password account lockout");
            user.Authentications.Single().LockedUntil = null;
            foreach (var roleName in new[] { "Admin", "Executor", "LegalVerifier" })
            {
                var role = new LegacyVault.DAL.Entities.Role { RoleName = roleName };
                user.Roles.Add(role); await db.SaveChangesAsync();
                await Rejected(() => identity.GoogleLogin(user.Email, "User", default), 403, "Google rejects public login for " + roleName);
                user.Roles.Remove(role); await db.SaveChangesAsync();
            }
        }
        await app.StopAsync();
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }

    private sealed class GoogleBackchannel(RsaSecurityKey key) : HttpMessageHandler
    {
        public string Nonce { get; set; } = "";
        public bool EmailVerified { get; set; } = true;
        public string Fault { get; set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            object body;
            if (request.RequestUri!.AbsolutePath.Contains("openid-configuration"))
                body = new { issuer = "https://accounts.google.com", authorization_endpoint = "https://accounts.google.com/authorize", token_endpoint = "https://accounts.google.com/token", jwks_uri = "https://accounts.google.com/keys", response_types_supported = new[] { "code" }, subject_types_supported = new[] { "public" }, id_token_signing_alg_values_supported = new[] { "RS256" } };
            else if (request.RequestUri.AbsolutePath == "/keys")
            {
                var parameters = key.Rsa.ExportParameters(false);
                body = new { keys = new[] { new { kty = "RSA", kid = key.KeyId, use = "sig", alg = "RS256", n = Base64UrlEncoder.Encode(parameters.Modulus!), e = Base64UrlEncoder.Encode(parameters.Exponent!) } } };
            }
            else
            {
                var claims = new[] { new Claim("sub", "google-test-sub"), new Claim("email", "newgoogle@example.test"), new Claim("email_verified", EmailVerified.ToString().ToLowerInvariant(), ClaimValueTypes.Boolean), new Claim("name", "Google Test User"), new Claim("nonce", Nonce), new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64) };
                using var wrongRsa = RSA.Create(2048);
                var signingKey = Fault == "signature" ? new RsaSecurityKey(wrongRsa) { KeyId = key.KeyId } : key;
                var token = new JwtSecurityToken("https://accounts.google.com", Fault == "audience" ? "wrong-client" : "test-client", claims,
                    DateTime.UtcNow.AddMinutes(-20), Fault == "expiry" ? DateTime.UtcNow.AddMinutes(-10) : DateTime.UtcNow.AddMinutes(5), new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256));
                body = new { access_token = "test-access-token", token_type = "Bearer", expires_in = 300, id_token = new JwtSecurityTokenHandler().WriteToken(token) };
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
