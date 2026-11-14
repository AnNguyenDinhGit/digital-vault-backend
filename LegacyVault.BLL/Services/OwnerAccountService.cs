using System.Security.Cryptography;
using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Security;
using LegacyVault.DAL.Entities;
using LegacyVault.DAL.Repositories;
using LegacyVault.DAL.Storage;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.BLL.Services;

// Tài khoản của chính người dùng đang đăng nhập: nhật ký hoạt động, hồ sơ cá nhân và bật/tắt 2FA qua email.
public sealed class OwnerAccountService(
    IVaultRepository repository,
    IWorkflowRepository? workflow = null,
    IDocumentStore? store = null,
    DocumentProtection? protection = null)
{
    public const int MaxActivityLogs = 500;
    private const string EmailFactor = "Email";

    private static readonly Dictionary<string, string> ActionVietnameseLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PasswordLogin"] = "Đăng nhập",
        ["GoogleLogin"] = "Đăng nhập Google",
        ["Login"] = "Đăng nhập",
        ["Logout"] = "Đăng xuất",
        ["CreateVault"] = "Tạo kho",
        ["UpdateVault"] = "Chỉnh sửa kho",
        ["CloseVault"] = "Đóng kho",
        ["CreateAsset"] = "Tạo tài sản",
        ["UpdateAsset"] = "Chỉnh sửa tài sản",
        ["DeleteAsset"] = "Xóa tài sản",
        ["RevokeAsset"] = "Thu hồi tài sản",
        ["AssignBeneficiary"] = "Chỉ định người thụ hưởng",
        ["UpdateAllocation"] = "Sửa phân bổ người thụ hưởng",
        ["RemoveBeneficiary"] = "Xóa người thụ hưởng",
        ["RevokeAllocation"] = "Thu hồi phân bổ",
        ["AcceptBeneficiaryAssignment"] = "Chấp nhận thụ hưởng",
        ["RejectBeneficiaryAssignment"] = "Từ chối thụ hưởng",
        ["UploadAssetDocument"] = "Tải lên giấy tờ",
        ["DownloadDocument"] = "Tải giấy tờ về",
        ["RevokeDocument"] = "Thu hồi giấy tờ",
        ["UpdateProofOfLife"] = "Cập nhật cấu hình điểm danh",
        ["PingProofOfLife"] = "Điểm danh sự sống",
        ["ConfirmAlive"] = "Xác nhận còn sống",
        ["AssignExecutor"] = "Gán người thi hành",
        ["AutoAssignExecutor"] = "Tự động gán người thi hành",
        ["RevokeExecutor"] = "Thu hồi người thi hành",
        ["UpdateProfile"] = "Cập nhật thông tin cá nhân",
        ["ChangePassword"] = "Đổi mật khẩu",
        ["ResetPassword"] = "Đặt lại mật khẩu qua OTP",
        ["ToggleTwoFactor"] = "Đổi cài đặt xác thực hai lớp",
        ["EnableTwoFactor"] = "Bật xác thực hai lớp",
        ["DisableTwoFactor"] = "Tắt xác thực hai lớp",
        ["CreateHandoverRequest"] = "Tạo đơn bàn giao",
        ["CreateHandover"] = "Tạo đơn bàn giao",
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
        ["CreateSubscriptionPlan"] = "Tạo gói dịch vụ",
        ["UpdateSubscriptionPlan"] = "Cập nhật gói dịch vụ",
        ["DeactivateSubscriptionPlan"] = "Ngừng gói dịch vụ",
        ["DeleteSubscriptionPlan"] = "Xóa gói dịch vụ",
        ["UpdateSystemConfig"] = "Cập nhật cấu hình hệ thống",
        ["RotateEncryptionKeys"] = "Xoay vòng khóa mã hóa",
        ["TestIntegrationConnection"] = "Kiểm tra kết nối tích hợp",
        ["AdminCreateUser"] = "Admin tạo người dùng",
        ["UpdateUserRoles"] = "Admin đổi vai trò",
        ["UpdateUserStatus"] = "Admin đổi trạng thái",
        ["AdminUpdateUserProfile"] = "Admin sửa thông tin",
        ["AdminResetUserPassword"] = "Admin đặt lại mật khẩu"
    };

    public static string LocalizeAction(string action)
    {
        if (string.IsNullOrWhiteSpace(action)) return "";
        return ActionVietnameseLabels.TryGetValue(action.Trim(), out var label) ? label : action;
    }

    public async Task<List<OwnerActivityLogDto>> ActivityLogs(int userId, CancellationToken ct)
    {
        await ActiveUser(userId, ct);
        var rawLogs = await repository.QueryAuditLogs()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.Timestamp)
            .ThenByDescending(x => x.LogId)
            .Take(MaxActivityLogs)
            .Select(x => new
            {
                x.LogId,
                x.Action,
                x.EntityType,
                x.EntityId,
                x.Timestamp,
                x.Ipaddress,
                x.Result
            })
            .ToListAsync(ct);

        return rawLogs.Select(x => new OwnerActivityLogDto(
            x.LogId,
            LocalizeAction(x.Action),
            x.EntityType,
            x.EntityId,
            x.Timestamp,
            x.Ipaddress,
            x.Result
        )).ToList();
    }

    public async Task<OwnerProfileDto> Profile(int userId, CancellationToken ct) =>
        await ToProfile(await ActiveUser(userId, ct), ct);

    public async Task<OwnerProfileDto> UpdateProfile(int userId, OwnerProfileInput input, CancellationToken ct)
    {
        var fullName = input.FullName?.Trim();
        if (string.IsNullOrEmpty(fullName) || fullName.Length > 100)
            throw new WorkflowException(400, "Họ tên là bắt buộc (tối đa 100 ký tự).");

        var user = await ActiveUser(userId, ct);
        await EnsureNotDeceasedIfAvailable(userId, ct);
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
        await EnsureNotDeceasedIfAvailable(userId, ct);
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

    private async Task EnsureNotDeceasedIfAvailable(int userId, CancellationToken ct)
    {
        if (workflow is not null && store is not null && protection is not null)
            await HandoverEvidence.EnsureOwnerNotDeceased(userId, workflow, store, protection, ct);
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
