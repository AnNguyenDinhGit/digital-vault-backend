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
