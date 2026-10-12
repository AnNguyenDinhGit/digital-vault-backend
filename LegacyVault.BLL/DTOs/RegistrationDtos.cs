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

public sealed class UpdateProfileInput
{
    [StringLength(100)]
    public string? FullName { get; init; }

    [StringLength(20)]
    public string? Phone { get; init; }

    [StringLength(20)]
    public string? IdentityNumber { get; init; }
}
