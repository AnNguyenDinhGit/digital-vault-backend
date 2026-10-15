using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace LegacyVault.BLL.DTOs;

public sealed record TokenDto(
    int UserId,
    string[] Roles,
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string? RefreshToken = null,
    DateTime? RefreshTokenExpiresAt = null
);

public sealed record MeDto(int UserId, string FullName, string Email, string[] Roles, string Status);
public sealed record LoginChallengeDto(string ChallengeId, string Method, DateTime ExpiresAt, bool RequiresTwoFactor = true);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FactorInput([Required, StringLength(32, MinimumLength = 32)] string ChallengeId,
    [Required, RegularExpression("^[0-9]{6}$")] string Code);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FactorResendInput([Required, StringLength(32, MinimumLength = 32)] string ChallengeId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RefreshTokenInput([Required] string RefreshToken);

public interface IAccessTokenIssuer { TokenDto Issue(LoginDto login); }