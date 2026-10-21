using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LegacyVault.API.Authentication;
using LegacyVault.API.Controllers;
using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Security;
using LegacyVault.BLL.Services;
using LegacyVault.DAL.Context;
using LegacyVault.DAL.Entities;
using LegacyVault.DAL.Repositories;
using LegacyVault.DAL.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

internal static class JwtWorkflowTests
{
    public static async Task Run(Action<bool, string> check)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        connection.CreateFunction("sysdatetime", () => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fffffff"));
        var dbOptions = new DbContextOptionsBuilder<LegacyVaultDbContext>().UseSqlite(connection).Options;
        using var ownerKey = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=Owner upload test", ownerKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        certificateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        certificateRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, true));
        using var ownerCertificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var executorKey = RSA.Create(2048);
        var executorCertificateRequest = new CertificateRequest("CN=Executor completion test", executorKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        executorCertificateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        executorCertificateRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, true));
        using var executorCertificate = executorCertificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        var security = new SecurityOptions { EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), TrustedRootCertificates = new() { ownerCertificate.ExportCertificatePem() } };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders(); builder.WebHost.UseTestServer();
        TestAuthentication.Configure(builder.Configuration);
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Cors:Origins:0"] = "https://frontend.example.test", ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)) });
        builder.Services.AddControllers().AddApplicationPart(typeof(OwnerController).Assembly);
        builder.Services.AddDbContext<LegacyVaultDbContext>(o => o.UseSqlite(connection));
        builder.Services.AddScoped<IVaultRepository, VaultRepository>(); builder.Services.AddScoped<IWorkflowRepository, WorkflowRepository>();
        builder.Services.AddScoped<IdentityService>(); builder.Services.AddScoped<VaultService>(); builder.Services.AddScoped<ManagementService>();
        builder.Services.AddScoped<IBeneficiaryService, BeneficiaryService>();
        builder.Services.AddSingleton(security);
        builder.Services.AddSingleton<DocumentProtection>(); builder.Services.AddSingleton<IDocumentStore, FakeStore>();
        builder.Services.AddSingleton<IOtpSender, FakeSender>(); builder.Services.AddSingleton<OtpService>();
        builder.Services.AddVaultJwt(builder.Configuration); builder.Services.AddGoogleEmailAuthentication(builder.Configuration);
        builder.Services.AddVaultCors(builder.Configuration); builder.Services.AddAuthorization();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddRateLimiter(o => { foreach (var policy in new[] { "login", "otp" }) o.AddPolicy(policy, _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test")); });
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(o => { o.SwaggerDoc("v1", new() { Title = "Test", Version = "v1" }); o.AddSecurityDefinition("Bearer", new() { Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http, Scheme = "bearer" }); });
        await using var app = builder.Build(); app.UseVaultErrors(); app.UseSwagger(); app.UseRouting(); app.UseCors("Frontend"); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter(); app.MapControllers();
        var ids = new Dictionary<string, int>();
        await using (var db = new LegacyVaultDbContext(dbOptions))
        {
            await db.Database.EnsureCreatedAsync();
            var roles = new[] { "Owner", "Beneficiary", "Executor", "Staff", "LegalVerifier", "Admin" }.ToDictionary(x => x, x => new Role { RoleName = x });
            var hash = IdentityService.HashPassword("Registration123!");
            foreach (var (name, role) in new[] { ("owner", "Owner"), ("other", "Owner"), ("recipient", "Beneficiary"), ("executor", "Executor"), ("staff", "Staff"), ("staff2", "Staff"), ("legal", "LegalVerifier"), ("legal2", "LegalVerifier") })
            {
                var u = new User { FullName = name, Email = name + "@example.test", Status = "Active", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
                u.Roles.Add(roles[role]); u.Authentications.Add(new() { PasswordHash = hash, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }); db.Users.Add(u);
            }
            await db.SaveChangesAsync(); foreach (var u in await db.Users.ToListAsync()) ids[u.FullName] = u.UserId;
        }
        await app.StartAsync();
        security.SignerCertificates[ids["owner"]] = ownerCertificate.ExportCertificatePem();
        security.SignerCertificates[ids["other"]] = ownerCertificate.ExportCertificatePem();
        security.SignerCertificates[ids["executor"]] = executorCertificate.ExportCertificatePem();
        security.TrustedRootCertificates.Add(executorCertificate.ExportCertificatePem());
        using var client = app.GetTestClient(); client.BaseAddress = new("https://localhost");
        var issuer = app.Services.GetRequiredService<IAccessTokenIssuer>();
        void As(string name) { client.DefaultRequestHeaders.Authorization = new("Bearer", issuer.Issue(new(ids[name], Array.Empty<string>())).AccessToken); }
        async Task Expect(HttpResponseMessage response, HttpStatusCode status, string name)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != status) throw new Exception(name + $": expected {(int)status}, received {(int)response.StatusCode}: {body}");
            check(true, name);
        }
        async Task EditUser(string name, Action<User> edit)
        {
            await using var db = new LegacyVaultDbContext(dbOptions); var u = await db.Users.Include(x => x.Roles).Include(x => x.Authentications).Include(x => x.TwoFactorAuths).SingleAsync(x => x.UserId == ids[name]); edit(u); await db.SaveChangesAsync();
        }
        await Expect(await client.GetAsync("/api/auth/me"), HttpStatusCode.Unauthorized, "JWT missing is rejected");
        var settings = app.Services.GetRequiredService<JwtSettings>();
        string InvalidToken(string fault)
        {
            var key = fault == "signature" ? new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64)) : settings.Key();
            var token = new JwtSecurityToken(fault == "issuer" ? "wrong" : settings.Issuer, fault == "audience" ? "wrong" : settings.Audience,
                new[] { new Claim("sub", ids["owner"].ToString()), new Claim("vault_session", "test-session") }, DateTime.UtcNow.AddHours(-1), fault == "expiry" ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(10), new SigningCredentials(key, fault == "algorithm" ? SecurityAlgorithms.HmacSha512 : SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        foreach (var fault in new[] { "signature", "issuer", "audience", "expiry", "algorithm" })
        { client.DefaultRequestHeaders.Authorization = new("Bearer", InvalidToken(fault)); await Expect(await client.GetAsync("/api/auth/me"), HttpStatusCode.Unauthorized, "JWT rejects invalid " + fault); }
        As("owner"); var ownerToken = client.DefaultRequestHeaders.Authorization!;
        var me = (await client.GetFromJsonAsync<MeDto>("/api/auth/me"))!;
        check(me.UserId == ids["owner"] && me.FullName == "owner" && me.Roles.Contains("Owner"), "me exposes public profile and current DB roles");
        await EditUser("owner", u => u.Status = "Inactive"); await Expect(await client.GetAsync("/api/auth/me"), HttpStatusCode.Unauthorized, "JWT with inactive account is rejected before expiry");
        await Expect(await client.PostAsync("/api/owner/life-confirmation", null), HttpStatusCode.Unauthorized, "inactive Owner cannot confirm alive");
        await EditUser("owner", u => { u.Status = "Active"; u.Authentications.Single().LockedUntil = DateTime.UtcNow.AddMinutes(5); }); await Expect(await client.GetAsync("/api/auth/me"), HttpStatusCode.Unauthorized, "JWT with locked account is rejected before expiry");
        await Expect(await client.PostAsync("/api/owner/life-confirmation", null), HttpStatusCode.Unauthorized, "locked Owner cannot confirm alive");
        await EditUser("owner", u => { u.Authentications.Single().LockedUntil = null; u.Roles.Clear(); }); await Expect(await client.PostAsJsonAsync("/api/owner/vaults", new { name = "Forbidden" }), HttpStatusCode.Forbidden, "old JWT cannot retain revoked Owner role");
        await Expect(await client.PostAsync("/api/owner/life-confirmation", null), HttpStatusCode.Forbidden, "revoked Owner role cannot confirm alive with old token");
        await using (var db = new LegacyVaultDbContext(dbOptions)) { (await db.Users.SingleAsync(u => u.UserId == ids["owner"])).Roles.Add(await db.Roles.SingleAsync(r => r.RoleName == "Owner")); await db.SaveChangesAsync(); }
        client.DefaultRequestHeaders.Authorization = null;
        await Expect(await client.PostAsync("/api/owner/life-confirmation", null), HttpStatusCode.Unauthorized, "life confirmation requires Bearer authentication");
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "owner@example.test", password = "Registration123!" });
        await Expect(await client.PostAsJsonAsync("/api/auth/login", new { email = "owner@example.test", password = "Registration123!", role = "Admin" }), HttpStatusCode.BadRequest, "password login rejects role selection");
        await Expect(await client.PostAsJsonAsync("/api/auth/refresh-token", new { refreshToken = "unused" }), HttpStatusCode.Unauthorized, "refresh endpoint from main rejects unknown persisted token");
        var realToken = await TestAuthentication.UseToken(client, login); check(realToken.TokenType == "Bearer" && realToken.ExpiresIn == 900, "password login returns bounded JWT token contract");
        check(!string.IsNullOrEmpty(realToken.RefreshToken) && realToken.RefreshTokenExpiresAt > DateTime.UtcNow, "merged login persists and returns refresh token only after authentication");
        var rotated = await TestAuthentication.UseToken(client, await client.PostAsJsonAsync("/api/auth/refresh-token", new { refreshToken = realToken.RefreshToken }));
        check(rotated.RefreshToken != realToken.RefreshToken, "refresh rotates token while preserving Bearer contract");
        await Expect(await client.PostAsJsonAsync("/api/auth/refresh-token", new { refreshToken = realToken.RefreshToken }), HttpStatusCode.Unauthorized, "rotated refresh token cannot replay");
        await EditUser("owner", u => u.Authentications.Single().LockedUntil = DateTime.UtcNow.AddMinutes(5));
        await Expect(await client.PostAsJsonAsync("/api/auth/refresh-token", new { refreshToken = rotated.RefreshToken }), HttpStatusCode.Unauthorized, "refresh cannot bypass account lock from database");
        await EditUser("owner", u => u.Authentications.Single().LockedUntil = null);
        await Expect(await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = rotated.RefreshToken }), HttpStatusCode.NoContent, "logout accepts and revokes persisted refresh token");
        await Expect(await client.PostAsJsonAsync("/api/auth/refresh-token", new { refreshToken = rotated.RefreshToken }), HttpStatusCode.Unauthorized, "revoked logout refresh token cannot be reused");
        await Expect(await client.PostAsync("/api/auth/logout", null), HttpStatusCode.NoContent, "logout retains compatibility with empty request body");
        await EditUser("owner", u => u.TwoFactorAuths.Add(new() { Method = "Email", Secret = "unused-email-secret", IsEnabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }));
        client.DefaultRequestHeaders.Authorization = null;
        var factorResponse = await client.PostAsJsonAsync("/api/auth/login", new { email = "owner@example.test", password = "Registration123!" });
        await Expect(factorResponse, HttpStatusCode.Accepted, "enabled Email 2FA returns challenge instead of token");
        var challenge = (await factorResponse.Content.ReadFromJsonAsync<LoginChallengeDto>())!;
        await Expect(await client.PostAsJsonAsync("/api/auth/2fa/resend", new { challengeId = challenge.ChallengeId }), HttpStatusCode.TooManyRequests, "HTTP 2FA resend enforces cooldown without issuing a token");
        await Expect(await client.PostAsJsonAsync("/api/auth/2fa/resend", new { challengeId = challenge.ChallengeId, email = "attacker@example.test" }), HttpStatusCode.BadRequest, "HTTP 2FA resend rejects caller-supplied destination");
        client.DefaultRequestHeaders.Authorization = new("Bearer", challenge.ChallengeId); await Expect(await client.GetAsync("/api/auth/me"), HttpStatusCode.Unauthorized, "2FA challenge cannot authorize business API");
        client.DefaultRequestHeaders.Authorization = null;
        var code = ((FakeSender)app.Services.GetRequiredService<IOtpSender>()).Code;
        await Expect(await client.PostAsJsonAsync("/api/auth/2fa/verify", new { challengeId = challenge.ChallengeId, code = code == "000000" ? "000001" : "000000" }), HttpStatusCode.BadRequest, "2FA rejects wrong email code");
        await TestAuthentication.UseToken(client, await client.PostAsJsonAsync("/api/auth/2fa/verify", new { challengeId = challenge.ChallengeId, code }));
        await Expect(await client.PostAsJsonAsync("/api/auth/2fa/verify", new { challengeId = challenge.ChallengeId, code }), HttpStatusCode.BadRequest, "2FA code cannot replay");
        await EditUser("owner", u => { var f = u.TwoFactorAuths.Single(); f.Method = "SMS"; });
        await Expect(await client.PostAsJsonAsync("/api/auth/login", new { email = "owner@example.test", password = "Registration123!" }), HttpStatusCode.ServiceUnavailable, "unsupported enabled 2FA is not bypassed");
        await EditUser("owner", u => u.TwoFactorAuths.Single().IsEnabled = false);
        const string totpSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        await EditUser("other", u => u.TwoFactorAuths.Add(new() { Method = "TOTP", Secret = totpSecret, IsEnabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }));
        var totpResponse = await client.PostAsJsonAsync("/api/auth/login", new { email = "other@example.test", password = "Registration123!" });
        await Expect(totpResponse, HttpStatusCode.Accepted, "TOTP-enabled password login requires second factor");
        var totpChallenge = (await totpResponse.Content.ReadFromJsonAsync<LoginChallengeDto>())!;
        var totpCode = LoginFactorStore.Totp(totpSecret, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        await Expect(await client.PostAsJsonAsync("/api/auth/2fa/verify", new { challengeId = totpChallenge.ChallengeId, code = totpCode }), HttpStatusCode.OK, "RFC6238 Base32 TOTP completes authentication");
        await Expect(await client.PostAsJsonAsync("/api/auth/2fa/verify", new { challengeId = totpChallenge.ChallengeId, code = totpCode }), HttpStatusCode.BadRequest, "TOTP challenge cannot replay");
        await EditUser("other", u => u.TwoFactorAuths.Single().IsEnabled = false);
        check(LoginFactorStore.Totp(totpSecret, 1) == "287082", "TOTP implementation matches RFC6238 SHA1 six-digit vector");
        var testClock = new FactorClock();
        var factorStore = new LoginFactorStore(new FakeSender(), testClock);
        var factorUser = new User { UserId = 900, Email = "factor@example.test" };
        factorUser.TwoFactorAuths.Add(new() { TwoFactorId = 901, Method = "TOTP", Secret = totpSecret, IsEnabled = true });
        var challenge1 = await factorStore.Start(factorUser, default);
        await factorStore.Verify(factorUser, challenge1.ChallengeId, LoginFactorStore.Totp(totpSecret, testClock.GetUtcNow().ToUnixTimeSeconds() / 30), default);
        testClock.Advance(TimeSpan.FromSeconds(61)); var challenge2 = await factorStore.Start(factorUser, default);
        testClock.Advance(TimeSpan.FromMinutes(6));
        try { await factorStore.Verify(factorUser, challenge2.ChallengeId, "000000", default); throw new Exception("Expected expired factor"); }
        catch (WorkflowException ex) { check(ex.StatusCode == 400, "2FA challenge expires before it can issue token"); }

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/owner/assets"); preflight.Headers.Add("Origin", "https://frontend.example.test"); preflight.Headers.Add("Access-Control-Request-Method", "POST"); preflight.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        var cors = await client.SendAsync(preflight); check(cors.Headers.GetValues("Access-Control-Allow-Origin").Single() == "https://frontend.example.test", "CORS permits configured Bearer/multipart origin");
        using var deniedPreflight = new HttpRequestMessage(HttpMethod.Options, "/api/owner/assets"); deniedPreflight.Headers.Add("Origin", "https://evil.example.test"); deniedPreflight.Headers.Add("Access-Control-Request-Method", "POST");
        check(!(await client.SendAsync(deniedPreflight)).Headers.Contains("Access-Control-Allow-Origin"), "CORS rejects unconfigured origin");
        var swagger = await client.GetStringAsync("/swagger/v1/swagger.json"); check(swagger.Contains("Bearer") && swagger.Contains("bearer"), "Swagger exposes Bearer scheme and new endpoints without route collisions");
        var routes = app.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints.OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods ?? Array.Empty<string>())
                .Select(method => method + " " + System.Text.RegularExpressions.Regex.Replace(e.RoutePattern.RawText ?? "", @"\{[^}]+\}", "{}"))).ToArray();
        check(routes.Distinct(StringComparer.OrdinalIgnoreCase).Count() == routes.Length, "every HTTP method and normalized controller route is unique");
        check(!swagger.Contains("/api/staff/"), "OpenAPI contains no Staff endpoints");

        As("owner");
        await Expect(await client.PostAsJsonAsync("/api/owner/vaults", new { name = "Too long", description = new string('a', 501) }), HttpStatusCode.BadRequest, "vault description is limited to scaffold 500 characters");
        var initialLife = (await client.GetFromJsonAsync<LifeConfirmationDto>("/api/owner/life-confirmation"))!;
        check(initialLife.OwnerId == ids["owner"] && initialLife.LastConfirmedAliveAt is null, "Owner without confirmation sees null instead of profile UpdatedAt");
        var beforeConfirmation = DateTime.UtcNow;
        var lifeResponse = await client.PostAsync("/api/owner/life-confirmation", null);
        await Expect(lifeResponse, HttpStatusCode.OK, "Owner confirms alive without body or client timestamp");
        var firstLife = (await lifeResponse.Content.ReadFromJsonAsync<LifeConfirmationDto>())!;
        check(firstLife.OwnerId == ids["owner"] && firstLife.LastConfirmedAliveAt >= beforeConfirmation && firstLife.LastConfirmedAliveAt <= DateTime.UtcNow && firstLife.LastConfirmedAliveAt.Value.Kind == DateTimeKind.Utc, "confirmation uses server UTC time and authenticated Owner");
        await using (var db = new LegacyVaultDbContext(dbOptions))
        {
            var proof = await db.ProofOfLives.SingleAsync(p => p.UserId == ids["owner"]);
            check(proof.LastPingAt == firstLife.LastConfirmedAliveAt && proof.NextPingDue == proof.LastPingAt.AddDays(30) && proof.GracePeriodDays == 14, "first confirmation persists in scaffolded PROOF_OF_LIFE with default schedule");
            proof.FrequencyDays = 45; proof.GracePeriodDays = 7; proof.MissedCount = 2; proof.Status = "Overdue";
            await db.SaveChangesAsync();
        }
        await Expect(await client.PostAsJsonAsync("/api/owner/life-confirmation", new { ownerId = ids["other"], lastConfirmedAliveAt = "2099-01-01T00:00:00Z" }), HttpStatusCode.OK, "client cannot choose another Owner or forge confirmation timestamp");
        var lastLife = (await client.GetFromJsonAsync<LifeConfirmationDto>("/api/owner/life-confirmation"))!;
        check(lastLife.LastConfirmedAliveAt >= firstLife.LastConfirmedAliveAt && lastLife.LastConfirmedAliveAt <= DateTime.UtcNow, "GET returns latest persisted confirmation after repeat click");
        await using (var db = new LegacyVaultDbContext(dbOptions))
        {
            var events = await db.AuditLogs.Where(a => a.Action == "ConfirmAlive").ToListAsync();
            check(events.Count == 2 && events.All(a => a.UserId == ids["owner"] && a.EntityId == ids["owner"] && a.EntityType == "User" && a.Result == "Success"), "confirmation audit is persisted without overwriting earlier events or spoofing identity");
            var proof = await db.ProofOfLives.SingleAsync(p => p.UserId == ids["owner"]);
            check(proof.FrequencyDays == 45 && proof.GracePeriodDays == 7 && proof.NextPingDue == proof.LastPingAt.AddDays(45) && proof.MissedCount == 0 && proof.Status == "Active", "repeat click updates existing proof, preserves schedule and resets missed checks");
        }
        As("other"); check((await client.GetFromJsonAsync<LifeConfirmationDto>("/api/owner/life-confirmation"))!.LastConfirmedAliveAt is null, "another Owner cannot read caller confirmation through client input");
        foreach (var actor in new[] { "executor", "legal", "recipient" })
        {
            As(actor);
            await Expect(await client.GetAsync("/api/owner/life-confirmation"), HttpStatusCode.Forbidden, "non-Owner cannot read private confirmation: " + actor);
            await Expect(await client.PostAsync("/api/owner/life-confirmation", null), HttpStatusCode.Forbidden, "non-Owner cannot confirm alive: " + actor);
        }
        As("owner");
        async Task<(int Vault, int Asset)> CreateFixture(string name, string actor = "owner")
        {
            As(actor); var vResponse = await client.PostAsJsonAsync("/api/owner/vaults", new { name }); var v = (await vResponse.Content.ReadFromJsonAsync<VaultDto>())!;
            var aResponse = await client.PostAsJsonAsync("/api/owner/assets", new { vaultId = v.VaultId, name = name + " asset", type = "Document" }); var a = (await aResponse.Content.ReadFromJsonAsync<AssetDto>())!;
            await Expect(await client.PostAsJsonAsync($"/api/owner/assets/{a.AssetId}/beneficiaries", new { email = "recipient@example.test", allocation = 100 }), HttpStatusCode.Created, "fixture beneficiary is persisted");
            return (v.VaultId, a.AssetId);
        }
        var editable = await CreateFixture("Editable");
        await Expect(await client.PutAsJsonAsync($"/api/owner/vaults/{editable.Vault}", new { name = "Updated", description = "500 bounded" }), HttpStatusCode.OK, "owner updates vault");
        var futureVersion = DateTime.UtcNow.AddSeconds(1);
        await using (var db = new LegacyVaultDbContext(dbOptions)) { (await db.DigitalVaults.SingleAsync(v => v.VaultId == editable.Vault)).UpdatedAt = futureVersion; await db.SaveChangesAsync(); }
        await client.PutAsJsonAsync($"/api/owner/vaults/{editable.Vault}", new { name = "Monotonic version" });
        await using (var db = new LegacyVaultDbContext(dbOptions)) check((await db.DigitalVaults.SingleAsync(v => v.VaultId == editable.Vault)).UpdatedAt > futureVersion, "concurrency timestamp advances even if clock has not advanced past stored version");
        await Expect(await client.PutAsJsonAsync($"/api/owner/assets/{editable.Asset}", new { name = "Updated asset", type = "Document" }), HttpStatusCode.OK, "owner updates asset");
        As("other"); await Expect(await client.PutAsJsonAsync($"/api/owner/vaults/{editable.Vault}", new { name = "Other" }), HttpStatusCode.NotFound, "another owner cannot update vault");
        As("owner"); var allocations = (await client.GetFromJsonAsync<PageDto<AssignmentDto>>($"/api/owner/assets/{editable.Asset}/allocations"))!.Items; var assignment = allocations.Single().AssignmentId;
        await Expect(await client.PutAsJsonAsync($"/api/owner/assets/{editable.Asset}/allocations/{assignment}", new { allocation = 50 }), HttpStatusCode.OK, "owner updates allocation");
        await Expect(await client.PutAsJsonAsync($"/api/owner/assets/{editable.Asset}/allocations/{assignment}", new { allocation = 50.001 }), HttpStatusCode.BadRequest, "updated allocation rejects precision loss");
        await Expect(await client.PostAsJsonAsync($"/api/owner/assets/{editable.Asset}/allocations/{assignment}/revoke", new { }), HttpStatusCode.OK, "owner revokes allocation without deleting history");
        await Expect(await client.PostAsJsonAsync($"/api/owner/assets/{editable.Asset}/revoke", new { }), HttpStatusCode.NoContent, "owner revokes asset");
        await Expect(await client.PostAsJsonAsync($"/api/owner/vaults/{editable.Vault}/close", new { }), HttpStatusCode.NoContent, "owner closes unprocessed vault");
        var concurrentAllocation = await CreateFixture("Concurrent allocation");
        var initialAllocation = (await client.GetFromJsonAsync<PageDto<AssignmentDto>>($"/api/owner/assets/{concurrentAllocation.Asset}/allocations"))!.Items.Single();
        await client.PutAsJsonAsync($"/api/owner/assets/{concurrentAllocation.Asset}/allocations/{initialAllocation.AssignmentId}", new { allocation = 40 });
        await Expect(await client.PostAsJsonAsync($"/api/owner/assets/{concurrentAllocation.Asset}/beneficiaries", new { email = "other@example.test", allocation = 40 }), HttpStatusCode.Created, "second allocation persists below total cap");
        var allocationRows = (await client.GetFromJsonAsync<PageDto<AssignmentDto>>($"/api/owner/assets/{concurrentAllocation.Asset}/allocations"))!.Items;
        await Expect(await client.PutAsJsonAsync($"/api/owner/assets/{concurrentAllocation.Asset}/allocations/{initialAllocation.AssignmentId}", new { allocation = 61 }), HttpStatusCode.Conflict, "allocation update cannot exceed total 100 percent");
        var competitor = allocationRows.Single(a => a.BeneficiaryId == ids["other"]).AssignmentId;
        var racingOptions = new DbContextOptionsBuilder<LegacyVaultDbContext>().UseSqlite(connection).AddInterceptors(new AllocationRace(dbOptions, ids["owner"], concurrentAllocation.Asset, competitor, app.Services)).Options;
        await using (var db = new LegacyVaultDbContext(racingOptions))
        {
            try { await Service(db, app.Services).ChangeAllocation(ids["owner"], concurrentAllocation.Asset, initialAllocation.AssignmentId, new(60), default); throw new Exception("Expected allocation update conflict"); }
            catch (RepositoryConflictException) { check(true, "concurrent allocation updates cannot jointly exceed 100 percent"); }
        }
        await using (var db = new LegacyVaultDbContext(dbOptions)) check((await db.BeneficiaryAssignments.Where(a => a.AssetId == concurrentAllocation.Asset && a.Status == "Active").ToListAsync()).Sum(a => a.Allocation) == 100, "concurrent allocation loser is rolled back");

        async Task<DocumentDto> UploadAsset(int asset)
        {
            using var form = new MultipartFormDataContent();
            var bytes = Encoding.UTF8.GetBytes("inheritance instructions");
            var content = new ByteArrayContent(bytes); content.Headers.ContentType = new("text/plain"); form.Add(content, "File", "instructions.txt");
            form.Add(new ByteArrayContent(ownerKey.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)), "Signature", "instructions.sig");
            var response = await client.PostAsync($"/api/owner/assets/{asset}/documents", form);
            await Expect(response, HttpStatusCode.OK, "owner uploads signed encrypted asset document without OwnerAlive or DeathCertificate");
            return (await response.Content.ReadFromJsonAsync<DocumentDto>())!;
        }
        async Task<HttpResponseMessage> UploadCertificate(int request)
        {
            using var form = new MultipartFormDataContent();
            var bytes = new ByteArrayContent("%PDF-1.7 executor death proof"u8.ToArray()); bytes.Headers.ContentType = new("application/pdf");
            form.Add(bytes, "File", "death.pdf");
            form.Add(new StringContent(ids["owner"].ToString()), "UploadedBy");
            form.Add(new StringContent(ids["owner"].ToString()), "ExecutorId");
            return await client.PostAsync($"/api/executor/handovers/{request}/death-certificate", form);
        }
        var handover = await CreateFixture("Handover"); var doc = await UploadAsset(handover.Asset);
        using (var forbiddenDeath = new MultipartFormDataContent())
        {
            var bytes = Encoding.UTF8.GetBytes("inheritance instructions");
            var content = new ByteArrayContent(bytes); content.Headers.ContentType = new("text/plain"); forbiddenDeath.Add(content, "File", "instructions.txt");
            forbiddenDeath.Add(new ByteArrayContent(ownerKey.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)), "Signature", "instructions.sig");
            forbiddenDeath.Add(new ByteArrayContent("%PDF-1.7 death"u8.ToArray()), "DeathCertificate", "death.pdf");
            await Expect(await client.PostAsync($"/api/owner/assets/{handover.Asset}/documents", forbiddenDeath), HttpStatusCode.BadRequest, "Owner cannot submit a DeathCertificate through asset upload");
        }
        As("other"); await Expect(await client.GetAsync($"/api/owner/assets/{handover.Asset}/documents/{doc.DocumentId}/content"), HttpStatusCode.NotFound, "owner download rejects another owner");
        using (var crossOwner = new MultipartFormDataContent())
        {
            var content = new ByteArrayContent("cross owner file"u8.ToArray()); content.Headers.ContentType = new("text/plain"); crossOwner.Add(content, "File", "other.txt");
            crossOwner.Add(new ByteArrayContent(new byte[256]), "Signature", "other.sig");
            await Expect(await client.PostAsync($"/api/owner/assets/{handover.Asset}/documents", crossOwner), HttpStatusCode.NotFound, "Owner upload checks ownership before signature or persistence");
        }
        As("owner"); check((await client.GetStringAsync($"/api/owner/assets/{handover.Asset}/documents/{doc.DocumentId}/content")) == "inheritance instructions", "owner downloads decrypted content with ownership check");
        using (var bad = new MultipartFormDataContent())
        {
            var b = new ByteArrayContent("wrong PNG"u8.ToArray()); b.Headers.ContentType = new("image/png"); bad.Add(b, "File", "bad.png"); bad.Add(new StringContent(ids["recipient"].ToString()), "BeneficiaryId"); bad.Add(new ByteArrayContent(new byte[256]), "Signature", "bad.sig");
            await Expect(await client.PostAsync($"/api/owner/assets/{handover.Asset}/documents", bad), HttpStatusCode.BadRequest, "upload rejects MIME/content mismatch before signature");
        }
        using (var oversized = new MultipartFormDataContent())
        {
            oversized.Add(new ByteArrayContent(new byte[10 * 1024 * 1024 + 1]), "File", "large.txt"); oversized.Add(new StringContent(ids["recipient"].ToString()), "BeneficiaryId"); oversized.Add(new ByteArrayContent(new byte[256]), "Signature", "large.sig");
            await Expect(await client.PostAsync($"/api/owner/assets/{handover.Asset}/documents", oversized), HttpStatusCode.RequestEntityTooLarge, "upload exceeds 10 MB returns 413");
        }
        await Expect(await client.PutAsJsonAsync($"/api/owner/vaults/{handover.Vault}/executor", new { executorId = ids["executor"] }), HttpStatusCode.NoContent, "owner selects independent active Executor");
        As("staff"); await Expect(await client.PostAsJsonAsync($"/api/staff/intake-vaults/{handover.Vault}/accept", new { }), HttpStatusCode.NotFound, "removed Staff intake route is not exposed");
        As("owner"); var requestResponse = await client.PostAsJsonAsync($"/api/owner/vaults/{handover.Vault}/handovers", new { }); await Expect(requestResponse, HttpStatusCode.Created, "owner creates handover atomically with cases"); var request = (await requestResponse.Content.ReadFromJsonAsync<HandoverDto>())!;
        await Expect(await client.PostAsJsonAsync($"/api/owner/vaults/{handover.Vault}/handovers", new { }), HttpStatusCode.Conflict, "duplicate handover creation is rejected");
        await Expect(await client.PostAsJsonAsync($"/api/owner/vaults/{handover.Vault}/close", new { }), HttpStatusCode.Conflict, "close protects processing handover");
        await Expect(await client.PostAsJsonAsync($"/api/owner/assets/{handover.Asset}/documents/{doc.DocumentId}/revoke", new { }), HttpStatusCode.Conflict, "document revocation protects processing handover");
        As("staff"); await Expect(await client.GetAsync($"/api/executor/handovers/{request.RequestId}"), HttpStatusCode.Forbidden, "legacy Staff role cannot inspect executor request");
        As("executor"); await Expect(await client.GetAsync($"/api/executor/handovers/{request.RequestId}"), HttpStatusCode.OK, "assigned Executor sees handover detail");
        var enriched = (await client.GetFromJsonAsync<HandoverDto>($"/api/executor/handovers/{request.RequestId}"))!;
        check(enriched.Owner?.UserId == ids["owner"] && enriched.Owner.FullName == "owner" && enriched.ExecutorId == ids["executor"], "assigned detail includes Owner identity and responsible Executor");
        check((await client.GetFromJsonAsync<OwnerLifeStatusDto>($"/api/executor/handovers/{request.RequestId}/owner-status"))!.Status == "NotConfirmed", "no death evidence does not assert Owner is deceased or alive");
        check((await client.GetFromJsonAsync<OwnerLifeStatusDto>($"/api/executor/handovers/{request.RequestId}/owner-status"))!.LastConfirmedAliveAt == lastLife.LastConfirmedAliveAt, "assigned Executor sees last Owner self-confirmation separately from death status");
        As("owner"); await Expect(await UploadCertificate(request.RequestId), HttpStatusCode.Forbidden, "Owner cannot upload a handover death certificate");
        As("recipient"); await Expect(await UploadCertificate(request.RequestId), HttpStatusCode.Forbidden, "Beneficiary cannot upload a death certificate");
        As("legal"); await Expect(await UploadCertificate(request.RequestId), HttpStatusCode.Forbidden, "LegalVerifier cannot upload a death certificate");
        As("staff"); await Expect(await UploadCertificate(request.RequestId), HttpStatusCode.Forbidden, "legacy Staff cannot upload a death certificate");
        await using (var db = new LegacyVaultDbContext(dbOptions))
        {
            (await db.Users.SingleAsync(u => u.UserId == ids["other"])).Roles.Add(await db.Roles.SingleAsync(r => r.RoleName == "Executor"));
            await db.SaveChangesAsync();
        }
        As("other"); await Expect(await UploadCertificate(request.RequestId), HttpStatusCode.NotFound, "unassigned Executor with another role cannot upload death certificate");
        var overview = (await client.GetFromJsonAsync<PageDto<HandoverOverviewDto>>("/api/executor/handovers/overview"))!;
        check(overview.Items.Any(r => r.RequestId == request.RequestId && r.Owner.UserId == ids["owner"] && r.Owner.FullName == "owner" && !r.IsAssignedToMe), "Executor overview includes other Executors' handovers and their Owners");
        var overviewJson = await client.GetStringAsync("/api/executor/handovers/overview");
        check(!overviewJson.Contains("storagePath", StringComparison.OrdinalIgnoreCase) && !overviewJson.Contains("documents") && !overviewJson.Contains("cases") && !overviewJson.Contains("email"), "global overview exposes status metadata without sensitive documents or contacts");
        check((await client.GetFromJsonAsync<PageDto<HandoverOverviewDto>>("/api/executor/handovers/overview?assignedOnly=true"))!.Items.Count == 0, "overview can filter only the caller's active assignments");
        check((await client.GetFromJsonAsync<PageDto<HandoverOverviewDto>>($"/api/executor/handovers/overview?ownerId={ids["owner"]}&status=Draft&pageSize=1"))!.Items.Count == 1, "overview supports Owner, status and pagination filters");
        check((await client.GetFromJsonAsync<PageDto<HandoverOverviewDto>>($"/api/executor/handovers/overview?ownerId={ids["other"]}"))!.Items.Count == 0, "overview Owner filter cannot return another Owner's rows");
        await Expect(await client.GetAsync("/api/executor/handovers/overview?pageSize=0"), HttpStatusCode.BadRequest, "overview rejects invalid pagination");
        await Expect(await client.GetAsync("/api/executor/handovers/overview?status=Arbitrary"), HttpStatusCode.BadRequest, "overview rejects arbitrary status filter");
        foreach (var suffix in new[] { "owner-status", "history", "documents" })
            await Expect(await client.GetAsync($"/api/executor/handovers/{request.RequestId}/{suffix}"), HttpStatusCode.NotFound, "overview access does not grant unassigned " + suffix + " access");
        await using (var db = new LegacyVaultDbContext(dbOptions))
        {
            var u = await db.Users.Include(u => u.Roles).SingleAsync(u => u.UserId == ids["other"]); u.Roles.Remove(u.Roles.Single(r => r.RoleName == "Executor"));
            (await db.Users.SingleAsync(u => u.UserId == ids["executor"])).Roles.Add(await db.Roles.SingleAsync(r => r.RoleName == "Owner")); await db.SaveChangesAsync();
        }
        As("executor"); await Expect(await UploadCertificate(request.RequestId), HttpStatusCode.OK, "assigned multi-role Executor uploads death certificate before legal submission");
        var reported = (await client.GetFromJsonAsync<OwnerLifeStatusDto>($"/api/executor/handovers/{request.RequestId}/owner-status"))!;
        check(reported.Status == "ReportedDeceased" && reported.VerifierId is null && reported.DeathCertificateDocumentId is not null, "Executor evidence reports death without legally confirming it");
        As("owner"); await Expect(await client.PostAsync("/api/owner/life-confirmation", null), HttpStatusCode.Conflict, "Owner cannot overwrite an outstanding death report with self-confirmation");
        check((await client.GetFromJsonAsync<LifeConfirmationDto>("/api/owner/life-confirmation"))!.LastConfirmedAliveAt == lastLife.LastConfirmedAliveAt, "conflicting confirmation does not replace last persisted time");
        As("executor");
        var evidenceMetadata = (await client.GetFromJsonAsync<DocumentDto[]>($"/api/executor/handovers/{request.RequestId}/documents"))!;
        check(evidenceMetadata.Single().Purpose == "DeathCertificate", "Executor document list identifies death certificate purpose from authenticated envelope");
        var verifiers = (await client.GetFromJsonAsync<VerifierOptionDto[]>($"/api/executor/handovers/{request.RequestId}/legal-verifiers"))!;
        check(verifiers.Any(v => v.UserId == ids["legal"]) && verifiers.All(v => v.UserId != ids["executor"] && v.UserId != ids["owner"]), "Executor can select eligible independent legal verifiers");
        await using (var db = new LegacyVaultDbContext(dbOptions))
        {
            var evidence = await db.HandoverDocuments.SingleAsync(d => d.RequestId == request.RequestId);
            check(evidence.UploadedBy == ids["executor"] && !await db.AssetDocuments.AnyAsync(d => d.FileName == "death.pdf"), "death certificate belongs to HANDOVER_DOCUMENTS and authenticated Executor, not asset documents");
            check((await db.HandoverRequests.SingleAsync(r => r.RequestId == request.RequestId)).Status == "Draft", "death certificate upload does not approve or complete request");
            check((await db.ExecutorAssignments.SingleAsync(a => a.VaultId == handover.Vault)).Status == "Active", "explicit Owner assignment remains unchanged");
        }
        check((await client.GetFromJsonAsync<PageDto<ExecutorAssignmentDto>>("/api/executor/assignments"))!.Items.Any(a => a.VaultId == handover.Vault), "Executor can discover persisted vault assignments");
        var assignmentPage = (await client.GetFromJsonAsync<PageDto<ExecutorAssignmentDto>>("/api/executor/assignments?page=1&pageSize=1"))!;
        check(assignmentPage.Page == 1 && assignmentPage.PageSize == 1 && assignmentPage.Items.Count == 1, "merged Executor assignments retain pagination metadata and page limit");
        await Expect(await client.GetAsync("/api/executor/assignments?page=0"), HttpStatusCode.BadRequest, "Executor assignments reject invalid page");
        await Expect(await client.GetAsync("/api/executor/assignments?pageSize=101"), HttpStatusCode.BadRequest, "Executor assignments reject excessive page size");
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent(ids["legal"].ToString()), "VerifierId"); var bytes = new ByteArrayContent("%PDF-1.7 legal evidence"u8.ToArray()); bytes.Headers.ContentType = new("application/pdf"); form.Add(bytes, "Evidence", "evidence.pdf");
            await Expect(await client.PostAsync($"/api/executor/handovers/{request.RequestId}/legal-verifications", form), HttpStatusCode.OK, "Executor submits assigned request for independent legal review");
        }
        As("legal2"); await Expect(await client.PostAsJsonAsync($"/api/legal/handovers/{request.RequestId}/approve", new { comment = "Unauthorized" }), HttpStatusCode.NotFound, "legal reviewer cannot approve another reviewer's case");
        await Expect(await client.GetAsync($"/api/legal/handovers/{request.RequestId}/owner-status"), HttpStatusCode.NotFound, "unassigned legal reviewer cannot inspect Owner death status");
        await Expect(await client.GetAsync("/api/executor/handovers/overview"), HttpStatusCode.Forbidden, "LegalVerifier does not inherit Executor-wide overview permission");
        As("executor"); await Expect(await UploadCertificate(request.RequestId), HttpStatusCode.Conflict, "death certificate upload is locked while legal review is pending");
        As("legal");
        int deathDocument;
        await using (var db = new LegacyVaultDbContext(dbOptions)) deathDocument = (await db.HandoverDocuments.FirstAsync(d => d.RequestId == request.RequestId)).HandoverDocumentId;
        await Expect(await client.GetAsync($"/api/legal/handovers/{request.RequestId}/documents/{deathDocument}/content"), HttpStatusCode.OK, "assigned LegalVerifier can download death evidence without upload permission");
        check((await client.GetFromJsonAsync<DocumentDto[]>($"/api/legal/handovers/{request.RequestId}/documents"))!.Any(d => d.Purpose == "DeathCertificate"), "LegalVerifier sees evidence types for the assigned review");
        As("legal"); await Expect(await client.PostAsJsonAsync($"/api/legal/handovers/{request.RequestId}/request-additional-information", new { comment = "Need more evidence" }), HttpStatusCode.OK, "legal requests additional information");
        await Expect(await client.PostAsJsonAsync($"/api/legal/handovers/{request.RequestId}/approve", new { comment = "Premature" }), HttpStatusCode.Conflict, "legal cannot approve before evidence resubmission");
        As("executor");
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent(ids["legal"].ToString()), "VerifierId"); var bytes = new ByteArrayContent("%PDF-1.7 additional evidence"u8.ToArray()); bytes.Headers.ContentType = new("application/pdf"); form.Add(bytes, "Evidence", "additional.pdf");
            await Expect(await client.PostAsync($"/api/executor/handovers/{request.RequestId}/legal-verifications", form), HttpStatusCode.OK, "Executor resubmits additional evidence");
        }
        As("legal");
        string assetPath;
        await using (var db = new LegacyVaultDbContext(dbOptions)) assetPath = (await db.AssetDocuments.SingleAsync(d => d.DocumentId == doc.DocumentId)).StoragePath;
        var documentStore = (FakeStore)app.Services.GetRequiredService<IDocumentStore>();
        var originalBytes = documentStore.Files[assetPath].ToArray(); documentStore.Files[assetPath][^1] ^= 1;
        await Expect(await client.PostAsJsonAsync($"/api/legal/handovers/{request.RequestId}/approve", new { comment = "Tampered asset evidence" }), HttpStatusCode.Conflict, "legal approval rejects tampered AES-GCM asset content");
        documentStore.Files[assetPath] = originalBytes;
        await using (var db = new LegacyVaultDbContext(dbOptions)) check((await db.HandoverRequests.SingleAsync(r => r.RequestId == request.RequestId)).Status == "Pending_Legal", "invalid evidence cannot partially approve handover");
        await Expect(await client.PostAsJsonAsync($"/api/legal/handovers/{request.RequestId}/approve", new { comment = "Evidence reviewed" }), HttpStatusCode.OK, "legal approves validated handover with review time");
        var confirmed = (await client.GetFromJsonAsync<OwnerLifeStatusDto>($"/api/legal/handovers/{request.RequestId}/owner-status"))!;
        check(confirmed.Status == "ConfirmedDeceased" && confirmed.VerifierId == ids["legal"] && confirmed.VerifiedAt is not null && confirmed.EvidenceRequestId == request.RequestId, "legal approval confirms Owner death using persisted evidence and reviewer provenance");
        check(confirmed.LastConfirmedAliveAt == lastLife.LastConfirmedAliveAt, "legal reviewer sees historical life confirmation without cancelling legal outcome");
        As("owner"); await Expect(await client.PostAsync("/api/owner/life-confirmation", null), HttpStatusCode.Conflict, "legally confirmed death cannot be reversed by life-confirmation button");
        As("legal");
        var history = (await client.GetFromJsonAsync<LegalHistoryDto[]>($"/api/legal/handovers/{request.RequestId}/history"))!;
        check(history.Length == 2 && history.Any(h => h.Status == "Additional_Info") && history.Any(h => h.Status == "Approved" && h.VerifierName == "legal"), "legal history retains additional-info and final decision with reviewer identity");
        await using (var db = new LegacyVaultDbContext(dbOptions))
            check((await db.Users.SingleAsync(u => u.UserId == ids["owner"])).Status == "Active" && await db.AuditLogs.AnyAsync(a => a.Action == "ConfirmOwnerDeceased" && a.EntityId == ids["owner"]), "death confirmation persists audit without changing account status");
        await Expect(await client.PostAsJsonAsync($"/api/legal/handovers/{request.RequestId}/reject", new { comment = "Too late" }), HttpStatusCode.Conflict, "review cannot overwrite an approved outcome");
        As("executor"); await Expect(await UploadCertificate(request.RequestId), HttpStatusCode.Conflict, "approved request cannot accept replacement death certificate");
        As("recipient"); await Expect(await client.GetAsync("/api/beneficiary/assets"), HttpStatusCode.Forbidden, "inheritance still requires separate beneficiary OTP after legal approval");
        var otpResponse = await client.PostAsJsonAsync("/api/beneficiary/otp/request", new { }); var inheritedOtp = (await otpResponse.Content.ReadFromJsonAsync<OtpChallenge>())!; var inheritedCode = ((FakeSender)app.Services.GetRequiredService<IOtpSender>()).Code;
        await Expect(await client.PostAsJsonAsync("/api/beneficiary/otp/verify", new { challengeId = inheritedOtp.ChallengeId, code = inheritedCode }), HttpStatusCode.OK, "beneficiary OTP opens inheritance only for current JWT session");
        var cases = (await client.GetFromJsonAsync<PageDto<CaseDto>>("/api/beneficiary/handovers"))!; var caseId = cases.Items.Single().HandoverId;
        await Expect(await client.PostAsJsonAsync($"/api/beneficiary/handovers/{caseId}/receipt", new { note = "Too soon" }), HttpStatusCode.Conflict, "receipt cannot precede completed handover");
        var beneficiaryAuthorization = client.DefaultRequestHeaders.Authorization;
        As("executor");
        await Expect(await client.PostAsJsonAsync($"/api/executor/handovers/{request.RequestId}/start", new { }), HttpStatusCode.NoContent, "Executor starts handover only after legal approval");
        async Task<HttpResponseMessage> CompleteRequest()
        {
            using var form = new MultipartFormDataContent(); var bytes = "signed handover completion"u8.ToArray();
            var file = new ByteArrayContent(bytes); file.Headers.ContentType = new("text/plain"); form.Add(file, "File", "completion.txt");
            form.Add(new ByteArrayContent(executorKey.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)), "Signature", "completion.sig");
            return await client.PostAsync($"/api/executor/handovers/{request.RequestId}/complete", form);
        }
        await Expect(await CompleteRequest(), HttpStatusCode.NoContent, "HTTP signed completion persists request, cases and notifications with real RSA validation");
        await Expect(await CompleteRequest(), HttpStatusCode.Conflict, "completed handover cannot be completed again");
        client.DefaultRequestHeaders.Authorization = beneficiaryAuthorization;
        await Expect(await client.PostAsJsonAsync($"/api/beneficiary/handovers/{caseId}/receipt", new { note = "Received" }), HttpStatusCode.OK, "beneficiary confirms completed receipt without claiming digital signature");
        await Expect(await client.PostAsJsonAsync($"/api/beneficiary/handovers/{caseId}/receipt", new { }), HttpStatusCode.Conflict, "receipt confirmation cannot repeat");
        check((await client.GetFromJsonAsync<PageDto<InheritanceDto>>("/api/beneficiary/assets"))!.Items.Count == 1, "received inheritance remains available");
        check((await client.GetFromJsonAsync<ReceiptDto>($"/api/beneficiary/handovers/{caseId}/receipt"))!.Status == "Confirmed", "beneficiary can read their persisted receipt");
        await Expect(await client.GetAsync($"/api/beneficiary/handovers/{caseId}/documents"), HttpStatusCode.OK, "beneficiary lists handover documents after OTP and legal approval");
        var notifications = (await client.GetFromJsonAsync<PageDto<NotificationDto>>("/api/notifications"))!; check(notifications.Items.Count > 0, "notifications persist for beneficiary workflow");
        As("other"); await Expect(await client.PostAsJsonAsync($"/api/notifications/{notifications.Items[0].NotificationId}/read", new { }), HttpStatusCode.NotFound, "notification access is limited to its recipient");
        var otherOtpResponse = await client.PostAsJsonAsync("/api/beneficiary/otp/request", new { }); var otherOtp = (await otherOtpResponse.Content.ReadFromJsonAsync<OtpChallenge>())!;
        await client.PostAsJsonAsync("/api/beneficiary/otp/verify", new { challengeId = otherOtp.ChallengeId, code = ((FakeSender)app.Services.GetRequiredService<IOtpSender>()).Code });
        await Expect(await client.PostAsJsonAsync($"/api/beneficiary/handovers/{caseId}/receipt", new { }), HttpStatusCode.NotFound, "verified beneficiary cannot confirm another person's receipt");
        await Expect(await client.GetAsync($"/api/beneficiary/assets/{handover.Asset}/documents/{doc.DocumentId}/content"), HttpStatusCode.NotFound, "verified beneficiary cannot download another person's inheritance");

        var eligibility = await CreateFixture("Executor eligibility"); await UploadAsset(eligibility.Asset);
        await using (var db = new LegacyVaultDbContext(dbOptions))
        {
            var role = await db.Roles.SingleAsync(r => r.RoleName == "Executor");
            foreach (var name in new[] { "owner", "recipient" }) (await db.Users.SingleAsync(u => u.UserId == ids[name])).Roles.Add(role);
            var executor = await db.Authentications.SingleAsync(a => a.UserId == ids["executor"]); executor.LockedUntil = DateTime.UtcNow.AddMinutes(10);
            var inactive = new User { FullName = "Inactive executor", Email = "inactive-executor@example.test", Status = "Inactive", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            inactive.Roles.Add(role); db.Users.Add(inactive); await db.SaveChangesAsync();
        }
        await Expect(await client.PostAsJsonAsync($"/api/owner/vaults/{eligibility.Vault}/handovers", new { }), HttpStatusCode.ServiceUnavailable, "no eligible Executor excludes owner, beneficiary, locked, inactive and legacy Staff accounts");
        await using (var db = new LegacyVaultDbContext(dbOptions))
        {
            check(!await db.ExecutorAssignments.AnyAsync(a => a.VaultId == eligibility.Vault) && !await db.HandoverRequests.AnyAsync(r => r.VaultId == eligibility.Vault), "no-candidate failure leaves no persisted assignment or request");
            (await db.Authentications.SingleAsync(a => a.UserId == ids["executor"])).LockedUntil = null; await db.SaveChangesAsync();
        }
        var selected = (await (await client.PostAsJsonAsync($"/api/owner/vaults/{eligibility.Vault}/handovers", new { })).Content.ReadFromJsonAsync<HandoverDto>())!;
        await using (var db = new LegacyVaultDbContext(dbOptions))
        {
            var selectedAssignment = await db.ExecutorAssignments.SingleAsync(a => a.VaultId == eligibility.Vault);
            check(selectedAssignment.ExecutorId == ids["executor"] && selectedAssignment.Status == "AutoAssigned", "random assignment persists the only eligible Executor using existing schema");
            check(await db.AuditLogs.AnyAsync(a => a.EntityId == eligibility.Vault && a.Action == "AutoAssignExecutor") && await db.Notifications.AnyAsync(n => n.UserId == ids["executor"]), "automatic assignment persists audit and notification");
        }
        await Expect(await client.PostAsJsonAsync($"/api/owner/vaults/{eligibility.Vault}/handovers", new { }), HttpStatusCode.Conflict, "retry cannot rerandomize or create a second assignment");
        await using (var db = new LegacyVaultDbContext(dbOptions))
        {
            foreach (var name in new[] { "owner", "recipient" }) { var u = await db.Users.Include(u => u.Roles).SingleAsync(u => u.UserId == ids[name]); u.Roles.Remove(u.Roles.Single(r => r.RoleName == "Executor")); }
            await db.SaveChangesAsync();
        }
        var legacyStaff = await CreateFixture("Legacy Staff assignment"); await UploadAsset(legacyStaff.Asset);
        await using (var db = new LegacyVaultDbContext(dbOptions))
        {
            db.ExecutorAssignments.Add(new() { VaultId = legacyStaff.Vault, ExecutorId = ids["staff"], Status = "Active", AssignedAt = DateTime.UtcNow }); await db.SaveChangesAsync();
        }
        await Expect(await client.PostAsJsonAsync($"/api/owner/vaults/{legacyStaff.Vault}/handovers", new { }), HttpStatusCode.Forbidden, "legacy Staff assignment is not silently treated as Executor or replaced by random selection");
        As("staff"); await Expect(await client.GetAsync("/api/executor/assignments"), HttpStatusCode.Forbidden, "legacy Staff DB role cannot discover Executor assignments");
        await Expect(await client.PostAsJsonAsync($"/api/executor/assignments/{legacyStaff.Vault}/handovers", new { }), HttpStatusCode.Forbidden, "legacy assigned Staff cannot initiate handover");
        var executorInitiated = await CreateFixture("Executor initiated"); await UploadAsset(executorInitiated.Asset);
        As("executor"); await Expect(await client.PostAsJsonAsync($"/api/executor/assignments/{executorInitiated.Vault}/handovers", new { }), HttpStatusCode.NotFound, "Executor cannot initiate an unassigned vault request");
        As("owner"); await client.PutAsJsonAsync($"/api/owner/vaults/{executorInitiated.Vault}/executor", new { executorId = ids["executor"] });
        As("executor"); await Expect(await client.PostAsJsonAsync($"/api/executor/assignments/{executorInitiated.Vault}/handovers", new { }), HttpStatusCode.Created, "previously assigned Executor can initiate a draft without needing Owner to return");
        await Expect(await client.PostAsJsonAsync($"/api/executor/assignments/{executorInitiated.Vault}/handovers", new { }), HttpStatusCode.Conflict, "Executor initiation retry cannot duplicate a protected handover");
        var rollback = await CreateFixture("Rollback random assignment"); await UploadAsset(rollback.Asset);
        await using (var db = new LegacyVaultDbContext(dbOptions)) await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_request BEFORE INSERT ON HANDOVER_REQUESTS BEGIN SELECT RAISE(ABORT, 'Injected failure'); END;");
        await Expect(await client.PostAsJsonAsync($"/api/owner/vaults/{rollback.Vault}/handovers", new { }), HttpStatusCode.InternalServerError, "request save failure is not reported as successful random assignment");
        await using (var db = new LegacyVaultDbContext(dbOptions))
        {
            check(!await db.ExecutorAssignments.AnyAsync(a => a.VaultId == rollback.Vault) && !await db.HandoverRequests.AnyAsync(r => r.VaultId == rollback.Vault), "failed request rolls back random assignment and cases on relational database");
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_request;");
        }
        var competing = new DbContextOptionsBuilder<LegacyVaultDbContext>().UseSqlite(connection).AddInterceptors(new AssignmentRace(dbOptions, rollback.Vault, ids["owner"], app.Services)).Options;
        await using (var db = new LegacyVaultDbContext(competing))
        {
            try { await Service(db, app.Services).CreateRequest(ids["owner"], rollback.Vault, default); throw new Exception("Expected assignment concurrency conflict"); }
            catch (RepositoryConflictException) { check(true, "concurrent random assignment rejected by vault version after competing SQL commit"); }
        }
        await using (var db = new LegacyVaultDbContext(dbOptions)) check(await db.ExecutorAssignments.CountAsync(a => a.VaultId == rollback.Vault) == 1 && await db.HandoverRequests.CountAsync(r => r.VaultId == rollback.Vault) == 1, "concurrent creation leaves one persisted assignment and one request");
        var revokeDocuments = await CreateFixture("Document revoke"); var revokeDoc = await UploadAsset(revokeDocuments.Asset);
        await Expect(await client.PostAsJsonAsync($"/api/owner/assets/{revokeDocuments.Asset}/documents/{revokeDoc.DocumentId}/revoke", new { }), HttpStatusCode.NoContent, "owner revokes unprocessed document without deleting history");
        await Expect(await client.GetAsync($"/api/owner/assets/{revokeDocuments.Asset}/documents/{revokeDoc.DocumentId}/content"), HttpStatusCode.NotFound, "revoked document is not downloadable");
        var rejectedFlow = await CreateFixture("Rejected flow"); await UploadAsset(rejectedFlow.Asset);
        var unrelatedResponse = await client.PostAsJsonAsync("/api/owner/assets", new { vaultId = rejectedFlow.Vault, name = "Excluded archived asset", type = "Document" });
        var unrelated = (await unrelatedResponse.Content.ReadFromJsonAsync<AssetDto>())!;
        await client.PostAsJsonAsync($"/api/owner/assets/{unrelated.AssetId}/beneficiaries", new { email = "recipient@example.test", allocation = 100 });
        var unrelatedDocument = await UploadAsset(unrelated.AssetId);
        await client.PostAsJsonAsync($"/api/owner/assets/{unrelated.AssetId}/revoke", new { });
        var rejectedRequest = (await (await client.PostAsJsonAsync($"/api/owner/vaults/{rejectedFlow.Vault}/handovers", new { })).Content.ReadFromJsonAsync<HandoverDto>())!;
        As("executor");
        check((await client.GetFromJsonAsync<PageDto<HandoverDto>>("/api/executor/handovers"))!.Items.Any(r => r.RequestId == rejectedRequest.RequestId), "AutoAssigned requests appear in executor list consistently with detail");
        var firstRequestPage = (await client.GetFromJsonAsync<PageDto<HandoverDto>>("/api/executor/handovers?page=1&pageSize=1"))!;
        var secondRequestPage = (await client.GetFromJsonAsync<PageDto<HandoverDto>>("/api/executor/handovers?page=2&pageSize=1"))!;
        check(firstRequestPage.Items.Count == 1 && secondRequestPage.Items.Count == 1 && firstRequestPage.Items[0].RequestId != secondRequestPage.Items[0].RequestId && secondRequestPage.Page == 2, "merged handover pagination returns distinct assigned records on consecutive pages");
        await Expect(await UploadCertificate(rejectedRequest.RequestId), HttpStatusCode.OK, "randomly assigned Executor can upload death certificate");
        await Expect(await client.PostAsJsonAsync($"/api/executor/handovers/{rejectedRequest.RequestId}/start", new { }), HttpStatusCode.Conflict, "Executor cannot start before legal approval");
        using (var upload = new MultipartFormDataContent())
        {
            var bytes = new ByteArrayContent("supplemental document"u8.ToArray()); bytes.Headers.ContentType = new("text/plain"); upload.Add(bytes, "File", "supplemental.txt");
            await Expect(await client.PostAsync($"/api/executor/handovers/{rejectedRequest.RequestId}/documents", upload), HttpStatusCode.OK, "assigned operator uploads real encrypted handover document before submission");
        }
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent(ids["legal"].ToString()), "VerifierId"); var bytes = new ByteArrayContent("%PDF-1.7 review evidence"u8.ToArray()); bytes.Headers.ContentType = new("application/pdf"); form.Add(bytes, "Evidence", "evidence.pdf");
            await Expect(await client.PostAsync($"/api/executor/handovers/{rejectedRequest.RequestId}/legal-verifications", form), HttpStatusCode.OK, "new autoassigned request submits legal review");
        }
        As("legal");
        await Expect(await client.GetAsync($"/api/legal/handovers/{rejectedRequest.RequestId}/asset-documents/{unrelatedDocument.DocumentId}/content"), HttpStatusCode.NotFound, "LegalVerifier cannot read unrelated archived asset in same vault");
        await Expect(await client.PostAsJsonAsync($"/api/legal/handovers/{rejectedRequest.RequestId}/reject", new { comment = "Evidence insufficient" }), HttpStatusCode.OK, "LegalVerifier persists independent rejection");
        As("owner");
        await Expect(await client.PostAsJsonAsync($"/api/owner/handovers/{rejectedRequest.RequestId}/cancel", new { }), HttpStatusCode.NoContent, "Owner can cancel rejected request to repair evidence without deleting history");
        await Expect(await client.PutAsJsonAsync($"/api/owner/vaults/{rejectedFlow.Vault}", new { name = "Corrected after rejection" }), HttpStatusCode.OK, "cancelled rejection releases mutation gate while completed records remain protected");
        async Task<HandoverDto> NewOtherOwnerRequest(string name)
        {
            var fixture = await CreateFixture(name, "other"); await UploadAsset(fixture.Asset);
            var result = await client.PostAsJsonAsync($"/api/owner/vaults/{fixture.Vault}/handovers", new { });
            await Expect(result, HttpStatusCode.Created, "different Owner creates persisted handover for workflow review");
            return (await result.Content.ReadFromJsonAsync<HandoverDto>())!;
        }
        async Task SubmitReview(int id)
        {
            using var form = new MultipartFormDataContent(); form.Add(new StringContent(ids["legal"].ToString()), "VerifierId");
            var bytes = new ByteArrayContent("%PDF-1.7 legal review evidence"u8.ToArray()); bytes.Headers.ContentType = new("application/pdf"); form.Add(bytes, "Evidence", "legal.pdf");
            await Expect(await client.PostAsync($"/api/executor/handovers/{id}/legal-verifications", form), HttpStatusCode.OK, "assigned Executor submits death report for legal review");
        }
        var otherRequest = await NewOtherOwnerRequest("Another Owner");
        As("executor");
        var allOwners = (await client.GetFromJsonAsync<PageDto<HandoverOverviewDto>>("/api/executor/handovers/overview"))!;
        check(allOwners.Items.Any(x => x.Owner.UserId == ids["owner"]) && allOwners.Items.Any(x => x.Owner.UserId == ids["other"]), "global overview contains handovers from different Owners");
        check((await client.GetFromJsonAsync<OwnerLifeStatusDto>($"/api/executor/handovers/{otherRequest.RequestId}/owner-status"))!.Status == "NotConfirmed", "confirmed death of one Owner never applies to another Owner");
        await Expect(await UploadCertificate(otherRequest.RequestId), HttpStatusCode.OK, "different Owner death report requires own linked evidence");
        await SubmitReview(otherRequest.RequestId);
        As("legal"); await Expect(await client.PostAsJsonAsync($"/api/legal/handovers/{otherRequest.RequestId}/reject", new { comment = "Certificate not sufficient" }), HttpStatusCode.OK, "legal rejection is persisted for separate Owner");
        check((await client.GetFromJsonAsync<OwnerLifeStatusDto>($"/api/legal/handovers/{otherRequest.RequestId}/owner-status"))!.Status == "Rejected", "rejected death evidence does not mark Owner confirmed deceased");
        As("other"); await Expect(await client.PostAsync("/api/owner/life-confirmation", null), HttpStatusCode.OK, "rejected death report does not permanently block Owner life confirmation");
        As("executor"); await Expect(await client.PutAsJsonAsync($"/api/executor/handovers/{otherRequest.RequestId}/owner-status", new { status = "ConfirmedDeceased" }), HttpStatusCode.MethodNotAllowed, "Executor cannot force confirmed death through arbitrary status input");
        var raceRequest = await NewOtherOwnerRequest("Concurrent legal review");
        As("executor"); await UploadCertificate(raceRequest.RequestId); await SubmitReview(raceRequest.RequestId);
        var racingReview = new DbContextOptionsBuilder<LegacyVaultDbContext>().UseSqlite(connection)
            .AddInterceptors(new LegalReviewRace(dbOptions, raceRequest.RequestId, ids["legal"], app.Services)).Options;
        await using (var db = new LegacyVaultDbContext(racingReview))
        {
            try { await Service(db, app.Services).Review(ids["legal"], raceRequest.RequestId, "Approved", new("Competing approval"), default); throw new Exception("Expected legal review concurrency conflict"); }
            catch (RepositoryConflictException) { check(true, "competing legal decisions cannot both commit after separate DbContext SQL writes"); }
        }
        await using (var db = new LegacyVaultDbContext(dbOptions))
        {
            check((await db.HandoverRequests.SingleAsync(r => r.RequestId == raceRequest.RequestId)).Status == "Rejected" &&
                (await db.HandoverCases.SingleAsync(c => c.RequestId == raceRequest.RequestId)).Status == "Pending" &&
                !await db.AuditLogs.AnyAsync(a => a.Action == "ConfirmOwnerDeceased" && a.EntityId == ids["other"]), "losing approval rolls back Owner death audit and case transitions");
        }
        await app.StopAsync();
    }
    private static ManagementService Service(LegacyVaultDbContext db, IServiceProvider services) => new(new VaultRepository(db), new WorkflowRepository(db), services.GetRequiredService<IDocumentStore>(), services.GetRequiredService<DocumentProtection>());
    private sealed class FactorClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan amount) => now += amount;
    }
    private sealed class AssignmentRace(DbContextOptions<LegacyVaultDbContext> options, int vault, int owner, IServiceProvider services) : SaveChangesInterceptor
    {
        private bool ran;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (!ran) { ran = true; await using var other = new LegacyVaultDbContext(options); await Service(other, services).CreateRequest(owner, vault, ct); }
            return result;
        }
    }
    private sealed class LegalReviewRace(DbContextOptions<LegacyVaultDbContext> options, int request, int verifier, IServiceProvider services) : SaveChangesInterceptor
    {
        private bool ran;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (!ran) { ran = true; await using var other = new LegacyVaultDbContext(options); await Service(other, services).Review(verifier, request, "Rejected", new("Winning independent review"), ct); }
            return result;
        }
    }
    private sealed class AllocationRace(DbContextOptions<LegacyVaultDbContext> options, int owner, int asset, int assignment, IServiceProvider services) : SaveChangesInterceptor
    {
        private bool ran;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (!ran) { ran = true; await using var other = new LegacyVaultDbContext(options); await Service(other, services).ChangeAllocation(owner, asset, assignment, new(60), ct); }
            return result;
        }
    }
}
