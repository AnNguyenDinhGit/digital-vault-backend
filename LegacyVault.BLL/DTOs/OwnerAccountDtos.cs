using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace LegacyVault.BLL.DTOs;

// Nhật ký hoạt động của chính người dùng, cột theo bảng AUDIT_LOGS.
public sealed record OwnerActivityLogDto(
    long LogId,
    string Action,
    string? EntityType,
    int? EntityId,
    DateTime Timestamp,
    string? IpAddress,
    string Result);

// Hồ sơ cá nhân hiển thị trên trang Cài đặt bảo mật của Chủ sở hữu.
public sealed record OwnerProfileDto(
    int UserId,
    string FullName,
    string Email,
    string? Phone,
    string? IdentityNumber,
    bool TwoFactorEnabled,
    DateTime? PasswordChangedAt);

// Chỉ sửa họ tên và số điện thoại; Phone = null nghĩa là xóa số điện thoại.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class OwnerProfileInput
{
    [Required, StringLength(100)]
    public string FullName { get; init; } = null!;

    [StringLength(20)]
    public string? Phone { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class TwoFactorToggleInput
{
    [Required]
    public bool? Enabled { get; init; }
}

public sealed record TwoFactorStatusDto(bool TwoFactorEnabled);

