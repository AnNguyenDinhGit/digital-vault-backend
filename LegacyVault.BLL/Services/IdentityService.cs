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
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600000, HashAlgorithmName.SHA256, 32);
        return $"PBKDF2-SHA256$600000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
    public async Task<RegistrationDto> Register(RegisterInput input, CancellationToken ct)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), errors, true))
            throw new WorkflowException(400, "Invalid registration input.");
        var name = input.FullName.Trim();
        var email = input.Email.Trim().ToLowerInvariant();
        if (name.Length == 0 || !new EmailAddressAttribute().IsValid(email) || input.Password != input.ConfirmPassword)
            throw new WorkflowException(400, "Full name/email is invalid or passwords do not match.");
        if (await repository.LoginUser(email, ct) is not null) throw new WorkflowException(409, "Email is already registered.");
        // Public registration always creates an owner; privileged accounts are provisioned separately.
        var now = DateTime.UtcNow;
        var user = new User { FullName = name, Email = email, Phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim(),
            Status = "Active", CreatedAt = now, UpdatedAt = now };
        user.Authentications.Add(new Authentication { PasswordHash = HashPassword(input.Password), CreatedAt = now, UpdatedAt = now });
        // DAL initializes the system-defined owner role if missing and saves the entire graph atomically.
        try { await repository.Register(user, VaultService.OwnerRole, ct); }
        catch (RepositoryDuplicateException) { throw new WorkflowException(409, "Email is already registered."); }
        return new RegistrationDto(user.UserId, user.FullName, user.Email, user.Roles.Select(x => x.RoleName).ToArray());
    }
    // Password format: PBKDF2-SHA256$iterations$base64Salt$base64Hash.
    public static bool VerifyPassword(string password, string encoded)
    {
        try
        {
            var parts = encoded.Split('$');
            if (parts.Length != 4 || parts[0] != "PBKDF2-SHA256" || !int.TryParse(parts[1], out var iterations) || iterations is < 100000 or > 1000000) return false;
            var salt = Convert.FromBase64String(parts[2]); var expected = Convert.FromBase64String(parts[3]);
            if (salt.Length < 16 || expected.Length != 32) return false;
            return CryptographicOperations.FixedTimeEquals(expected, Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32));
        }
        catch (FormatException) { return false; }
    }
    public async Task<LoginDto> Login(string email, string password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 150 || string.IsNullOrEmpty(password) || password.Length > 1024)
            throw new WorkflowException(400, "Invalid login input.");
        var user = await repository.LoginUser(email.Trim(), ct);
        var auth = user?.Authentications.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        if (user is null || user.Status != "Active" || auth is null || auth.LockedUntil > DateTime.UtcNow)
            throw new WorkflowException(401, "Invalid credentials or account unavailable.");
        if (!VerifyPassword(password, auth.PasswordHash))
        {
            auth.FailedLoginCount++;
            if (auth.FailedLoginCount >= 5) { auth.LockedUntil = DateTime.UtcNow.AddMinutes(15); auth.FailedLoginCount = 0; }
            auth.UpdatedAt = DateTime.UtcNow;
            await repository.Save(ct);
            throw new WorkflowException(401, "Invalid credentials or account unavailable.");
        }
        auth.FailedLoginCount = 0; auth.LastLoginAt = DateTime.UtcNow; auth.UpdatedAt = DateTime.UtcNow;
        await repository.Save(ct);
        return new LoginDto(user.UserId, user.Roles.Select(x => x.RoleName).ToArray());
    }
    public async Task<string> BeneficiaryEmail(int userId, CancellationToken ct)
    {
        var user = await repository.User(userId, ct);
        if (user is null || user.Status != "Active" || !user.Roles.Any(x => x.RoleName == VaultService.BeneficiaryRole))
            throw new WorkflowException(403, "Active beneficiary required.");
        return user.Email;
    }
}
