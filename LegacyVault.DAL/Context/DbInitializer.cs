using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using LegacyVault.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.DAL.Context;

public static class DbInitializer
{
    public static async Task SeedAsync(LegacyVaultDbContext db)
    {
        // 1. Khởi tạo danh sách Roles mặc định hệ thống nếu chưa có
        string[] defaultRoles = ["Admin", "Owner", "Executor", "Beneficiary", "LegalVerifier"];

        foreach (var roleName in defaultRoles)
        {
            if (!await db.Roles.AnyAsync(r => r.RoleName == roleName))
            {
                db.Roles.Add(new Role { RoleName = roleName });
            }
        }

        await db.SaveChangesAsync();

        // 2. Tạo tài khoản Admin hệ thống mặc định nếu chưa tồn tại
        var adminEmail = "admin@legacyvault.com";
        if (!await db.Users.AnyAsync(u => u.Email == adminEmail))
        {
            var adminRole = await db.Roles.SingleAsync(r => r.RoleName == "Admin");
            var now = DateTime.UtcNow;

            var adminUser = new User
            {
                FullName = "System Administrator",
                Email = adminEmail,
                Status = "Active",
                CreatedAt = now,
                UpdatedAt = now
            };

            adminUser.Roles.Add(adminRole);

            adminUser.Authentications.Add(new Authentication
            {
                PasswordHash = HashPassword("Admin@123456"),
                CreatedAt = now,
                UpdatedAt = now
            });

            db.Users.Add(adminUser);
            await db.SaveChangesAsync();
        }
    }

    // Thêm từ khóa 'static' ở đây để sửa lỗi CS0120 và CS0708
    private static string HashPassword(string password)
    {
        var salt = Encoding.UTF8.GetBytes("LegacyVaultAdminSalt2026!");
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100000, HashAlgorithmName.SHA256, 32);
        return $"PBKDF2-SHA256$100000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
}