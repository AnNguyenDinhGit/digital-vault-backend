using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace LegacyVault.BLL.DTOs;

public sealed record UserAdminDto(
    int UserId,
    string FullName,
    string Email,
    string? Phone,
    string? IdentityNumber,
    string Status,
    string[] Roles,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateUserRolesInput
{
    [Required]
    public string[] Roles { get; init; } = Array.Empty<string>();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateUserStatusInput
{
    [Required, RegularExpression("^(Active|Disabled|Locked)$")]
    public string Status { get; init; } = null!;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateUserByAdminInput
{
    [Required, StringLength(100)] public string FullName { get; init; } = null!;
    [Required, EmailAddress, StringLength(150)] public string Email { get; init; } = null!;
    [Required, StringLength(1024, MinimumLength = 12)] public string Password { get; init; } = null!;
    [StringLength(20)] public string? Phone { get; init; }
    [StringLength(20)] public string? IdentityNumber { get; init; }
    [Required] public string[] Roles { get; init; } = Array.Empty<string>();
}

public sealed record AuditLogDto(
    long LogId,
    int? UserId,
    string? UserEmail,
    string Action,
    string? EntityType,
    int? EntityId,
    string Result,
    string? Ipaddress,
    DateTime Timestamp
);

public sealed record RolePermissionDto(
    int RoleId,
    string RoleName,
    string Description,
    string[] Permissions,
    int ActiveUsersCount
);

public sealed record SecurityAlertDto(
    string AlertId,
    string Severity,
    string Title,
    string Description,
    string? TargetUserEmail,
    string? IpAddress,
    DateTime DetectedAt,
    string Status
);

public sealed record SecurityDashboardDto(
    int TotalAuditLogs24h,
    int FailedActions24h,
    int ActiveAlertsCount,
    double FailureRatePercentage,
    IReadOnlyList<SecurityAlertDto> RecentAlerts,
    IReadOnlyList<KeyValuePair<string, int>> TopFailedActions
);

public sealed class EncryptionPolicyConfig
{
    public string DefaultAlgorithm { get; set; } = "AES-256-GCM";
    public int KeyRotationIntervalDays { get; set; } = 90;
    public int MinPasswordLength { get; set; } = 12;
    public bool Require2FAForAdmin { get; set; } = true;
}

public sealed class ProofOfLifePolicyConfig
{
    public int DefaultPingIntervalDays { get; set; } = 30;
    public int GracePeriodDays { get; set; } = 7;
    public int WarningDaysBeforeGrace { get; set; } = 3;
}

public sealed class StoragePolicyConfig
{
    public long MaxUploadSizeBytes { get; set; } = 10485760;
    public string[] AllowedExtensions { get; set; } = [".pdf", ".png", ".jpg", ".jpeg", ".docx"];
}

public sealed class BackupPolicyConfig
{
    public bool AutoBackupEnabled { get; set; } = true;
    public string CronSchedule { get; set; } = "0 0 * * *";
    public int RetentionDays { get; set; } = 30;
}

public sealed record SystemConfigDto(
    EncryptionPolicyConfig EncryptionPolicy,
    ProofOfLifePolicyConfig ProofOfLifePolicy,
    StoragePolicyConfig StoragePolicy,
    BackupPolicyConfig BackupPolicy,
    DateTime LastUpdatedAt,
    string UpdatedByAdminEmail
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateSystemConfigInput
{
    public EncryptionPolicyConfig? EncryptionPolicy { get; init; }
    public ProofOfLifePolicyConfig? ProofOfLifePolicy { get; init; }
    public StoragePolicyConfig? StoragePolicy { get; init; }
    public BackupPolicyConfig? BackupPolicy { get; init; }
}

public sealed record KeyRotationItemDto(
    int KeyId,
    int AssetId,
    string? AssetName,
    string Algorithm,
    int KeyVersion,
    string Status,
    DateTime CreatedAt,
    DateTime? RotatedAt,
    bool NeedsRotation
);

public sealed record KeyRotationSummaryDto(
    int TotalKeys,
    int ActiveKeys,
    int RotatedKeys,
    int KeysNeedingRotation,
    int RotationIntervalDays,
    DateTime? LastRotatedAt,
    IReadOnlyList<KeyRotationItemDto> Keys
);

public sealed record ThirdPartyIntegrationDto(
    string ServiceId,
    string ServiceName,
    string ServiceType,
    string Provider,
    string Status,
    int LatencyMs,
    DateTime LastCheckedAt,
    string EndpointUrl,
    string? ErrorMessage
);

public sealed record TestIntegrationResultDto(
    string ServiceId,
    bool Success,
    int LatencyMs,
    string Message,
    DateTime TestedAt
);
