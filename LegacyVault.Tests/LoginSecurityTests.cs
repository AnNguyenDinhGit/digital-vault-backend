using LegacyVault.API.Authentication;
using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Security;
using LegacyVault.BLL.Services;
using LegacyVault.DAL.Context;
using LegacyVault.DAL.Entities;
using LegacyVault.DAL.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

internal static class LoginSecurityTests
{
    public static async Task Run(Action<bool, string> check)
    {
        async Task Reject(Func<Task> action, int status, string name)
        {
            try { await action(); throw new Exception("Expected rejection: " + name); }
            catch (WorkflowException ex) { check(ex.StatusCode == status, name); }
        }
        var clock = new LoginClock();
        var sender = new LoginSender();
        var store = new LoginFactorStore(sender, clock);
        var user = new User { UserId = 1, Email = "factor@example.test", Status = "Active" };
        var factor = new TwoFactorAuth { TwoFactorId = 1, Method = "Email", Secret = "email-factor", IsEnabled = true };
        user.TwoFactorAuths.Add(factor);
        user.Authentications.Add(new() { AuthId = 1, PasswordHash = "original-password-hash" });
        var first = await store.Start(user, default, google: true);
        await Reject(() => store.Resend(user, first.ChallengeId, default), 429, "2FA resend respects per-user cooldown");
        clock.Advance(TimeSpan.FromSeconds(61));
        var second = await store.Resend(user, first.ChallengeId, default);
        check(second.ChallengeId != first.ChallengeId && (await store.Identity(second.ChallengeId, default)).Google, "resend rotates challenge and preserves Google primary authentication");
        await Reject(() => store.Verify(user, first.ChallengeId, sender.Code, default), 400, "resent 2FA invalidates old challenge");
        var originalEmail = user.Email;
        user.Email = "changed@example.test";
        await Reject(() => store.Verify(user, second.ChallengeId, sender.Code, default), 409, "email change invalidates pending authentication");
        user.Email = originalEmail;
        user.Authentications.Single().PasswordHash = "changed-password-hash";
        await Reject(() => store.Resend(user, second.ChallengeId, default), 409, "password change prevents resending an authenticated challenge");
        await Reject(() => store.Verify(user, second.ChallengeId, sender.Code, default), 409, "password change invalidates pending authentication");
        user.Authentications.Single().PasswordHash = "original-password-hash";
        factor.IsEnabled = false;
        await Reject(() => store.Verify(user, second.ChallengeId, sender.Code, default), 409, "disabling 2FA does not turn a challenge into a token");
        factor.IsEnabled = true;
        var correctCode = sender.Code;
        var wrongCode = correctCode == "000000" ? "000001" : "000000";
        for (var attempt = 1; attempt <= 5; attempt++)
            await Reject(() => store.Verify(user, second.ChallengeId, wrongCode, default), 400, "2FA rejects wrong attempt " + attempt);
        await Reject(() => store.Verify(user, second.ChallengeId, correctCode, default), 400, "2FA attempt limit blocks a subsequently correct code");
        clock.Advance(TimeSpan.FromSeconds(61));
        await Reject(() => store.Resend(user, second.ChallengeId, default), 400, "exhausted challenge cannot reset attempts through resend");
        var third = await store.Start(user, default);
        var thirdCode = sender.Code;
        clock.Advance(TimeSpan.FromSeconds(61)); sender.Fail = true;
        await Reject(() => store.Resend(user, third.ChallengeId, default), 503, "SMTP failure cannot return a successful resend");
        await store.Verify(user, third.ChallengeId, thirdCode, default);
        check(true, "failed resend preserves the previous valid challenge");
        sender.Fail = false;
        var fourth = await store.Start(user, default);
        var fourthCode = sender.Code;
        var racing = await Task.WhenAll(VerifyOnce(), VerifyOnce());
        check(racing.Count(x => x) == 1, "concurrent 2FA verification succeeds exactly once");
        async Task<bool> VerifyOnce()
        {
            try { await store.Verify(user, fourth.ChallengeId, fourthCode, default); return true; }
            catch (WorkflowException) { return false; }
        }
        clock.Advance(TimeSpan.FromSeconds(61));
        factor.Method = "TOTP"; factor.Secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var totp = await store.Start(user, default);
        await Reject(() => store.Resend(user, totp.ChallengeId, default), 400, "authenticator TOTP cannot be emailed or resent");
        clock.Advance(TimeSpan.FromMinutes(6));
        await Reject(() => store.Identity(totp.ChallengeId, default), 400, "expired factor challenge cannot identify a login");

        var callback = new GoogleOptions { FrontendCallbackUrl = "https://frontend.example.test/auth/google" };
        callback.ValidateFrontendCallback(new[] { "https://frontend.example.test" });
        check(true, "fixed Google frontend callback accepts allowlisted HTTPS origin");
        foreach (var url in new[] { "https://evil.example.test/auth", "javascript:alert(1)", "https://frontend.example.test/auth?token=x", "https://frontend.example.test/auth#token", "http://frontend.example.test/auth" })
        {
            callback.FrontendCallbackUrl = url;
            try { callback.ValidateFrontendCallback(new[] { "https://frontend.example.test" }); throw new Exception("Accepted unsafe callback"); }
            catch (InvalidOperationException) { check(true, "Google rejects unsafe frontend callback: " + url); }
        }

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction("sysdatetime", () => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fffffff"));
        var options = new DbContextOptionsBuilder<LegacyVaultDbContext>().UseSqlite(connection).Options;
        var passwordHash = IdentityService.HashPassword("Registration123!");
        int userId;
        await using (var db = new LegacyVaultDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            var account = new User { FullName = "Login", Email = "login@example.test", Status = "Active", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            account.Roles.Add(new() { RoleName = "Owner" });
            account.Authentications.Add(new() { PasswordHash = passwordHash, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            account.TwoFactorAuths.Add(new() { Method = "Email", Secret = "email", IsEnabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            db.Users.Add(account); await db.SaveChangesAsync(); userId = account.UserId;
        }
        var loginClock = new LoginClock(); var loginSender = new LoginSender(); var loginFactors = new LoginFactorStore(loginSender, loginClock);
        var issuer = new CountingIssuer();
        PasswordAuthenticationService Service(LegacyVaultDbContext db)
        {
            var repository = new VaultRepository(db);
            return new(new IdentityService(repository), repository, loginFactors, issuer);
        }
        LoginChallengeDto challenge;
        await using (var db = new LegacyVaultDbContext(options))
            challenge = (LoginChallengeDto)await Service(db).Login(" LOGIN@EXAMPLE.TEST ", "Registration123!", default);
        await using (var db = new LegacyVaultDbContext(options))
            check((await db.Authentications.SingleAsync()).LastLoginAt is null && !await db.AuditLogs.AnyAsync() && issuer.Count == 0, "primary password success does not record complete login or issue JWT before 2FA");
        await using (var db = new LegacyVaultDbContext(options))
        {
            var account = await db.Users.SingleAsync(); account.Status = "Inactive"; await db.SaveChangesAsync();
        }
        await using (var db = new LegacyVaultDbContext(options))
            await Reject(() => Service(db).Verify(new(challenge.ChallengeId, loginSender.Code), default), 401, "disabled account cannot finish a pending factor challenge");
        await using (var db = new LegacyVaultDbContext(options))
        {
            var account = await db.Users.SingleAsync(); account.Status = "Active"; await db.SaveChangesAsync();
        }
        await using (var db = new LegacyVaultDbContext(options))
            await Service(db).Verify(new(challenge.ChallengeId, loginSender.Code), default);
        await using (var db = new LegacyVaultDbContext(options))
            check((await db.Authentications.SingleAsync()).LastLoginAt is not null && (await db.AuditLogs.SingleAsync()).Action == "PasswordLogin" && issuer.Count == 1, "complete password and 2FA persist login timestamp and safe audit before issuing JWT");
        loginClock.Advance(TimeSpan.FromSeconds(61));
        await using (var db = new LegacyVaultDbContext(options))
            challenge = (LoginChallengeDto)await Service(db).AfterPrimary(new(userId, new[] { "Owner" }), default, google: true);
        await using (var db = new LegacyVaultDbContext(options))
        {
            (await db.Users.SingleAsync()).Roles.Add(new() { RoleName = "Staff" }); await db.SaveChangesAsync();
        }
        await using (var db = new LegacyVaultDbContext(options))
            await Reject(() => Service(db).Verify(new(challenge.ChallengeId, loginSender.Code), default), 403, "Google factor challenge cannot sign in an account promoted to Staff");
        await using (var db = new LegacyVaultDbContext(options))
        {
            var account = await db.Users.Include(x => x.Roles).Include(x => x.TwoFactorAuths).SingleAsync();
            account.Roles.Remove(account.Roles.Single(x => x.RoleName == "Staff")); account.TwoFactorAuths.Single().IsEnabled = false; await db.SaveChangesAsync();
        }
        await using (var db = new LegacyVaultDbContext(options))
        {
            var token = (TokenDto)await Service(db).AfterPrimary(new(userId, new[] { "Admin" }), default);
            check(token.Roles.SequenceEqual(new[] { "Owner" }), "issued token ignores stale or supplied primary-result roles");
        }
        var beforeFailureCount = issuer.Count;
        DateTime? lastLogin;
        await using (var db = new LegacyVaultDbContext(options))
        {
            lastLogin = (await db.Authentications.SingleAsync()).LastLoginAt;
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_login_audit BEFORE INSERT ON AUDIT_LOGS BEGIN SELECT RAISE(ABORT, 'isolated audit failure'); END;");
        }
        await using (var db = new LegacyVaultDbContext(options))
        {
            try { await Service(db).AfterPrimary(new(userId, new[] { "Owner" }), default); throw new Exception("Expected failed persistence"); }
            catch (DbUpdateException) { check(issuer.Count == beforeFailureCount, "failed login persistence never issues an access token"); }
        }
        await using (var db = new LegacyVaultDbContext(options))
            check((await db.Authentications.SingleAsync()).LastLoginAt == lastLogin, "failed login audit rolls back timestamp update in relational database");
        await using (var db = new LegacyVaultDbContext(options))
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_login_audit;");
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await using var db = new LegacyVaultDbContext(options);
            await Reject(() => Service(db).Login("login@example.test", "WrongPassword123!", default), 401, "persisted wrong password attempt " + attempt);
        }
        await using (var db = new LegacyVaultDbContext(options))
        {
            var auth = await db.Authentications.SingleAsync();
            check(auth.LockedUntil > DateTime.UtcNow && auth.FailedLoginCount == 0, "five wrong passwords persist a temporary account lock");
            await Reject(() => Service(db).Login("login@example.test", "Registration123!", default), 401, "correct password cannot bypass a persisted lock");
        }
    }

    private sealed class LoginClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan value) => now += value;
    }

    private sealed class LoginSender : IOtpSender
    {
        public string Code { get; private set; } = "";
        public bool Fail { get; set; }

        public Task Send(string email, string code, CancellationToken ct)
        {
            if (Fail) throw new WorkflowException(503, "Test email unavailable.");
            Code = code;
            return Task.CompletedTask;
        }

        public Task SendEmail(string email, string subject, string body, CancellationToken ct)
        {
            if (Fail) throw new WorkflowException(503, "Test email unavailable.");
            Code = body;
            return Task.CompletedTask;
        }
    }

    private sealed class CountingIssuer : IAccessTokenIssuer
    {
        public int Count { get; private set; }
        public TokenDto Issue(LoginDto login) { Count++; return new(login.UserId, login.Roles, "test-token", "Bearer", 900); }
    }
}