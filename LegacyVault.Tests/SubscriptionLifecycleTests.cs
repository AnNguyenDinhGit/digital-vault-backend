using System.Net;
using System.Net.Http.Json;
using LegacyVault.API.Authentication;
using LegacyVault.API.Controllers;
using LegacyVault.API.Filters;
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
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public static class SubscriptionLifecycleTests
{
    public static async Task Run(Action<bool, string> check)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction("sysdatetime", () => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fffffff"));

        var dbOptions = new DbContextOptionsBuilder<LegacyVaultDbContext>()
            .UseSqlite(connection)
            .Options;

        // 1. Setup DB schema and seed plans
        SubscriptionPlan freePlan;
        SubscriptionPlan basicPlan;
        SubscriptionPlan proPlan;

        await using (var db = new LegacyVaultDbContext(dbOptions))
        {
            await db.Database.EnsureCreatedAsync();

            freePlan = new SubscriptionPlan
            {
                PlanName = "Free Tier",
                Price = 0m,
                MaxVaults = 1,
                MaxStorageBytes = 1_000_000,
                DurationDays = 36500,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            basicPlan = new SubscriptionPlan
            {
                PlanName = "Basic Tier",
                Price = 100_000m,
                MaxVaults = 5,
                MaxStorageBytes = 10_000_000,
                DurationDays = 30,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            proPlan = new SubscriptionPlan
            {
                PlanName = "Pro Tier",
                Price = 250_000m,
                MaxVaults = 20,
                MaxStorageBytes = 50_000_000,
                DurationDays = 30,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            db.SubscriptionPlans.AddRange(freePlan, basicPlan, proPlan);
            await db.SaveChangesAsync();
        }

        // 2. Test Repository and VnPayService quote calculations directly
        int ownerUserId;
        await using (var db = new LegacyVaultDbContext(dbOptions))
        {
            var repo = new PaymentRepository(db);
            var vnpay = new VnPayService(repo, new() { TmnCode = "TESTCODE", HashSecret = "TESTSECRET1234567890", ReturnUrl = "https://localhost/return" });

            // Create test user
            var user = new User { Email = "owner@test.com", FullName = "Owner User", Status = "Active", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            ownerUserId = user.UserId;

            // A. User without paid subscription
            var quoteNew = await vnpay.GetQuote(user.UserId, basicPlan.PlanId, default);
            check(quoteNew.Allowed && quoteNew.ChangeType == "New" && quoteNew.AmountDue == 100_000m, "quote for new user allows full price and changeType New");

            // Assign active paid subscription starting now
            var now = DateTime.UtcNow;
            var activePaid = new UserSubscription
            {
                UserId = user.UserId,
                PlanId = basicPlan.PlanId,
                StartDate = now,
                EndDate = now.AddDays(30),
                Status = "Active"
            };
            db.UserSubscriptions.Add(activePaid);
            await db.SaveChangesAsync();

            // B. Window 1: now <= start + 3 days
            // Upgrade to higher price (Pro tier)
            var quoteUpgrade = await vnpay.GetQuote(user.UserId, proPlan.PlanId, default);
            check(quoteUpgrade.Allowed && quoteUpgrade.ChangeType == "Upgrade" && quoteUpgrade.AmountDue == 150_000m, "quote in window 1 allows upgrade with price difference");

            // Same price / lower price in window 1
            var quoteSame = await vnpay.GetQuote(user.UserId, basicPlan.PlanId, default);
            check(!quoteSame.Allowed && quoteSame.ReasonCode == "UPGRADE_PRICE_TOO_LOW", "quote in window 1 rejects same or lower price with UPGRADE_PRICE_TOO_LOW");

            var quoteFreeInW1 = await vnpay.GetQuote(user.UserId, freePlan.PlanId, default);
            check(!quoteFreeInW1.Allowed && quoteFreeInW1.ReasonCode == "UPGRADE_PRICE_TOO_LOW", "quote in window 1 rejects free plan");

            // C. Window 2: start + 3 days < now < end - 5 days
            // Shift StartDate back by 10 days (now = day 10, end = day 30, window2 is [3, 25])
            activePaid.StartDate = now.AddDays(-10);
            activePaid.EndDate = now.AddDays(20);
            await db.SaveChangesAsync();

            var quoteWindow2 = await vnpay.GetQuote(user.UserId, proPlan.PlanId, default);
            check(!quoteWindow2.Allowed && quoteWindow2.ReasonCode == "PLAN_CHANGE_LOCKED" && quoteWindow2.ReasonMessage!.Contains("Chỉ đổi gói được từ"), "quote in window 2 blocks plan change with PLAN_CHANGE_LOCKED and renewal date message");

            // D. Window 3: end - 5 days <= now < end
            // Shift StartDate back by 26 days (now = day 26, end = day 30, window3 is [25, 30))
            activePaid.StartDate = now.AddDays(-26);
            activePaid.EndDate = now.AddDays(4);
            await db.SaveChangesAsync();

            var quoteWindow3Scheduled = await vnpay.GetQuote(user.UserId, proPlan.PlanId, default);
            check(quoteWindow3Scheduled.Allowed && quoteWindow3Scheduled.ChangeType == "Scheduled" && quoteWindow3Scheduled.AmountDue == 250_000m && quoteWindow3Scheduled.StartsAt == activePaid.EndDate, "quote in window 3 allows scheduled upgrade with full price and startsAt = end of current plan");

            var quoteWindow3Renew = await vnpay.GetQuote(user.UserId, basicPlan.PlanId, default);
            check(quoteWindow3Renew.Allowed && quoteWindow3Renew.ChangeType == "Renew" && quoteWindow3Renew.AmountDue == 100_000m && quoteWindow3Renew.StartsAt == activePaid.EndDate, "quote in window 3 allows renewal with full price and startsAt = end of current plan");

            // Add a scheduled subscription
            var scheduledSub = new UserSubscription
            {
                UserId = user.UserId,
                PlanId = proPlan.PlanId,
                StartDate = activePaid.EndDate,
                EndDate = activePaid.EndDate.AddDays(30),
                Status = "Scheduled"
            };
            db.UserSubscriptions.Add(scheduledSub);
            await db.SaveChangesAsync();

            // Attempting another quote when scheduled exists should be blocked
            var quoteBlockedByScheduled = await vnpay.GetQuote(user.UserId, basicPlan.PlanId, default);
            check(!quoteBlockedByScheduled.Allowed && quoteBlockedByScheduled.ReasonCode == "SCHEDULED_PLAN_EXISTS", "quote blocks when a scheduled subscription already exists");

            // E. Auto-activation of scheduled subscription
            // Simulate time advancing past activePaid.EndDate
            scheduledSub.StartDate = now.AddMinutes(-5); // Already due
            activePaid.EndDate = now.AddMinutes(-5); // Expired
            await db.SaveChangesAsync();

            await repo.ActivateScheduledSubscriptionsIfDue(user.UserId, DateTime.UtcNow, default);
            check(activePaid.Status == "Expired" && scheduledSub.Status == "Active", "scheduled subscription automatically activates when startDate arrives and old subscription expires");

            // Clean up subscriptions for read-only test
            scheduledSub.Status = "Expired";
            scheduledSub.EndDate = now.AddDays(-1);
            await db.SaveChangesAsync();

            // F. Read-only mode when paid subscription expired and no scheduled plan
            var isReadOnly = await repo.IsReadOnly(user.UserId, DateTime.UtcNow, default);
            check(isReadOnly, "account is read-only when paid subscription has expired and no scheduled subscription exists");

            var currentSubDto = await vnpay.CurrentSubscription(user.UserId, default);
            check(currentSubDto.IsReadOnly, "CurrentSubscription DTO reflects isReadOnly = true");

            // G. Select-free: blocked when active paid subscription exists, allowed when expired
            // If active paid exists:
            var newActivePaid = new UserSubscription
            {
                UserId = user.UserId,
                PlanId = basicPlan.PlanId,
                StartDate = now,
                EndDate = now.AddDays(30),
                Status = "Active"
            };
            db.UserSubscriptions.Add(newActivePaid);
            await db.SaveChangesAsync();

            var caughtConflict = false;
            try { await vnpay.SelectFreePlan(user.UserId, freePlan.PlanId, default); }
            catch (WorkflowException ex) when (ex.StatusCode == 409 && ex.ErrorCode == "CANNOT_CANCEL_PAID_SUBSCRIPTION") { caughtConflict = true; }
            check(caughtConflict, "select-free is blocked with 409 when active paid subscription exists");

            // Mark paid as expired: select-free should succeed
            newActivePaid.Status = "Expired";
            newActivePaid.EndDate = now.AddDays(-1);
            await db.SaveChangesAsync();

            var freeResult = await vnpay.SelectFreePlan(user.UserId, freePlan.PlanId, default);
            check(freeResult.HasActiveSubscription && freeResult.Subscription!.Plan.Price == 0m && !freeResult.IsReadOnly, "select-free succeeds when no active paid subscription exists and clears read-only");
        }

        // 3. Test Auto-assign Free plan on registration
        await using (var db = new LegacyVaultDbContext(dbOptions))
        {
            var vaultRepo = new VaultRepository(db);
            var newUser = new User { Email = "newowner@test.com", FullName = "New Owner", Status = "Active", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            await vaultRepo.Register(newUser, VaultService.OwnerRole, default);

            var userSub = await db.UserSubscriptions.Include(x => x.Plan).FirstOrDefaultAsync(x => x.UserId == newUser.UserId);
            check(userSub != null && userSub.Plan.Price == 0m && userSub.Status == "Active", "newly registered user is automatically assigned the free plan");
        }

        // 4. Full HTTP Integration Tests for /api/payments/quote, /api/payments/me/subscription, and SUBSCRIPTION_EXPIRED
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddControllers().AddApplicationPart(typeof(PaymentController).Assembly);
            builder.Services.AddDbContext<LegacyVaultDbContext>(o => o.UseSqlite(connection));
            builder.Services.AddScoped<IVaultRepository, VaultRepository>();
            builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
            builder.Services.AddScoped<IWorkflowRepository, WorkflowRepository>();
            builder.Services.AddScoped<IdentityService>();
            builder.Services.AddScoped<VaultService>();
            builder.Services.AddScoped<ManagementService>();
            builder.Services.AddScoped<VnPayService>();
            builder.Services.AddSingleton(new VnPayOptions { TmnCode = "TESTCODE", HashSecret = "TESTSECRET1234567890", ReturnUrl = "https://localhost/return" });
            builder.Services.AddSingleton(new SecurityOptions());
            builder.Services.AddSingleton<DocumentProtection>();
            builder.Services.AddSingleton<IDocumentStore, FakeStore>();
            builder.Services.AddSingleton<IOtpSender, FakeSender>();
            builder.Services.AddSingleton<OtpService>();
            builder.Services.AddScoped<RequireActiveSubscriptionFilter>();
            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
            TestAuthentication.Configure(builder.Configuration);
            builder.Services.AddVaultJwt(builder.Configuration);
            builder.Services.AddAuthorization();
            builder.Services.AddRateLimiter(o =>
            {
                foreach (var name in new[] { "login", "otp" })
                    o.AddPolicy(name, _ => System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("test"));
            });

            await using var app = builder.Build();
            app.UseVaultErrors();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapControllers();
            await app.StartAsync();

            var server = app.Services.GetRequiredService<IServer>();
            var addresses = server.Features.Get<IServerAddressesFeature>()!;
            using var client = new HttpClient { BaseAddress = new Uri(addresses.Addresses.First()) };
            client.DefaultRequestHeaders.Add("X-Vault-Request", "1");

            // Register and login an owner via HTTP
            await client.PostAsJsonAsync("/api/auth/register", new { fullName = "HTTP Lifecycle Owner", email = "httplifecycle@example.test", password = "Registration123!", confirmPassword = "Registration123!" });
            await TestAuthentication.UseToken(client, await client.PostAsJsonAsync("/api/auth/login", new { email = "httplifecycle@example.test", password = "Registration123!" }));

            // Test GET /api/payments/me/subscription
            var meSub = await client.GetFromJsonAsync<CurrentSubscriptionDto>("/api/payments/me/subscription");
            check(meSub != null && meSub.HasActiveSubscription && meSub.Subscription!.Plan.Price == 0m && !meSub.IsReadOnly, "HTTP GET /api/payments/me/subscription returns auto-assigned free subscription");

            // Test GET /api/payments/quote?planId=
            var quoteResponse = await client.GetFromJsonAsync<SubscriptionQuoteDto>($"/api/payments/quote?planId={basicPlan.PlanId}");
            check(quoteResponse != null && quoteResponse.Allowed && quoteResponse.AmountDue == 100_000m, "HTTP GET /api/payments/quote returns valid quote");

            // User can create vault while on active free plan
            var createVaultRes = await client.PostAsJsonAsync("/api/owner/vaults", new { name = "Initial Vault" });
            check(createVaultRes.StatusCode == HttpStatusCode.Created, "owner on active free plan can create vault");

            // Now simulate account expiration by adding an expired paid subscription and expiring all active plans in DB
            await using (var db = new LegacyVaultDbContext(dbOptions))
            {
                var userRecord = await db.Users.SingleAsync(u => u.Email == "httplifecycle@example.test");
                var activeSubs = await db.UserSubscriptions.Where(s => s.UserId == userRecord.UserId).ToListAsync();
                foreach (var s in activeSubs) s.Status = "Expired";

                // Add an expired paid subscription
                db.UserSubscriptions.Add(new UserSubscription
                {
                    UserId = userRecord.UserId,
                    PlanId = basicPlan.PlanId,
                    StartDate = DateTime.UtcNow.AddDays(-40),
                    EndDate = DateTime.UtcNow.AddDays(-10),
                    Status = "Expired"
                });
                await db.SaveChangesAsync();
            }

            // Test GET /api/payments/me/subscription when expired
            var expiredSub = await client.GetFromJsonAsync<CurrentSubscriptionDto>("/api/payments/me/subscription");
            check(expiredSub != null && expiredSub.IsReadOnly, "HTTP GET /api/payments/me/subscription reports isReadOnly = true when paid plan expired");

            // Test owner mutating API blocked with SUBSCRIPTION_EXPIRED (403)
            var blockedVaultRes = await client.PostAsJsonAsync("/api/owner/vaults", new { name = "Blocked Vault" });
            check(blockedVaultRes.StatusCode == HttpStatusCode.Forbidden, "owner mutating API returns 403 Forbidden when subscription is expired");
            var errorBody = await blockedVaultRes.Content.ReadAsStringAsync();
            check(errorBody.Contains("SUBSCRIPTION_EXPIRED"), "error response contains SUBSCRIPTION_EXPIRED errorCode");

            // Test owner GET API is still allowed when expired
            var getVaultsRes = await client.GetAsync("/api/owner/vaults");
            check(getVaultsRes.StatusCode == HttpStatusCode.OK, "owner GET API remains allowed when subscription is expired");

            // Test POST /api/owner/life-confirmation is still allowed when expired
            var lifeConfirmRes = await client.PostAsJsonAsync("/api/owner/life-confirmation", new { });
            check(lifeConfirmRes.StatusCode == HttpStatusCode.OK, "owner life-confirmation remains allowed when subscription is expired");

            // Test deleting discontinued/inactive VNPay subscription plans
            await using (var db = new LegacyVaultDbContext(dbOptions))
            {
                var service = new VnPayService(new PaymentRepository(db), new VnPayOptions());
                var discontinuedPlan = await service.CreatePlan(ownerUserId, new SaveSubscriptionPlanInput
                {
                    PlanName = "Discontinued Plan",
                    MaxVaults = 2,
                    MaxStorageBytes = 100_000,
                    Price = 50_000m,
                    DurationDays = 30,
                    IsActive = true
                }, default);

                var activeDeleteBlocked = false;
                try { await service.DeleteInactivePlan(ownerUserId, discontinuedPlan.PlanId, default); }
                catch (WorkflowException ex) when (ex.StatusCode == 409 && ex.ErrorCode == "PLAN_STILL_ACTIVE") { activeDeleteBlocked = true; }
                check(activeDeleteBlocked, "active subscription plan cannot be permanently deleted before deactivation");

                await service.DeactivatePlan(ownerUserId, discontinuedPlan.PlanId, default);

                var deleteAllRes = await service.DeleteAllInactivePlans(ownerUserId, default);
                check(deleteAllRes.DeletedCount == 1 && deleteAllRes.DeletedPlanIds.Contains(discontinuedPlan.PlanId), "DeleteAllInactivePlans removes discontinued VNPay subscription plan");
            }
        }
    }
}
