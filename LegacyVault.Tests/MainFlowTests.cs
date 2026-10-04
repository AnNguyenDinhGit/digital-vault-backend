using System.Net;
using System.Net.Http.Json;
using LegacyVault.API.Controllers;
using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Security;
using LegacyVault.BLL.Services;
using LegacyVault.DAL.Context;
using LegacyVault.DAL.Repositories;
using LegacyVault.DAL.Storage;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

internal static class MainFlowTests
{
    public static async Task Run(Action<bool, string> check)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction("sysdatetime", () => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fffffff"));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(OwnerController).Assembly);
        builder.Services.AddDbContext<LegacyVaultDbContext>(o => o.UseSqlite(connection));
        builder.Services.AddScoped<IVaultRepository, VaultRepository>();
        builder.Services.AddScoped<IdentityService>();
        builder.Services.AddScoped<VaultService>();
        builder.Services.AddSingleton(new SecurityOptions());
        builder.Services.AddSingleton<DocumentProtection>();
        builder.Services.AddSingleton<IDocumentStore, FakeStore>();
        builder.Services.AddSingleton<IOtpSender, FakeSender>();
        builder.Services.AddSingleton<OtpService>();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
        {
            // Loopback test host uses HTTP; production keeps SecurePolicy.Always and HTTPS.
            o.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
            o.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
        });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(o =>
        {
            foreach (var name in new[] { "login", "otp" })
                o.AddPolicy(name, _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test"));
        });
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            try { await next(context); }
            catch (WorkflowException ex) { context.Response.StatusCode = ex.StatusCode; await Microsoft.AspNetCore.Http.HttpResponseJsonExtensions.WriteAsJsonAsync(context.Response, new { status = ex.StatusCode, detail = ex.Message }); }
        });
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.MapControllers();
        await using (var scope = app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<LegacyVaultDbContext>().Database.EnsureCreatedAsync();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(address) };
        client.DefaultRequestHeaders.Add("X-Vault-Request", "1");
        check((await client.PostAsJsonAsync("/api/owner/vaults", new { name = "Unauthenticated" })).StatusCode == HttpStatusCode.Unauthorized, "HTTP vault creation requires login");
        check((await client.PostAsJsonAsync("/api/auth/register", new { fullName = "Test", email = "escalate@example.test", password = "Registration123!", confirmPassword = "Registration123!", role = "Beneficiary" })).StatusCode == HttpStatusCode.BadRequest, "HTTP public registration rejects role selection");
        var registration = await client.PostAsJsonAsync("/api/auth/register", new { fullName = "Main Flow Owner", email = "mainflow@example.test", password = "Registration123!", confirmPassword = "Registration123!", phone = "0901234567" });
        check(registration.StatusCode == HttpStatusCode.Created && (await registration.Content.ReadFromJsonAsync<RegistrationDto>())!.Roles.SequenceEqual(new[] { VaultService.OwnerRole }), "HTTP main flow registration creates an owner");
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "mainflow@example.test", password = "Registration123!" });
        if (login.StatusCode != HttpStatusCode.OK) throw new Exception($"Main flow login failed: HTTP {(int)login.StatusCode}.");
        var account = (await login.Content.ReadFromJsonAsync<LoginDto>())!;
        check(login.StatusCode == HttpStatusCode.OK && handler.CookieContainer.GetCookies(client.BaseAddress!).Count > 0, "HTTP main flow login stores a session cookie");
        var vaultResponse = await client.PostAsJsonAsync("/api/owner/vaults", new { name = " My Vault ", description = "Personal assets" });
        var vault = (await vaultResponse.Content.ReadFromJsonAsync<VaultDto>())!;
        check(vaultResponse.StatusCode == HttpStatusCode.Created && vault.VaultId > 0 && vault.Name == "My Vault" && vault.Status == "Active" && vaultResponse.Headers.Location is not null, "HTTP main flow creates a vault with a usable Location");
        check((await client.GetAsync(vaultResponse.Headers.Location)).StatusCode == HttpStatusCode.OK, "created vault Location resolves to owner detail");
        check((await client.GetFromJsonAsync<VaultDto[]>("/api/owner/vaults"))!.Single().VaultId == vault.VaultId, "HTTP owner can list created vault");
        var assetResponse = await client.PostAsJsonAsync("/api/owner/assets", new { vaultId = vault.VaultId, name = " Savings Account ", type = "BankAccount", description = "Asset description" });
        var asset = (await assetResponse.Content.ReadFromJsonAsync<AssetDto>())!;
        check(assetResponse.StatusCode == HttpStatusCode.Created && asset.AssetId > 0 && asset.Name == "Savings Account" && asset.Status == "Active" && asset.Documents.Count == 0 && assetResponse.Headers.Location is not null, "HTTP main flow creates asset inside owned vault");
        check((await client.GetFromJsonAsync<AssetDto>(assetResponse.Headers.Location))!.AssetId == asset.AssetId, "created asset Location resolves to asset detail");
        check((await client.GetFromJsonAsync<AssetDto[]>("/api/owner/assets"))!.Single().AssetId == asset.AssetId, "HTTP main flow lists persisted asset");
        check((await client.PostAsJsonAsync("/api/beneficiary/otp/request", new { })).StatusCode == HttpStatusCode.Forbidden, "publicly registered owner does not silently gain beneficiary role");
        check((await client.PostAsJsonAsync("/api/owner/assets", new { vaultId = vault.VaultId, name = " ", type = "BankAccount" })).StatusCode == HttpStatusCode.BadRequest, "HTTP asset creation validates required name");
        check((await client.PostAsJsonAsync("/api/owner/assets", new { vaultId = vault.VaultId, name = "Example", type = new string('X', 51) })).StatusCode == HttpStatusCode.BadRequest, "HTTP asset type respects scaffolded length");
        check((await client.PostAsJsonAsync("/api/owner/vaults", new { name = "Spoof", ownerId = 999 })).StatusCode == HttpStatusCode.BadRequest, "HTTP caller cannot supply another ownerId");
        using var otherHandler = new HttpClientHandler { CookieContainer = new CookieContainer() };
        using var other = new HttpClient(otherHandler) { BaseAddress = client.BaseAddress };
        other.DefaultRequestHeaders.Add("X-Vault-Request", "1");
        await other.PostAsJsonAsync("/api/auth/register", new { fullName = "Other Owner", email = "otherflow@example.test", password = "Registration123!", confirmPassword = "Registration123!" });
        await other.PostAsJsonAsync("/api/auth/login", new { email = "otherflow@example.test", password = "Registration123!" });
        check((await other.PostAsJsonAsync("/api/owner/assets", new { vaultId = vault.VaultId, name = "Unauthorized", type = "Other" })).StatusCode == HttpStatusCode.NotFound, "another owner cannot create an asset in this vault");
        check((await other.GetAsync($"/api/owner/assets/{asset.AssetId}")).StatusCode == HttpStatusCode.NotFound && (await other.GetFromJsonAsync<AssetDto[]>("/api/owner/assets"))!.Length == 0, "another owner cannot read or list this asset");
        var selectionPath = $"/api/owner/assets/{asset.AssetId}/beneficiaries";
        check((await other.PostAsJsonAsync(selectionPath, new { email = "mainflow@example.test", allocation = 100 })).StatusCode == HttpStatusCode.NotFound, "only the asset owner can designate a beneficiary");
        check((await client.PostAsJsonAsync(selectionPath, new { email = "mainflow@example.test", allocation = 100 })).StatusCode == HttpStatusCode.BadRequest, "beneficiary selection rejects self-inheritance");
        check((await client.PostAsJsonAsync(selectionPath, new { email = "missing@example.test", allocation = 100 })).StatusCode == HttpStatusCode.NotFound, "beneficiary must have an existing active account");
        check((await client.PostAsJsonAsync(selectionPath, new { email = "otherflow@example.test", allocation = 0 })).StatusCode == HttpStatusCode.BadRequest, "beneficiary allocation must be positive");
        check((await client.PostAsJsonAsync(selectionPath, new { email = "otherflow@example.test", allocation = 1.001m })).StatusCode == HttpStatusCode.BadRequest, "beneficiary allocation respects decimal(5,2) precision");
        check((await client.PostAsJsonAsync(selectionPath, new { email = "otherflow@example.test", allocation = 60, role = "Admin" })).StatusCode == HttpStatusCode.BadRequest, "beneficiary selection cannot grant caller-supplied privileged role");
        var chosenResponse = await client.PostAsJsonAsync(selectionPath, new { email = " OTHERFLOW@EXAMPLE.TEST ", allocation = 60 });
        var chosen = (await chosenResponse.Content.ReadFromJsonAsync<BeneficiaryDto>())!;
        check(chosenResponse.StatusCode == HttpStatusCode.Created && chosen.Allocation == 60 && chosen.Status == "Active", "owner can select a registered owner as beneficiary by normalized email");
        check((await client.GetFromJsonAsync<BeneficiaryDto[]>($"/api/owner/beneficiaries?assetId={asset.AssetId}"))!.Single().BeneficiaryId == chosen.BeneficiaryId, "owner beneficiary list returns the selected user ID for subsequent upload");
        check((await client.PostAsJsonAsync(selectionPath, new { email = "otherflow@example.test", allocation = 10 })).StatusCode == HttpStatusCode.Conflict, "duplicate active beneficiary assignment is rejected");
        var otpResponse = await other.PostAsJsonAsync("/api/beneficiary/otp/request", new { });
        var challenge = (await otpResponse.Content.ReadFromJsonAsync<OtpChallenge>())!;
        check(otpResponse.StatusCode == HttpStatusCode.OK, "designated user can request beneficiary OTP without losing owner session");
        var code = ((FakeSender)app.Services.GetRequiredService<IOtpSender>()).Code;
        check((await other.PostAsJsonAsync("/api/beneficiary/otp/verify", new { challengeId = challenge.ChallengeId, code })).StatusCode == HttpStatusCode.OK, "designated beneficiary verifies identity using OTP");
        check((await other.GetFromJsonAsync<InheritanceDto[]>("/api/beneficiary/assets"))!.Length == 0, "designation and OTP do not prematurely release assets without legal handover");
        check((await other.GetAsync($"/api/owner/assets/{asset.AssetId}")).StatusCode == HttpStatusCode.NotFound, "beneficiary role does not grant ownership of someone else's asset");
        await client.PostAsJsonAsync("/api/auth/register", new { fullName = "Third User", email = "thirdflow@example.test", password = "Registration123!", confirmPassword = "Registration123!" });
        check((await client.PostAsJsonAsync(selectionPath, new { email = "thirdflow@example.test", allocation = 50 })).StatusCode == HttpStatusCode.Conflict, "combined beneficiary allocation cannot exceed 100 percent");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LegacyVaultDbContext>();
            var rejected = await db.Users.Include(x => x.Roles).SingleAsync(x => x.Email == "thirdflow@example.test");
            check(rejected.Roles.All(x => x.RoleName != VaultService.BeneficiaryRole), "failed allocation validation does not grant beneficiary role");
            rejected.Status = "Inactive";
            await db.SaveChangesAsync();
        }
        check((await client.PostAsJsonAsync(selectionPath, new { email = "thirdflow@example.test", allocation = 40 })).StatusCode == HttpStatusCode.NotFound, "inactive recipient cannot be designated");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LegacyVaultDbContext>();
            (await db.Users.SingleAsync(x => x.Email == "thirdflow@example.test")).Status = "Active";
            await db.SaveChangesAsync();
        }
        check((await client.PostAsJsonAsync(selectionPath, new { email = "thirdflow@example.test", allocation = 40 })).StatusCode == HttpStatusCode.Created, "second beneficiary can receive the remaining allocation");
        var allocations = (await client.GetFromJsonAsync<BeneficiaryDto[]>($"/api/owner/beneficiaries?assetId={asset.AssetId}"))!;
        check(allocations.Length == 2 && allocations.Sum(x => x.Allocation) == 100, "multiple beneficiary assignments persist with a total allocation of 100 percent");
        var secondAssetResponse = await client.PostAsJsonAsync("/api/owner/assets", new { vaultId = vault.VaultId, name = "Second Asset", type = "Other" });
        var secondAsset = (await secondAssetResponse.Content.ReadFromJsonAsync<AssetDto>())!;
        check((await client.PostAsJsonAsync($"/api/owner/assets/{secondAsset.AssetId}/beneficiaries", new { email = "otherflow@example.test", allocation = 100 })).StatusCode == HttpStatusCode.Created, "same beneficiary can be designated for a second asset");
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LegacyVaultDbContext>();
            var storedVault = await db.DigitalVaults.SingleAsync();
            var storedAsset = await db.DigitalAssets.SingleAsync(x => x.AssetId == asset.AssetId);
            check(storedVault.OwnerId == account.UserId && storedAsset.VaultId == storedVault.VaultId && await db.Users.CountAsync() == 3, "main flow persists actual USERS to DIGITAL_VAULTS to DIGITAL_ASSETS foreign keys");
            var recipient = await db.Users.Include(x => x.Roles).SingleAsync(x => x.UserId == chosen.BeneficiaryId);
            check(recipient.Roles.Select(x => x.RoleName).Order().SequenceEqual(new[] { "Beneficiary", "Owner" }) && await db.Roles.CountAsync(x => x.RoleName == VaultService.BeneficiaryRole) == 1, "designation persists both roles and reuses the single beneficiary role");
            storedVault.Status = "Locked";
            await db.SaveChangesAsync();
        }
        check((await client.PostAsJsonAsync("/api/owner/assets", new { vaultId = vault.VaultId, name = "Blocked", type = "Other" })).StatusCode == HttpStatusCode.Conflict, "inactive vault rejects new assets");
        check((await client.PostAsJsonAsync($"/api/owner/assets/{secondAsset.AssetId}/beneficiaries", new { email = "thirdflow@example.test", allocation = 1 })).StatusCode == HttpStatusCode.Conflict, "inactive vault rejects beneficiary changes");
        check((await client.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode == HttpStatusCode.NoContent && (await client.GetAsync("/api/owner/assets")).StatusCode == HttpStatusCode.Unauthorized, "HTTP main flow logout removes access");
        await app.StopAsync();
    }
}
