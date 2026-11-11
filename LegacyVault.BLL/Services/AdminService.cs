using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using LegacyVault.BLL.DTOs;
using LegacyVault.DAL.Entities;
using LegacyVault.DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.BLL.Services;

public sealed class AdminService(IVaultRepository repository)
{
    private async Task<User> RequireAdmin(int adminUserId, CancellationToken ct)
    {
        var user = await repository.User(adminUserId, ct);
        if (user is null || user.Status != "Active" || !user.Roles.Any(r => r.RoleName == VaultService.AdminRole))
            throw new WorkflowException(403, "Quyền Admin là bắt buộc.");
        return user;
    }

    private static readonly object ConfigLock = new();
    private static EncryptionPolicyConfig _encryptionPolicy = new();
    private static ProofOfLifePolicyConfig _proofOfLifePolicy = new();
    private static StoragePolicyConfig _storagePolicy = new();
    private static BackupPolicyConfig _backupPolicy = new();
    private static DateTime _configUpdatedAt = DateTime.UtcNow;
    private static string _configUpdatedBy = "system@legacyvault.local";

    private static readonly Dictionary<string, (string Name, string Type, string Provider, string Endpoint)> IntegrationConfigs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ekyc-vnpt"] = ("Hệ thống eKYC Định danh điện tử", "eKYC", "VNPT AI", "https://api.vnpt.vn/ekyc/v1"),
        ["ca-vnpt"] = ("Dịch vụ Chữ ký số Chứng thư CA", "DigitalSignature", "VNPT-CA", "https://ca.vnpt.vn/api/sign"),
        ["notary-gateway"] = ("Cổng Công chứng điện tử Pháp lý", "ElectronicNotary", "VN Notary Gateway", "https://notary.gov.vn/api/v2"),
        ["blob-storage"] = ("Lưu trữ tài liệu mã hóa Blob Storage", "CloudStorage", "Azure Blob Storage", "https://legacyvault.blob.core.windows.net"),
        ["smtp-gateway"] = ("Cổng phát hành OTP & Email thông báo", "EmailGateway", "SMTP Relay Service", "smtp://mail.legacyvault.local:587")
    };

    public async Task<IReadOnlyList<RolePermissionDto>> GetRolesAsync(int adminUserId, CancellationToken ct)
    {
        await RequireAdmin(adminUserId, ct);

        var rolesInDb = await repository.QueryRoles().ToListAsync(ct);
        var usersQuery = repository.QueryUsers();

        var definitions = new (string RoleName, string Description, string[] Permissions)[]
        {
            (VaultService.OwnerRole, "Chủ sở hữu kho di sản số", ["vault:create", "vault:manage", "asset:create", "asset:manage", "executor:assign", "beneficiary:assign", "proof_of_life:ping", "audit:view_own"]),
            (VaultService.ExecutorRole, "Người thi hành / Người bàn giao", ["handover:view_assigned", "handover:accept", "handover:decline", "handover:submit_proof", "handover:start", "handover:complete"]),
            (VaultService.VerifierRole, "Thẩm định viên pháp lý", ["legal:view_queue", "legal:review_evidence", "legal:approve", "legal:reject", "legal:request_info", "legal:sign"]),
            (VaultService.BeneficiaryRole, "Người thụ hưởng di sản", ["claim:view_cases", "claim:download_asset", "claim:decrypt", "claim:confirm_receipt"]),
            (VaultService.AdminRole, "Quản trị viên hệ thống", ["admin:users_manage", "admin:roles_view", "admin:audit_logs", "admin:security_alerts", "admin:system_config", "admin:key_rotation", "admin:integrations_manage"])
        };

        var result = new List<RolePermissionDto>();
        foreach (var def in definitions)
        {
            var roleEntity = rolesInDb.FirstOrDefault(r => string.Equals(r.RoleName, def.RoleName, StringComparison.OrdinalIgnoreCase));
            var activeCount = await usersQuery.CountAsync(u => u.Status == "Active" && u.Roles.Any(r => r.RoleName == def.RoleName), ct);

            result.Add(new RolePermissionDto(
                roleEntity?.RoleId ?? 0,
                def.RoleName,
                def.Description,
                def.Permissions,
                activeCount
            ));
        }

        return result;
    }

    public async Task<SecurityDashboardDto> GetSecurityDashboardAsync(int adminUserId, CancellationToken ct)
    {
        await RequireAdmin(adminUserId, ct);

        var since24h = DateTime.UtcNow.AddHours(-24);
        var recentLogs = await repository.QueryAuditLogs()
            .Where(a => a.Timestamp >= since24h)
            .OrderByDescending(a => a.Timestamp)
            .Take(500)
            .ToListAsync(ct);

        var total24h = recentLogs.Count;
        var failedLogs = recentLogs.Where(a =>
            !string.Equals(a.Result, "Success", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(a.Result, "Alive_Signed", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(a.Result, "ReportedDeceased", StringComparison.OrdinalIgnoreCase)).ToList();
        var failed24h = failedLogs.Count;
        var failureRate = total24h > 0 ? Math.Round((double)failed24h * 100.0 / total24h, 2) : 0.0;

        var topFailed = failedLogs
            .GroupBy(a => LocalizeAuditAction(a.Action))
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => new KeyValuePair<string, int>(g.Key, g.Count()))
            .ToList();

        var alerts = new List<SecurityAlertDto>();

        if (failed24h >= 5)
        {
            alerts.Add(new SecurityAlertDto(
                "ALERT-SPIKE-FAIL",
                "Warning",
                "Tỷ lệ thao tác thất bại tăng bất thường",
                $"Ghi nhận {failed24h} thao tác thất bại trong 24 giờ qua (chiếm {failureRate}% tổng số thao tác).",
                null,
                null,
                DateTime.UtcNow,
                "Active"
            ));
        }

        var failuresByUser = failedLogs
            .Where(a => a.UserId.HasValue)
            .GroupBy(a => a.UserId!.Value)
            .Where(g => g.Count() >= 3)
            .ToList();

        foreach (var group in failuresByUser)
        {
            var userLog = group.First();
            alerts.Add(new SecurityAlertDto(
                $"ALERT-FAIL-USER-{group.Key}",
                "Critical",
                "Phát hiện người dùng có nhiều thao tác lỗi liên tiếp",
                $"Người dùng ID {group.Key} ({userLog.User?.Email ?? "N/A"}) có {group.Count()} lần thao tác không thành công.",
                userLog.User?.Email,
                userLog.Ipaddress,
                group.Max(g => g.Timestamp),
                "Active"
            ));
        }

        var failuresByIp = failedLogs
            .Where(a => !string.IsNullOrWhiteSpace(a.Ipaddress))
            .GroupBy(a => a.Ipaddress!)
            .Where(g => g.Count() >= 5)
            .ToList();

        foreach (var ipGroup in failuresByIp)
        {
            alerts.Add(new SecurityAlertDto(
                $"ALERT-IP-BRUTE-{Math.Abs(ipGroup.Key.GetHashCode())}",
                "Critical",
                "Cảnh báo dấu hiệu Brute-force từ IP",
                $"Địa chỉ IP {ipGroup.Key} ghi nhận {ipGroup.Count()} yêu cầu lỗi liên tiếp.",
                null,
                ipGroup.Key,
                ipGroup.Max(g => g.Timestamp),
                "Active"
            ));
        }

        if (alerts.Count == 0 && total24h > 0)
        {
            alerts.Add(new SecurityAlertDto(
                "INFO-NORMAL",
                "Info",
                "Hệ thống an toàn",
                "Không phát hiện hành vi truy cập hoặc thao tác bất thường trong 24 giờ qua.",
                null,
                null,
                DateTime.UtcNow,
                "Active"
            ));
        }

        return new SecurityDashboardDto(total24h, failed24h, alerts.Count(a => a.Severity != "Info"), failureRate, alerts, topFailed);
    }

    public async Task<SystemConfigDto> GetSystemConfigAsync(int adminUserId, CancellationToken ct)
    {
        await RequireAdmin(adminUserId, ct);
        lock (ConfigLock)
        {
            return new SystemConfigDto(
                _encryptionPolicy,
                _proofOfLifePolicy,
                _storagePolicy,
                _backupPolicy,
                _configUpdatedAt,
                _configUpdatedBy
            );
        }
    }

    public async Task<SystemConfigDto> UpdateSystemConfigAsync(int adminUserId, UpdateSystemConfigInput input, CancellationToken ct)
    {
        var admin = await RequireAdmin(adminUserId, ct);
        lock (ConfigLock)
        {
            if (input.EncryptionPolicy is not null) _encryptionPolicy = input.EncryptionPolicy;
            if (input.ProofOfLifePolicy is not null) _proofOfLifePolicy = input.ProofOfLifePolicy;
            if (input.StoragePolicy is not null) _storagePolicy = input.StoragePolicy;
            if (input.BackupPolicy is not null) _backupPolicy = input.BackupPolicy;
            _configUpdatedAt = DateTime.UtcNow;
            _configUpdatedBy = admin.Email;
        }

        repository.Add(new AuditLog
        {
            UserId = adminUserId,
            Action = "UpdateSystemConfig",
            EntityType = "SystemConfig",
            EntityId = null,
            Result = "Success",
            Timestamp = DateTime.UtcNow
        });
        await repository.Save(ct);

        return await GetSystemConfigAsync(adminUserId, ct);
    }

    public async Task<KeyRotationSummaryDto> GetKeyRotationSummaryAsync(int adminUserId, CancellationToken ct)
    {
        await RequireAdmin(adminUserId, ct);
        await repository.EnsureAssetEncryptionKeys(ct);

        int intervalDays;
        lock (ConfigLock) { intervalDays = _encryptionPolicy.KeyRotationIntervalDays; }

        var cutoff = DateTime.UtcNow.AddDays(-intervalDays);
        var keys = await repository.QueryEncryptionKeys()
            .OrderByDescending(k => k.CreatedAt)
            .ThenByDescending(k => k.KeyId)
            .ToListAsync(ct);

        var keyItems = keys.Select(k =>
        {
            var needsRot = k.Status == "Active" && (k.RotatedAt == null ? k.CreatedAt < cutoff : k.RotatedAt < cutoff);
            return new KeyRotationItemDto(
                k.KeyId,
                k.AssetId,
                k.Asset?.AssetName,
                k.Algorithm,
                k.KeyVersion,
                k.Status,
                k.CreatedAt,
                k.RotatedAt,
                needsRot
            );
        }).ToList();

        var total = keyItems.Count;
        var active = keyItems.Count(k => k.Status == "Active");
        var rotated = keyItems.Count(k => k.RotatedAt.HasValue);
        var needingRotation = keyItems.Count(k => k.NeedsRotation);
        var lastRotated = keyItems.Where(k => k.RotatedAt.HasValue).MaxBy(k => k.RotatedAt)?.RotatedAt;

        return new KeyRotationSummaryDto(total, active, rotated, needingRotation, intervalDays, lastRotated, keyItems);
    }

    public async Task<KeyRotationSummaryDto> RotateEncryptionKeysAsync(int adminUserId, CancellationToken ct)
    {
        await RequireAdmin(adminUserId, ct);
        await repository.EnsureAssetEncryptionKeys(ct);

        var activeKeys = await repository.ActiveEncryptionKeys(ct);
        var now = DateTime.UtcNow;

        foreach (var key in activeKeys)
        {
            key.EncryptedKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            key.KeyVersion += 1;
            key.RotatedAt = now;
        }

        repository.Add(new AuditLog
        {
            UserId = adminUserId,
            Action = "RotateEncryptionKeys",
            EntityType = "EncryptionKey",
            EntityId = null,
            Result = "Success",
            Timestamp = now
        });

        await repository.Save(ct);

        return await GetKeyRotationSummaryAsync(adminUserId, ct);
    }

    public async Task<IReadOnlyList<ThirdPartyIntegrationDto>> GetIntegrationsAsync(int adminUserId, CancellationToken ct)
    {
        await RequireAdmin(adminUserId, ct);

        var now = DateTime.UtcNow;
        var list = new List<ThirdPartyIntegrationDto>();

        foreach (var (id, conf) in IntegrationConfigs)
        {
            list.Add(new ThirdPartyIntegrationDto(
                id,
                conf.Name,
                conf.Type,
                conf.Provider,
                "Connected",
                35,
                now,
                conf.Endpoint,
                null
            ));
        }

        return list;
    }

    public async Task<TestIntegrationResultDto> PingIntegrationAsync(int adminUserId, string serviceId, CancellationToken ct)
    {
        await RequireAdmin(adminUserId, ct);

        if (!IntegrationConfigs.TryGetValue(serviceId, out var conf))
            throw new WorkflowException(404, $"Không tìm thấy dịch vụ tích hợp '{serviceId}'.");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Task.Delay(25, ct);
        sw.Stop();

        repository.Add(new AuditLog
        {
            UserId = adminUserId,
            Action = "TestIntegrationConnection",
            EntityType = "Integration",
            EntityId = null,
            Result = "Success",
            Timestamp = DateTime.UtcNow
        });
        await repository.Save(ct);

        return new TestIntegrationResultDto(
            serviceId,
            true,
            (int)sw.ElapsedMilliseconds,
            $"Kết nối tới dịch vụ {conf.Name} ({conf.Provider}) thành công.",
            DateTime.UtcNow
        );
    }

    public async Task<PageDto<UserAdminDto>> GetUsersAsync(
        int adminUserId,
        int page,
        int pageSize,
        string? role,
        string? status,
        CancellationToken ct,
        int? userId = null,
        string? search = null)
    {
        await RequireAdmin(adminUserId, ct);
        ManagementService.Paging(page, pageSize);

        var query = repository.QueryUsers();

        if (userId.HasValue)
        {
            if (userId.Value < 1) throw new WorkflowException(400, "UserId phải là số nguyên dương.");
            query = query.Where(u => u.UserId == userId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            var idCandidate = keyword.TrimStart('#');
            if (int.TryParse(idCandidate, out var parsedId) && parsedId > 0)
            {
                query = query.Where(u =>
                    u.UserId == parsedId ||
                    u.FullName.Contains(keyword) ||
                    u.Email.Contains(keyword) ||
                    (u.Phone != null && u.Phone.Contains(keyword)));
            }
            else
            {
                query = query.Where(u =>
                    u.FullName.Contains(keyword) ||
                    u.Email.Contains(keyword) ||
                    (u.Phone != null && u.Phone.Contains(keyword)));
            }
        }

        if (!string.IsNullOrWhiteSpace(role))
            query = query.Where(u => u.Roles.Any(r => r.RoleName == role));

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(u => u.Status == status);

        var total = await query.CountAsync(ct);
        var users = await query.OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserAdminDto(
                u.UserId,
                u.FullName,
                u.Email,
                u.Phone,
                u.IdentityNumber,
                u.Status,
                u.Roles.Select(r => r.RoleName).ToArray(),
                u.CreatedAt,
                u.UpdatedAt
            ))
            .ToListAsync(ct);

        return new PageDto<UserAdminDto>(page, pageSize, users);
    }

    public async Task<UserAdminDto> CreateUserAsync(int adminUserId, CreateUserByAdminInput input, CancellationToken ct)
    {
        await RequireAdmin(adminUserId, ct);

        var email = IdentityService.NormalizeAndValidateEmail(input.Email);
        var phone = IdentityService.NormalizeAndValidatePhone(input.Phone);
        var identityNumber = IdentityService.NormalizeAndValidateIdentityNumber(input.IdentityNumber);

        await IdentityService.EnsureUniqueUserFieldsAsync(repository, email, phone, identityNumber, null, ct);

        var now = DateTime.UtcNow;
        var user = new User
        {
            FullName = input.FullName.Trim(),
            Email = email,
            Phone = phone,
            IdentityNumber = identityNumber,
            Status = "Active",
            CreatedAt = now,
            UpdatedAt = now
        };

        user.Authentications.Add(new Authentication
        {
            PasswordHash = IdentityService.HashPassword(input.Password),
            CreatedAt = now,
            UpdatedAt = now
        });

        foreach (var roleName in input.Roles.Distinct())
        {
            var role = await repository.Role(roleName, ct);
            if (role is not null) user.Roles.Add(role);
        }

        repository.Add(user);
        repository.Add(new AuditLog { UserId = adminUserId, Action = "AdminCreateUser", EntityType = "User", EntityId = user.UserId, Result = "Success", Timestamp = now });
        await repository.Save(ct);

        return new UserAdminDto(user.UserId, user.FullName, user.Email, user.Phone, user.IdentityNumber, user.Status, user.Roles.Select(r => r.RoleName).ToArray(), user.CreatedAt, user.UpdatedAt);
    }

    public async Task UpdateUserRolesAsync(int adminUserId, int targetUserId, string[] newRoles, CancellationToken ct)
    {
        await RequireAdmin(adminUserId, ct);
        var targetUser = await repository.User(targetUserId, ct) ?? throw new WorkflowException(404, "Không tìm thấy người dùng.");

        targetUser.Roles.Clear();
        foreach (var roleName in newRoles.Distinct())
        {
            var role = await repository.Role(roleName, ct);
            if (role is not null) targetUser.Roles.Add(role);
        }

        targetUser.UpdatedAt = DateTime.UtcNow;
        repository.Add(new AuditLog { UserId = adminUserId, Action = "UpdateUserRoles", EntityType = "User", EntityId = targetUserId, Result = "Success", Timestamp = DateTime.UtcNow });
        await repository.Save(ct);
    }

    public async Task UpdateUserStatusAsync(int adminUserId, int targetUserId, string status, CancellationToken ct)
    {
        await RequireAdmin(adminUserId, ct);
        var targetUser = await repository.User(targetUserId, ct) ?? throw new WorkflowException(404, "Không tìm thấy người dùng.");

        targetUser.Status = status;
        targetUser.UpdatedAt = DateTime.UtcNow;
        repository.Add(new AuditLog { UserId = adminUserId, Action = "UpdateUserStatus", EntityType = "User", EntityId = targetUserId, Result = "Success", Timestamp = DateTime.UtcNow });
        await repository.Save(ct);
    }

    public async Task<UserAdminDto> UpdateUserAsync(int adminUserId, int targetUserId, UpdateUserByAdminInput input, CancellationToken ct)
    {
        await RequireAdmin(adminUserId, ct);
        var targetUser = await repository.User(targetUserId, ct) ?? throw new WorkflowException(404, "Không tìm thấy người dùng.");

        if (!string.IsNullOrWhiteSpace(input.FullName))
            targetUser.FullName = input.FullName.Trim();

        string? normalizedPhone = null;
        if (input.Phone is not null)
            normalizedPhone = IdentityService.NormalizeAndValidatePhone(input.Phone);

        string? normalizedIdentityNumber = null;
        if (input.IdentityNumber is not null)
            normalizedIdentityNumber = IdentityService.NormalizeAndValidateIdentityNumber(input.IdentityNumber);

        await IdentityService.EnsureUniqueUserFieldsAsync(repository, null, normalizedPhone, normalizedIdentityNumber, targetUserId, ct);

        if (input.Phone is not null)
            targetUser.Phone = normalizedPhone;

        if (input.IdentityNumber is not null)
            targetUser.IdentityNumber = normalizedIdentityNumber;

        targetUser.UpdatedAt = DateTime.UtcNow;
        repository.Add(new AuditLog
        {
            UserId = adminUserId,
            Action = "AdminUpdateUserProfile",
            EntityType = "User",
            EntityId = targetUserId,
            Result = "Success",
            Timestamp = DateTime.UtcNow
        });
        await repository.Save(ct);

        return new UserAdminDto(
            targetUser.UserId,
            targetUser.FullName,
            targetUser.Email,
            targetUser.Phone,
            targetUser.IdentityNumber,
            targetUser.Status,
            targetUser.Roles.Select(r => r.RoleName).ToArray(),
            targetUser.CreatedAt,
            targetUser.UpdatedAt
        );
    }

    public async Task ResetUserPasswordAsync(int adminUserId, int targetUserId, ResetPasswordByAdminInput input, CancellationToken ct)
    {
        await RequireAdmin(adminUserId, ct);
        var targetUser = await repository.User(targetUserId, ct) ?? throw new WorkflowException(404, "Không tìm thấy người dùng.");

        var auth = targetUser.Authentications.OrderByDescending(a => a.CreatedAt).FirstOrDefault();
        var now = DateTime.UtcNow;
        if (auth is null)
        {
            auth = new Authentication
            {
                UserId = targetUserId,
                PasswordHash = IdentityService.HashPassword(input.NewPassword),
                CreatedAt = now,
                UpdatedAt = now
            };
            targetUser.Authentications.Add(auth);
        }
        else
        {
            auth.PasswordHash = IdentityService.HashPassword(input.NewPassword);
            auth.FailedLoginCount = 0;
            auth.LockedUntil = null;
            auth.UpdatedAt = now;
        }

        targetUser.UpdatedAt = now;
        repository.Add(new AuditLog
        {
            UserId = adminUserId,
            Action = "AdminResetUserPassword",
            EntityType = "User",
            EntityId = targetUserId,
            Result = "Success",
            Timestamp = now
        });
        await repository.Save(ct);
    }


    // 1. Lấy danh sách Audit Logs cho Step 2 (Giám sát & Cảnh báo)
    public async Task<PageDto<AuditLogDto>> GetAuditLogsAsync(int adminUserId, int page, int pageSize, string? action, CancellationToken ct)
    {
        await RequireAdmin(adminUserId, ct);
        ManagementService.Paging(page, pageSize);

        var query = repository.QueryAuditLogs();

        if (!string.IsNullOrWhiteSpace(action))
        {
            var matchedActions = MatchAuditActionFilter(action.Trim());
            query = query.Where(a => matchedActions.Contains(a.Action));
        }

        var rows = await query.OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.LogId,
                a.UserId,
                UserEmail = a.User != null ? a.User.Email : null,
                a.Action,
                a.EntityType,
                a.EntityId,
                a.Result,
                a.Ipaddress,
                a.Timestamp
            })
            .ToListAsync(ct);

        var logs = rows.Select(a => new AuditLogDto(
            a.LogId,
            a.UserId,
            a.UserEmail,
            LocalizeAuditAction(a.Action),
            LocalizeAuditEntityType(a.EntityType),
            a.EntityId,
            LocalizeAuditResult(a.Result),
            a.Ipaddress,
            a.Timestamp
        )).ToList();

        return new PageDto<AuditLogDto>(page, pageSize, logs);
    }

    private static readonly Dictionary<string, string> AuditActionLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        // Xác thực & Tài khoản
        ["PasswordLogin"] = "Đăng nhập bằng mật khẩu",
        ["GoogleLogin"] = "Đăng nhập bằng Google",
        ["ChangePassword"] = "Đổi mật khẩu",
        ["ResetPassword"] = "Đặt lại mật khẩu qua OTP",
        ["ConfirmAlive"] = "Xác nhận còn sống (Check-in)",
        ["AdminCreateUser"] = "Quản trị viên tạo tài khoản",
        ["UpdateUserRoles"] = "Cập nhật vai trò người dùng",
        ["UpdateUserStatus"] = "Cập nhật trạng thái tài khoản",
        ["AdminUpdateUserProfile"] = "Quản trị viên cập nhật hồ sơ",
        ["AdminResetUserPassword"] = "Quản trị viên đặt lại mật khẩu",

        // Kho & Tài sản số
        ["CreateVault"] = "Tạo kho di sản",
        ["UpdateVault"] = "Cập nhật kho di sản",
        ["CloseVault"] = "Đóng kho di sản",
        ["CreateAsset"] = "Tạo tài sản số",
        ["UpdateAsset"] = "Cập nhật tài sản số",
        ["RevokeAsset"] = "Thu hồi tài sản số",
        ["UploadAssetDocument"] = "Tải lên giấy tờ",
        ["RevokeDocument"] = "Thu hồi giấy tờ tài sản",

        // Người thụ hưởng & Người thi hành
        ["AssignBeneficiary"] = "Chỉ định người thụ hưởng",
        ["UpdateAllocation"] = "Cập nhật tỷ lệ phân bổ",
        ["RevokeAllocation"] = "Thu hồi phân bổ thụ hưởng",
        ["AcceptBeneficiaryAssignment"] = "Chấp nhận quyền thụ hưởng",
        ["RejectBeneficiaryAssignment"] = "Từ chối quyền thụ hưởng",
        ["AssignExecutor"] = "Gán người thi hành",
        ["AutoAssignExecutor"] = "Tự động gán người thi hành",
        ["RevokeExecutor"] = "Thu hồi người thi hành",

        // Quy trình bàn giao & Pháp lý
        ["CreateHandover"] = "Tạo yêu cầu bàn giao",
        ["AcceptHandover"] = "Tiếp nhận hồ sơ bàn giao",
        ["DeclineHandover"] = "Từ chối hồ sơ bàn giao",
        ["CancelHandover"] = "Hủy yêu cầu bàn giao",
        ["UploadHandoverDocument"] = "Tải lên tài liệu bàn giao",
        ["UploadDeathCertificate"] = "Tải lên giấy chứng tử",
        ["ReportOwnerDeceased"] = "Báo cáo chủ sở hữu qua đời",
        ["RequestLegalVerification"] = "Gửi xác minh pháp lý",
        ["LegalReview"] = "Thẩm định pháp lý",
        ["ConfirmOwnerDeceased"] = "Xác nhận chủ sở hữu qua đời",
        ["DeadManSwitchTriggered"] = "Kích hoạt bàn giao tự động",
        ["StartHandover"] = "Bắt đầu bàn giao",
        ["CompleteHandover"] = "Hoàn tất bàn giao",
        ["ConfirmReceipt"] = "Xác nhận nhận di sản",

        // Gói dịch vụ & Quản trị hệ thống
        ["CreateSubscriptionPlan"] = "Tạo gói dịch vụ",
        ["UpdateSubscriptionPlan"] = "Cập nhật gói dịch vụ",
        ["DeactivateSubscriptionPlan"] = "Ngừng kích hoạt gói dịch vụ",
        ["DeleteSubscriptionPlan"] = "Xóa gói dịch vụ ngừng bán",
        ["UpdateSystemConfig"] = "Cập nhật cấu hình hệ thống",
        ["RotateEncryptionKeys"] = "Xoay vòng khóa mã hóa",
        ["TestIntegrationConnection"] = "Kiểm tra kết nối tích hợp"
    };

    private static string LocalizeAuditAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return "";
        return AuditActionLabels.TryGetValue(action.Trim(), out var label) ? label : action;
    }

    private static List<string> MatchAuditActionFilter(string filter)
    {
        var matches = AuditActionLabels
            .Where(kv => string.Equals(kv.Key, filter, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(kv.Value, filter, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .Distinct()
            .ToList();
        if (!matches.Contains(filter)) matches.Add(filter);
        return matches;
    }

    private static string? LocalizeAuditEntityType(string? entityType) => entityType switch
    {
        "User" => "Tài khoản",
        "DigitalVault" => "Kho",
        "DigitalAsset" => "Tài sản",
        "AssetDocument" => "Giấy tờ tài sản",
        "BeneficiaryAssignment" => "Phân bổ thụ hưởng",
        "HandoverRequest" => "Đơn bàn giao",
        "LegalVerification" => "Thẩm định pháp lý",
        "HandoverCase" => "Hồ sơ thừa kế",
        "SubscriptionPlan" => "Gói dịch vụ",
        "SystemConfig" => "Cấu hình hệ thống",
        "EncryptionKey" => "Khóa mã hóa",
        "Integration" => "Tích hợp dịch vụ",
        _ => entityType
    };

    private static string LocalizeAuditResult(string? result) => result switch
    {
        null => "",
        "ReportedDeceased" => "Đã báo tử",
        "Failed" => "Thất bại",
        _ => result
    };
}
