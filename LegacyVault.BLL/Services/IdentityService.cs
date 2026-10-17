using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using LegacyVault.BLL.DTOs;
using LegacyVault.DAL.Entities;
using LegacyVault.DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.BLL.Services;

public sealed class IdentityService(IVaultRepository repository)
{
    /// <summary>
    /// Phát hành cả AccessToken và RefreshToken (lưu vào DB) cho người dùng sau khi xác thực thành công.
    /// </summary>
    public async Task<TokenDto> GenerateAuthResponseAsync(User user, IAccessTokenIssuer issuer, CancellationToken ct)
    {
        var loginDto = new LoginDto(user.UserId, user.Roles.Select(r => r.RoleName).ToArray());

        var refreshTokenStr = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var refreshToken = new RefreshToken
        {
            UserId = user.UserId,
            Token = refreshTokenStr,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow,
            IsRevoked = false
        };

        repository.Add(refreshToken);
        await repository.Save(ct);

        return issuer.Issue(loginDto) with
        {
            RefreshToken = refreshTokenStr,
            RefreshTokenExpiresAt = refreshToken.ExpiresAt
        };
    }

    /// <summary>
    /// Xoay vòng (Rotate) Refresh Token khi cấp lại Access Token mới.
    /// </summary>
    public async Task<TokenDto> RefreshAccessTokenAsync(string refreshTokenStr, IAccessTokenIssuer issuer, CancellationToken ct)
    {
        var token = await repository.GetRefreshTokenAsync(refreshTokenStr, ct);
        if (token is null || token.IsRevoked || token.ExpiresAt <= DateTime.UtcNow)
        {
            throw new WorkflowException(401, "Refresh Token không hợp lệ hoặc đã hết hạn.");
        }

        var user = await repository.User(token.UserId, ct);
        if (user is null || user.Status != "Active" || user.Authentications.Any(a => a.LockedUntil > DateTime.UtcNow))
        {
            throw new WorkflowException(401, "Tài khoản không khả dụng.");
        }

        // Revoke token cũ (Token Rotation)
        token.IsRevoked = true;
        token.UpdatedAt = DateTime.UtcNow;

        // Tạo Refresh Token mới
        var newRefreshTokenStr = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var newRefreshToken = new RefreshToken
        {
            UserId = user.UserId,
            Token = newRefreshTokenStr,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow,
            IsRevoked = false
        };

        token.ReplacedByToken = newRefreshTokenStr;

        repository.Add(newRefreshToken);
        await repository.Save(ct);

        // Phát hành AccessToken mới
        var loginDto = new LoginDto(user.UserId, user.Roles.Select(r => r.RoleName).ToArray());
        var tokenResult = issuer.Issue(loginDto);

        return tokenResult with
        {
            RefreshToken = newRefreshTokenStr,
            RefreshTokenExpiresAt = newRefreshToken.ExpiresAt
        };
    }

    /// <summary>
    /// Thu hồi Refresh Token khi Logout.
    /// </summary>
    public async Task RevokeRefreshTokenAsync(string refreshTokenStr, CancellationToken ct)
    {
        var token = await repository.GetRefreshTokenAsync(refreshTokenStr, ct);
        if (token is not null && !token.IsRevoked)
        {
            token.IsRevoked = true;
            token.UpdatedAt = DateTime.UtcNow;
            await repository.Save(ct);
        }
    }

    private static readonly Lazy<string> UnavailableAccountHash = new(() => HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            600000,
            HashAlgorithmName.SHA256,
            32
        );

        return $"PBKDF2-SHA256$600000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public async Task<RegistrationDto> Register(
        RegisterInput input,
        CancellationToken ct)
    {
        var errors = new List<ValidationResult>();

        if (!Validator.TryValidateObject(
                input,
                new ValidationContext(input),
                errors,
                true))
        {
            throw new WorkflowException(
                400,
                "Invalid registration input.");
        }

        var name = input.FullName.Trim();
        var email = input.Email.Trim().ToLowerInvariant();

        if (name.Length == 0 ||
            !new EmailAddressAttribute().IsValid(email) ||
            input.Password != input.ConfirmPassword)
        {
            throw new WorkflowException(
                400,
                "Full name/email is invalid or passwords do not match.");
        }

        if (await repository.LoginUser(email, ct) is not null)
        {
            throw new WorkflowException(
                409,
                "Email is already registered.");
        }

        var now = DateTime.UtcNow;

        var user = new User
        {
            FullName = name,
            Email = email,
            Phone = string.IsNullOrWhiteSpace(input.Phone)
                ? null
                : input.Phone.Trim(),
            IdentityNumber = string.IsNullOrWhiteSpace(input.IdentityNumber)
                ? null
                : input.IdentityNumber.Trim(),
            Status = "Active",
            CreatedAt = now,
            UpdatedAt = now
        };

        user.Authentications.Add(new Authentication
        {
            PasswordHash = HashPassword(input.Password),
            CreatedAt = now,
            UpdatedAt = now
        });

        try
        {
            await repository.Register(
                user,
                VaultService.OwnerRole,
                ct);
        }
        catch (RepositoryDuplicateException)
        {
            throw new WorkflowException(
                409,
                "Email is already registered.");
        }

        return new RegistrationDto(
            user.UserId,
            user.FullName,
            user.Email,
            user.Phone,
            user.IdentityNumber,
            user.Roles.Select(x => x.RoleName).ToArray()
        );
    }

    // Update user profile
    public async Task<bool> UpdateProfile(
        int userId,
        UpdateProfileInput input,
        CancellationToken ct)
    {
        var errors = new List<ValidationResult>();

        if (!Validator.TryValidateObject(
                input,
                new ValidationContext(input),
                errors,
                true))
        {
            throw new WorkflowException(
                400,
                "Invalid profile update input.");
        }

        var user = await repository.User(userId, ct);

        if (user is null || user.Status != "Active")
        {
            throw new WorkflowException(
                404,
                "User not found or inactive.");
        }

        if (!string.IsNullOrWhiteSpace(input.FullName))
        {
            user.FullName = input.FullName.Trim();
        }

        if (input.Phone != null)
        {
            user.Phone = string.IsNullOrWhiteSpace(input.Phone)
                ? null
                : input.Phone.Trim();
        }

        if (input.IdentityNumber != null)
        {
            user.IdentityNumber =
                string.IsNullOrWhiteSpace(input.IdentityNumber)
                    ? null
                    : input.IdentityNumber.Trim();
        }

        user.UpdatedAt = DateTime.UtcNow;

        await repository.Save(ct);

        return true;
    }

    // Verify password using PBKDF2-SHA256
    public static bool VerifyPassword(
        string password,
        string encoded)
    {
        try
        {
            var parts = encoded.Split('$');

            if (parts.Length != 4 ||
                parts[0] != "PBKDF2-SHA256" ||
                !int.TryParse(parts[1], out var iterations) ||
                iterations is < 100000 or > 1000000)
            {
                return false;
            }

            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);

            if (salt.Length < 16 || expected.Length != 32)
            {
                return false;
            }

            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                32
            );

            return CryptographicOperations.FixedTimeEquals(
                expected,
                actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    // Login with email and password
    public async Task<LoginDto> Login(
        string email,
        string password,
        CancellationToken ct)
    {
        var login = await CheckPassword(email, password, ct);
        var user = await repository.User(login.UserId, ct);
        if (user is null) throw new WorkflowException(401, "Account unavailable.");
        if (user.TwoFactorAuths.Any(x => x.IsEnabled)) throw new WorkflowException(409, "Two-factor authentication required.");
        return await CompleteLogin(login.UserId, false, ct);
    }

    public async Task<LoginDto> CheckPassword(string email, string password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 150 || string.IsNullOrEmpty(password) || password.Length > 1024)
            throw new WorkflowException(400, "Invalid login input.");
        var user = await repository.LoginUser(email.Trim(), ct);
        var auth = user?.Authentications.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        if (user is null || user.Status != "Active" || auth is null || user.Authentications.Any(x => x.LockedUntil > DateTime.UtcNow))
        {
            // Keep unavailable-account failures on the password hashing path as well.
            _ = VerifyPassword(password, UnavailableAccountHash.Value);
            throw new WorkflowException(401, "Invalid credentials or account unavailable.");
        }
        if (!VerifyPassword(password, auth.PasswordHash))
        {
            auth.FailedLoginCount++;
            if (auth.FailedLoginCount >= 5) { auth.LockedUntil = DateTime.UtcNow.AddMinutes(15); auth.FailedLoginCount = 0; }
            auth.UpdatedAt = RecordVersion.Next(auth.UpdatedAt);
            await repository.Save(ct);

            throw new WorkflowException(
                401,
                "Invalid credentials or account unavailable.");
        }
        auth.FailedLoginCount = 0;
        auth.UpdatedAt = RecordVersion.Next(auth.UpdatedAt);
        await repository.Save(ct);

        return new LoginDto(
            user.UserId,
            user.Roles.Select(x => x.RoleName).ToArray());
    }

    // Get beneficiary email
    public async Task<string> BeneficiaryEmail(
        int userId,
        CancellationToken ct)
    {
        var user = await repository.User(userId, ct);

        if (user is null ||
            user.Status != "Active" ||
            !user.Roles.Any(x =>
                x.RoleName == VaultService.BeneficiaryRole))
        {
            throw new WorkflowException(
                403,
                "Active beneficiary required.");
        }

        return user.Email;
    }

    public async Task<MeDto> Me(int userId, CancellationToken ct)
    {
        var user = await repository.User(userId, ct);
        if (user is null || user.Status != "Active" || user.Authentications.Any(x => x.LockedUntil > DateTime.UtcNow))
            throw new WorkflowException(401, "Account unavailable.");
        return new(user.UserId, user.FullName, user.Email, user.Roles.Select(x => x.RoleName).ToArray(), user.Status);
    }

    // Persist success only after every mandatory factor has completed, before issuing a token.
    public async Task<LoginDto> CompleteLogin(int userId, bool google, CancellationToken ct)
    {
        var user = await repository.User(userId, ct);
        if (user is null || user.Status != "Active" || user.Authentications.Any(x => x.LockedUntil > DateTime.UtcNow))
            throw new WorkflowException(401, "Account unavailable.");
        if (google) RequireGoogleAccount(user);
        var auth = user.Authentications.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        if (auth is not null)
        {
            auth.LastLoginAt = DateTime.UtcNow;
            auth.FailedLoginCount = 0;
            auth.UpdatedAt = RecordVersion.Next(auth.UpdatedAt);
        }
        repository.Add(new AuditLog
        {
            UserId = userId,
            Action = google ? "GoogleLogin" : "PasswordLogin",
            EntityType = "User",
            EntityId = userId,
            Timestamp = DateTime.UtcNow,
            Result = "Success"
        });
        await repository.Save(ct);
        return new(user.UserId, user.Roles.Select(x => x.RoleName).ToArray());
    }

    public static void RequireGoogleAccount(User user)
    {
        if (user.Status != "Active" || user.Authentications.Any(x => x.LockedUntil > DateTime.UtcNow) ||
            user.Roles.Any(x => x.RoleName is not (VaultService.OwnerRole or VaultService.BeneficiaryRole)))
            throw new WorkflowException(403, "This account cannot use public Google sign-in.");
    }

    // Google Login
    public async Task<LoginDto> GoogleLogin(
        string verifiedEmail,
        string fullName,
        CancellationToken ct)
    {
        var email = verifiedEmail.Trim().ToLowerInvariant();

        if (email.Length > 150 ||
            !new EmailAddressAttribute().IsValid(email))
        {
            throw new WorkflowException(
                400,
                "Invalid Google email.");
        }

        var user = await repository.LoginUser(email, ct);

        // Create new account if Google email does not exist in database
        if (user is null)
        {
            var name = fullName.Trim();

            if (string.IsNullOrEmpty(name))
            {
                name = email;
            }

            if (name.Length > 100)
            {
                name = name[..100];
            }

            var now = DateTime.UtcNow;

            user = new User
            {
                Email = email,
                FullName = name,
                Status = "Active",
                CreatedAt = now,
                UpdatedAt = now
            };

            try
            {
                await repository.Register(
                    user,
                    VaultService.OwnerRole,
                    ct);
            }
            catch (RepositoryDuplicateException)
            {
                throw new WorkflowException(
                    409,
                    "Email was registered concurrently. Start Google sign-in again.");
            }
        }

        RequireGoogleAccount(user);
        return new LoginDto(user.UserId, user.Roles.Select(x => x.RoleName).ToArray());
    }
}
