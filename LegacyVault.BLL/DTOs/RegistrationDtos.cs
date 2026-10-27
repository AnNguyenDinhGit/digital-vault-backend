using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace LegacyVault.BLL.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RegisterInput
{
    [Required, StringLength(100)] public string FullName { get; init; } = null!;
    [Required, EmailAddress, StringLength(150)] public string Email { get; init; } = null!;
    [Required, StringLength(1024, MinimumLength = 12)] public string Password { get; init; } = null!;
    [Required, StringLength(1024)] public string ConfirmPassword { get; init; } = null!;
    [StringLength(20)] public string? Phone { get; init; }
    [StringLength(20)] public string? IdentityNumber { get; init; }
}

public sealed record RegistrationDto(int UserId, string FullName, string Email, string? Phone, string? IdentityNumber, string[] Roles);

public sealed record RegisterChallengeDto(
    string ChallengeId,
    int ExpiresInSeconds,
    string Message
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ConfirmRegisterInput
{
    [Required, StringLength(32, MinimumLength = 32)]
    public string ChallengeId { get; init; } = null!;

    [Required, RegularExpression("^[0-9]{6}$")]
    public string Code { get; init; } = null!;
}

public sealed class UpdateProfileInput
{
    [StringLength(100)]
    public string? FullName { get; init; }

    [StringLength(20)]
    public string? Phone { get; init; }

    [StringLength(20)]
    public string? IdentityNumber { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ChangePasswordRequestInput
{
    [Required, StringLength(1024)]
    public string OldPassword { get; init; } = null!;

    [Required, StringLength(1024, MinimumLength = 12)]
    public string NewPassword { get; init; } = null!;

    [Required, StringLength(1024)]
    public string ConfirmPassword { get; init; } = null!;
}

public sealed record ChangePasswordChallengeDto(
    string ChallengeId,
    int ExpiresInSeconds,
    string Message
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ConfirmChangePasswordInput
{
    [Required, StringLength(32, MinimumLength = 32)]
    public string ChallengeId { get; init; } = null!;

    [Required, RegularExpression("^[0-9]{6}$")]
    public string Code { get; init; } = null!;
}

