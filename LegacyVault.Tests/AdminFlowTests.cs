using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Security;
using LegacyVault.BLL.Services;
using LegacyVault.DAL.Context;
using LegacyVault.DAL.Entities;
using LegacyVault.DAL.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

internal static class AdminFlowTests
{
    public static async Task Run(Action<bool, string> check)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction("sysdatetime", () => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fffffff"));

        var options = new DbContextOptionsBuilder<LegacyVaultDbContext>().UseSqlite(connection).Options;
        await using var db = new LegacyVaultDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var repo = new VaultRepository(db);
        var adminService = new AdminService(repo);

        // 1. Seed Admin User and Normal User
        var adminRole = new Role { RoleName = VaultService.AdminRole };
        var ownerRole = new Role { RoleName = VaultService.OwnerRole };
        var executorRole = new Role { RoleName = VaultService.ExecutorRole };
        var verifierRole = new Role { RoleName = VaultService.VerifierRole };
        var beneficiaryRole = new Role { RoleName = VaultService.BeneficiaryRole };
        db.Roles.AddRange(adminRole, ownerRole, executorRole, verifierRole, beneficiaryRole);

        var adminUser = new User
        {
            FullName = "Admin User",
            Email = "admin@example.test",
            Status = "Active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Roles = new List<Role> { adminRole }
        };

        var normalUser = new User
        {
            FullName = "Normal Owner",
            Email = "owner@example.test",
            Status = "Active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Roles = new List<Role> { ownerRole }
        };

        db.Users.AddRange(adminUser, normalUser);
        await db.SaveChangesAsync();

        // Check non-admin rejection
        var rejected = false;
        try
        {
            await adminService.GetRolesAsync(normalUser.UserId, default);
        }
        catch (WorkflowException ex) when (ex.StatusCode == 403)
        {
            rejected = true;
        }
        check(rejected, "admin endpoints reject non-admin users with 403");

        // 2. Test Step 1: GetRolesAsync
        var roles = await adminService.GetRolesAsync(adminUser.UserId, default);
        check(roles.Count == 5, "roles endpoint returns 5 RBAC roles");
        check(roles.Any(r => r.RoleName == VaultService.AdminRole && r.ActiveUsersCount == 1), "admin role has 1 active user");
        check(roles.Any(r => r.RoleName == VaultService.OwnerRole && r.ActiveUsersCount == 1), "owner role has 1 active user");

        // 3. Test Step 2: Audit Logs & Security Dashboard
        db.AuditLogs.Add(new AuditLog
        {
            UserId = normalUser.UserId,
            Action = "FailedLogin",
            Result = "Failed",
            Ipaddress = "192.168.1.100",
            Timestamp = DateTime.UtcNow
        });
        db.AuditLogs.Add(new AuditLog
        {
            UserId = adminUser.UserId,
            Action = "AdminLogin",
            Result = "Success",
            Ipaddress = "192.168.1.1",
            Timestamp = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var dashboard = await adminService.GetSecurityDashboardAsync(adminUser.UserId, default);
        check(dashboard.TotalAuditLogs24h >= 2, "security dashboard counts recent audit logs");
        check(dashboard.FailedActions24h >= 1, "security dashboard counts failed actions");
        check(dashboard.TopFailedActions.Any(x => x.Key == "FailedLogin"), "security dashboard tracks top failed action");

        // 4. Test Step 3: System Configuration
        var config = await adminService.GetSystemConfigAsync(adminUser.UserId, default);
        check(config.EncryptionPolicy.DefaultAlgorithm == "AES-256-GCM", "default encryption policy is AES-256-GCM");
        check(config.ProofOfLifePolicy.DefaultPingIntervalDays == 30, "default proof of life ping interval is 30 days");

        var updatedConfig = await adminService.UpdateSystemConfigAsync(adminUser.UserId, new UpdateSystemConfigInput
        {
            EncryptionPolicy = new EncryptionPolicyConfig { KeyRotationIntervalDays = 60 },
            ProofOfLifePolicy = new ProofOfLifePolicyConfig { DefaultPingIntervalDays = 45, GracePeriodDays = 10 }
        }, default);
        check(updatedConfig.EncryptionPolicy.KeyRotationIntervalDays == 60, "updated key rotation interval persisted");
        check(updatedConfig.ProofOfLifePolicy.DefaultPingIntervalDays == 45, "updated proof of life interval persisted");
        check(await db.AuditLogs.AnyAsync(a => a.Action == "UpdateSystemConfig"), "system config update persists audit log");

        // 5. Test Step 3: Key Rotation
        var vault = new DigitalVault { OwnerId = normalUser.UserId, VaultName = "Test Vault", Status = "Active" };
        db.DigitalVaults.Add(vault);
        await db.SaveChangesAsync();

        var asset = new DigitalAsset { VaultId = vault.VaultId, AssetName = "Test Asset", AssetType = "Account", Status = "Active" };
        db.DigitalAssets.Add(asset);
        await db.SaveChangesAsync();

        var encKey = new EncryptionKey
        {
            AssetId = asset.AssetId,
            EncryptedKey = "sample-encrypted-key",
            Algorithm = "AES-256-GCM",
            KeyVersion = 1,
            Status = "Active",
            CreatedAt = DateTime.UtcNow.AddDays(-100)
        };
        db.EncryptionKeys.Add(encKey);
        await db.SaveChangesAsync();

        var keySummaryBefore = await adminService.GetKeyRotationSummaryAsync(adminUser.UserId, default);
        check(keySummaryBefore.TotalKeys == 1, "key rotation summary counts total keys");
        check(keySummaryBefore.KeysNeedingRotation == 1, "detects key exceeding 60-day rotation interval");

        var keySummaryAfter = await adminService.RotateEncryptionKeysAsync(adminUser.UserId, default);
        check(keySummaryAfter.Keys.First().KeyVersion == 2, "key version incremented upon rotation");
        check(keySummaryAfter.Keys.First().RotatedAt.HasValue, "rotation date updated upon rotation");
        check(await db.AuditLogs.AnyAsync(a => a.Action == "RotateEncryptionKeys"), "key rotation persists audit log");

        // 6. Test Step 4: Third-Party Integrations
        var integrations = await adminService.GetIntegrationsAsync(adminUser.UserId, default);
        check(integrations.Count >= 5, "returns list of third-party integrations (eKYC, CA, Notary, Storage, Email)");

        var pingResult = await adminService.PingIntegrationAsync(adminUser.UserId, "ekyc-vnpt", default);
        check(pingResult.Success, "ping eKYC service succeeds");
        check(await db.AuditLogs.AnyAsync(a => a.Action == "TestIntegrationConnection"), "ping integration persists audit log");

        // 7. Test Admin User Edit & Password Reset
        var updatedUser = await adminService.UpdateUserAsync(adminUser.UserId, normalUser.UserId, new UpdateUserByAdminInput
        {
            FullName = "Updated Owner Name",
            Phone = "0909999888",
            IdentityNumber = "001200999999"
        }, default);
        check(updatedUser.FullName == "Updated Owner Name" && updatedUser.Phone == "0909999888", "admin successfully updates user profile");

        await adminService.ResetUserPasswordAsync(adminUser.UserId, normalUser.UserId, new ResetPasswordByAdminInput
        {
            NewPassword = "AdminResetPass123!"
        }, default);
        check(await db.AuditLogs.AnyAsync(a => a.Action == "AdminResetUserPassword"), "admin password reset persists audit log");

        // 8. Test User Self-Profile Update & Change Password with OTP
        var fakeSender = new TestOtpSender();
        var identityService = new IdentityService(repo, fakeSender);

        var selfProfile = await identityService.UpdateProfileAndGetMe(normalUser.UserId, new UpdateProfileInput
        {
            FullName = "Owner Self Updated",
            Phone = "0911222333"
        }, default);
        check(selfProfile.FullName == "Owner Self Updated", "user successfully updates own profile");

        // Request Password Change (Validates old pass: "AdminResetPass123!")
        var challenge = await identityService.RequestPasswordChange(normalUser.UserId, new ChangePasswordRequestInput
        {
            OldPassword = "AdminResetPass123!",
            NewPassword = "UserNewPass1234!",
            ConfirmPassword = "UserNewPass1234!"
        }, default);
        check(!string.IsNullOrEmpty(challenge.ChallengeId), "password change challenge generated");
        check(!string.IsNullOrEmpty(fakeSender.LastCode), "OTP code sent to email");

        // Wrong OTP rejection
        var wrongOtpRejected = false;
        try
        {
            await identityService.ConfirmPasswordChange(normalUser.UserId, new ConfirmChangePasswordInput
            {
                ChallengeId = challenge.ChallengeId,
                Code = "000000"
            }, default);
        }
        catch (WorkflowException ex) when (ex.StatusCode == 400)
        {
            wrongOtpRejected = true;
        }
        check(wrongOtpRejected, "wrong OTP rejected with 400");

        // Correct OTP confirmation
        await identityService.ConfirmPasswordChange(normalUser.UserId, new ConfirmChangePasswordInput
        {
            ChallengeId = challenge.ChallengeId,
            Code = fakeSender.LastCode
        }, default);

        var checkLogin = await identityService.CheckPassword(normalUser.Email, "UserNewPass1234!", default);
        check(checkLogin.UserId == normalUser.UserId, "new password verified successfully after OTP confirmation");
        check(await db.AuditLogs.AnyAsync(a => a.Action == "ChangePassword"), "change password persists audit log");

        // 9. Test Registration with Email OTP
        var regChallenge = await identityService.RequestRegister(new RegisterInput
        {
            FullName = "New OTP Registered User",
            Email = "otpregister@example.test",
            Password = "OtpRegisterPass123!",
            ConfirmPassword = "OtpRegisterPass123!",
            Phone = "0988777666"
        }, default);
        check(!string.IsNullOrEmpty(regChallenge.ChallengeId), "registration challenge generated");
        check(!string.IsNullOrEmpty(fakeSender.LastCode), "registration OTP code sent to email");

        var regResult = await identityService.ConfirmRegister(new ConfirmRegisterInput
        {
            ChallengeId = regChallenge.ChallengeId,
            Code = fakeSender.LastCode
        }, default);
        check(regResult.UserId > 0 && regResult.Email == "otpregister@example.test", "user registered successfully after OTP confirmation");

        var regLogin = await identityService.CheckPassword("otpregister@example.test", "OtpRegisterPass123!", default);
        check(regLogin.UserId == regResult.UserId, "newly registered user can login with confirmed credentials");
    }

    private sealed class TestOtpSender : IOtpSender
    {
        public string LastCode { get; private set; } = "";
        public Task Send(string email, string code, CancellationToken ct) { LastCode = code; return Task.CompletedTask; }
        public Task SendEmail(string email, string subject, string body, CancellationToken ct)
        {
            var match = System.Text.RegularExpressions.Regex.Match(body, @"\b\d{6}\b");
            if (match.Success) LastCode = match.Value;
            return Task.CompletedTask;
        }
    }
}
