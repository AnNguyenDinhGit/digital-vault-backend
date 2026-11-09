using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Services;
using LegacyVault.DAL.Context;
using LegacyVault.DAL.Entities;
using LegacyVault.DAL.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

// Nhật ký hoạt động, hồ sơ cá nhân và bật/tắt 2FA của Chủ sở hữu trên SQLite trong bộ nhớ.
internal static class OwnerAccountTests
{
    public static async Task Run(Action<bool, string> check)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction("sysdatetime", () => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fffffff"));
        var options = new DbContextOptionsBuilder<LegacyVaultDbContext>().UseSqlite(connection).Options;

        int ownerId, otherId;
        await using (var setup = new LegacyVaultDbContext(options))
        {
            // Chỉ tạo cơ sở dữ liệu SQLite cô lập, không chạm vào SQL Server thật.
            await setup.Database.EnsureCreatedAsync();
            var now = DateTime.UtcNow;
            var owner = new User { FullName = "Owner Account", Email = "owner.account@example.test", Phone = "0901111111", Status = "Active", CreatedAt = now, UpdatedAt = now };
            var other = new User { FullName = "Other Account", Email = "other.account@example.test", Phone = "0902222222", Status = "Active", CreatedAt = now, UpdatedAt = now };
            setup.Users.AddRange(owner, other);
            await setup.SaveChangesAsync();
            ownerId = owner.UserId;
            otherId = other.UserId;
            setup.AuditLogs.AddRange(
                new AuditLog { UserId = ownerId, Action = "Login", EntityType = "User", EntityId = ownerId, Result = "Success", Timestamp = now.AddMinutes(-30), Ipaddress = "10.0.0.1" },
                new AuditLog { UserId = ownerId, Action = "CreateVault", EntityType = "DigitalVault", EntityId = 7, Result = "Success", Timestamp = now.AddMinutes(-10) },
                new AuditLog { UserId = ownerId, Action = "ChangePassword", EntityType = "User", EntityId = ownerId, Result = "Success", Timestamp = now.AddMinutes(-5) },
                new AuditLog { UserId = otherId, Action = "CreateVault", EntityType = "DigitalVault", EntityId = 8, Result = "Success", Timestamp = now });
            await setup.SaveChangesAsync();
        }

        await using (var db = new LegacyVaultDbContext(options))
        {
            var service = new OwnerAccountService(new VaultRepository(db));
            var logs = await service.ActivityLogs(ownerId, default);
            check(logs.Count == 3 && logs.All(x => x.Action != "CreateVault" || x.EntityId == 7), "owner activity logs only include the caller's own audit entries");
            check(logs.Select(x => x.Timestamp).SequenceEqual(logs.Select(x => x.Timestamp).OrderByDescending(x => x)), "owner activity logs are ordered newest first");
            check(logs.Any(x => x.Action == "Login" && x.IpAddress == "10.0.0.1"), "owner activity logs expose IP address of login entries");

            var profile = await service.Profile(ownerId, default);
            check(profile.Email == "owner.account@example.test" && profile.Phone == "0901111111" && !profile.TwoFactorEnabled && profile.PasswordChangedAt is not null, "owner profile returns account fields, 2FA state and last password change");
        }

        await using (var db = new LegacyVaultDbContext(options))
        {
            var service = new OwnerAccountService(new VaultRepository(db));
            var updated = await service.UpdateProfile(ownerId, new OwnerProfileInput { FullName = "  Owner Renamed  ", Phone = "+84903333333" }, default);
            check(updated.FullName == "Owner Renamed" && updated.Phone == "0903333333", "owner profile update trims name and normalizes phone");

            var cleared = await service.UpdateProfile(ownerId, new OwnerProfileInput { FullName = "Owner Renamed", Phone = null }, default);
            check(cleared.Phone is null, "owner profile update with null phone clears the phone number");

            try { await service.UpdateProfile(ownerId, new OwnerProfileInput { FullName = "Owner Renamed", Phone = "0902222222" }, default); check(false, "duplicate phone must be rejected"); }
            catch (WorkflowException ex) { check(ex.StatusCode == 409, "owner profile update rejects a phone number used by another account"); }

            try { await service.UpdateProfile(ownerId, new OwnerProfileInput { FullName = "   ", Phone = null }, default); check(false, "blank name must be rejected"); }
            catch (WorkflowException ex) { check(ex.StatusCode == 400, "owner profile update rejects a blank full name"); }
        }

        await using (var db = new LegacyVaultDbContext(options))
        {
            var service = new OwnerAccountService(new VaultRepository(db));
            check((await service.SetTwoFactor(ownerId, new TwoFactorToggleInput { Enabled = true }, default)).TwoFactorEnabled, "owner can enable email two-factor authentication");
            check((await service.SetTwoFactor(ownerId, new TwoFactorToggleInput { Enabled = true }, default)).TwoFactorEnabled, "enabling two-factor twice is idempotent");
        }
        await using (var db = new LegacyVaultDbContext(options))
        {
            var factors = await db.TwoFactorAuths.Where(x => x.UserId == ownerId).ToListAsync();
            check(factors.Count == 1 && factors[0].IsEnabled && factors[0].Method == "Email", "exactly one enabled email factor is stored, as required by login");
            var service = new OwnerAccountService(new VaultRepository(db));
            check(!(await service.SetTwoFactor(ownerId, new TwoFactorToggleInput { Enabled = false }, default)).TwoFactorEnabled, "owner can disable email two-factor authentication");
            try { await service.SetTwoFactor(ownerId, new TwoFactorToggleInput { Enabled = null }, default); check(false, "missing enabled must be rejected"); }
            catch (WorkflowException ex) { check(ex.StatusCode == 400, "two-factor toggle rejects a missing enabled value"); }
        }

        await using (var db = new LegacyVaultDbContext(options))
        {
            db.TwoFactorAuths.Add(new TwoFactorAuth { UserId = otherId, Method = "TOTP", Secret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP", IsEnabled = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            var service = new OwnerAccountService(new VaultRepository(db));
            try { await service.SetTwoFactor(otherId, new TwoFactorToggleInput { Enabled = false }, default); check(false, "TOTP disable must be rejected"); }
            catch (WorkflowException ex) { check(ex.StatusCode == 409, "administrator-configured TOTP cannot be disabled by the account owner"); }
        }

        await using (var db = new LegacyVaultDbContext(options))
        {
            var actions = await db.AuditLogs.Where(x => x.UserId == ownerId).Select(x => x.Action).ToListAsync();
            check(actions.Contains("UpdateProfile") && actions.Contains("EnableTwoFactor") && actions.Contains("DisableTwoFactor"), "profile and two-factor changes are written to the audit log");
            try { await new OwnerAccountService(new VaultRepository(db)).Profile(999999, default); check(false, "unknown user must be rejected"); }
            catch (WorkflowException ex) { check(ex.StatusCode == 404, "owner profile rejects an unknown user"); }
        }
    }
}

