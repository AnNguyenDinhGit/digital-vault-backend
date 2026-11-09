using System.Security.Cryptography;
using LegacyVault.BLL.DTOs;
using LegacyVault.DAL.Entities;
using LegacyVault.DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.BLL.Services;

// Tài khoản của chính người dùng đang đăng nhập: nhật ký hoạt động, hồ sơ cá nhân và bật/tắt 2FA qua email.
public sealed class OwnerAccountService(IVaultRepository repository)
{
    public const int MaxActivityLogs = 500;
    private const string EmailFactor = "Email";

    public async Task<List<OwnerActivityLogDto>> ActivityLogs(int userId, CancellationToken ct)
    {
        await ActiveUser(userId, ct);
        return await repository.QueryAuditLogs()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.Timestamp)
            .ThenByDescending(x => x.LogId)
            .Take(MaxActivityLogs)
            .Select(x => new OwnerActivityLogDto(x.LogId, x.Action, x.EntityType, x.EntityId, x.Timestamp, x.Ipaddress, x.Result))
            .ToListAsync(ct);
    }

    public async Task<OwnerProfileDto> Profile(int userId, CancellationToken ct) =>
        await ToProfile(await ActiveUser(userId, ct), ct);

    public async Task<OwnerProfileDto> UpdateProfile(int userId, OwnerProfileInput input, CancellationToken ct)
    {
        var fullName = input.FullName?.Trim();
        if (string.IsNullOrEmpty(fullName) || fullName.Length > 100)
            throw new WorkflowException(400, "Họ tên là bắt buộc (tối đa 100 ký tự).");

        var user = await ActiveUser(userId, ct);
        var phone = IdentityService.NormalizeAndValidatePhone(input.Phone);
        if (phone is not null)
            await IdentityService.EnsureUniqueUserFieldsAsync(repository, null, phone, null, userId, ct);

        var now = DateTime.UtcNow;
        user.FullName = fullName;
        user.Phone = phone;
        user.UpdatedAt = now;
        AddAudit(userId, "UpdateProfile", now);
        await repository.Save(ct);
        return await ToProfile(user, ct);
    }

    public async Task<TwoFactorStatusDto> SetTwoFactor(int userId, TwoFactorToggleInput input, CancellationToken ct)
    {
        if (input.Enabled is not bool enabled)
            throw new WorkflowException(400, "Trường enabled phải là true hoặc false.");

        var user = await ActiveUser(userId, ct);
        var active = user.TwoFactorAuths.Where(x => x.IsEnabled).ToList();
        var now = DateTime.UtcNow;

        if (enabled)
        {
            // Đã có đúng một yếu tố đang bật (Email hoặc TOTP) thì giữ nguyên, tránh bật song song nhiều yếu tố.
            if (active.Count > 0) return new(true);
            var factor = user.TwoFactorAuths.FirstOrDefault(x => x.Method == EmailFactor);
            if (factor is null)
            {
                factor = new TwoFactorAuth
                {
                    UserId = userId,
                    Method = EmailFactor,
                    Secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
                    CreatedAt = now
                };
                user.TwoFactorAuths.Add(factor);
            }
            factor.IsEnabled = true;
            factor.UpdatedAt = now;
            AddAudit(userId, "EnableTwoFactor", now);
        }
        else
        {
            if (active.Count == 0) return new(false);
            // TOTP do quản trị viên cấu hình; người dùng chỉ tự tắt được xác thực qua email.
            if (active.Any(x => x.Method != EmailFactor))
                throw new WorkflowException(409, "Xác thực hai lớp bằng ứng dụng (TOTP) do quản trị viên cấu hình, không thể tự tắt.");
            foreach (var factor in active)
            {
                factor.IsEnabled = false;
                factor.UpdatedAt = now;
            }
            AddAudit(userId, "DisableTwoFactor", now);
        }

        user.UpdatedAt = now;
        await repository.Save(ct);
        return new(enabled);
    }

    private async Task<User> ActiveUser(int userId, CancellationToken ct)
    {
        var user = await repository.User(userId, ct);
        if (user is null || user.Status != "Active")
            throw new WorkflowException(404, "Không tìm thấy tài khoản người dùng.");
        return user;
    }

    private async Task<OwnerProfileDto> ToProfile(User user, CancellationToken ct)
    {
        var passwordChangedAt = await repository.QueryAuditLogs()
            .Where(x => x.UserId == user.UserId && x.Action == "ChangePassword" && x.Result == "Success")
            .OrderByDescending(x => x.Timestamp)
            .Select(x => (DateTime?)x.Timestamp)
            .FirstOrDefaultAsync(ct);
        return new(user.UserId, user.FullName, user.Email, user.Phone, user.IdentityNumber,
            user.TwoFactorAuths.Any(x => x.IsEnabled), passwordChangedAt);
    }

    private void AddAudit(int userId, string action, DateTime now) =>
        repository.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            EntityType = "User",
            EntityId = userId,
            Result = "Success",
            Timestamp = now
        });
}

