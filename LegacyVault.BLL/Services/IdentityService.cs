
using System.Security.Cryptography;
using System.ComponentModel.DataAnnotations;
using LegacyVault.BLL.DTOs;
using LegacyVault.DAL.Entities;
using LegacyVault.DAL.Repositories;

namespace LegacyVault.BLL.Services;

public sealed class IdentityService(IVaultRepository repository)
{
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
        if (string.IsNullOrWhiteSpace(email) ||
            email.Length > 150 ||
            string.IsNullOrEmpty(password) ||
            password.Length > 1024)
        {
            throw new WorkflowException(
                400,
                "Invalid login input.");
        }

        var user = await repository.LoginUser(
            email.Trim(),
            ct);

        var auth = user?.Authentications
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();

        if (user is null ||
            user.Status != "Active" ||
            auth is null ||
            auth.LockedUntil > DateTime.UtcNow)
        {
            throw new WorkflowException(
                401,
                "Invalid credentials or account unavailable.");
        }

        if (!VerifyPassword(password, auth.PasswordHash))
        {
            auth.FailedLoginCount++;

            if (auth.FailedLoginCount >= 5)
            {
                auth.LockedUntil =
                    DateTime.UtcNow.AddMinutes(15);

                auth.FailedLoginCount = 0;
            }

            auth.UpdatedAt = DateTime.UtcNow;

            await repository.Save(ct);

            throw new WorkflowException(
                401,
                "Invalid credentials or account unavailable.");
        }

        auth.FailedLoginCount = 0;
        auth.LastLoginAt = DateTime.UtcNow;
        auth.UpdatedAt = DateTime.UtcNow;

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

    // Google Login - merged from branch dung
    // verifiedEmail must come from a Google token
    // that has already been validated by the backend.
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

        // Create new account if Google email
        // does not exist in database
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

            // Google accounts do not require a password.
            // Assign Owner role automatically.
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

        // Check account status and permissions
        if (user.Status != "Active" ||
            user.Authentications.Any(x =>
                x.LockedUntil > DateTime.UtcNow) ||
            user.Roles.Any(x =>
                x.RoleName is VaultService.AdminRole
                    or VaultService.ExecutorRole
                    or VaultService.VerifierRole))
        {
            throw new WorkflowException(
                403,
                "This account cannot use public Google sign-in.");
        }

        return new LoginDto(
            user.UserId,
            user.Roles.Select(x => x.RoleName).ToArray());
    }
}
