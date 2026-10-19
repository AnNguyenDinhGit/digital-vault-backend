using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using LegacyVault.BLL.DTOs;
using LegacyVault.DAL.Entities;
using LegacyVault.DAL.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.BLL.Services;

public sealed class AdminService(IVaultRepository repository)
{
    private async Task RequireAdmin(int adminUserId, CancellationToken ct)
    {
        var user = await repository.User(adminUserId, ct);
        if (user is null || user.Status != "Active" || !user.Roles.Any(r => r.RoleName == VaultService.AdminRole))
            throw new WorkflowException(403, "Quyền Admin là bắt buộc.");
    }

    public async Task<PageDto<UserAdminDto>> GetUsersAsync(int adminUserId, int page, int pageSize, string? role, string? status, CancellationToken ct)
    {
        await RequireAdmin(adminUserId, ct);
        ManagementService.Paging(page, pageSize);

        var query = repository.QueryUsers(); // Giả định có Queryable từ Repository

        if (!string.IsNullOrWhiteSpace(role))
            query = query.Where(u => u.Roles.Any(r => r.RoleName == role));

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(u => u.Status == status);

        var total = await query.CountAsync(ct);
        var users = await query.OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new UserAdminDto(
                u.UserId,
                u.FullName,
                u.Email,
                u.Phone,
                u.IdentityNumber,
                u.Status,
                u.Roles.Select(r => r.RoleName).ToArray(),
                u.CreatedAt,
                u.UpdatedAt
            ))
            .ToListAsync(ct);

        return new PageDto<UserAdminDto>(page, pageSize, users);
    }

    public async Task<UserAdminDto> CreateUserAsync(int adminUserId, CreateUserByAdminInput input, CancellationToken ct)
    {
        await RequireAdmin(adminUserId, ct);

        var email = input.Email.Trim().ToLowerInvariant();
        if (await repository.LoginUser(email, ct) is not null)
            throw new WorkflowException(409, "Email đã được sử dụng.");

        var now = DateTime.UtcNow;
        var user = new User
        {
            FullName = input.FullName.Trim(),
            Email = email,
            Phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim(),
            IdentityNumber = string.IsNullOrWhiteSpace(input.IdentityNumber) ? null : input.IdentityNumber.Trim(),
            Status = "Active",
            CreatedAt = now,
            UpdatedAt = now
        };

        user.Authentications.Add(new Authentication
        {
            PasswordHash = IdentityService.HashPassword(input.Password),
            CreatedAt = now,
            UpdatedAt = now
        });

        foreach (var roleName in input.Roles.Distinct())
        {
            var role = await repository.Role(roleName, ct);
            if (role is not null) user.Roles.Add(role);
        }

        repository.Add(user);
        repository.Add(new AuditLog { UserId = adminUserId, Action = "AdminCreateUser", EntityType = "User", EntityId = user.UserId, Result = "Success", Timestamp = now });
        await repository.Save(ct);

        return new UserAdminDto(user.UserId, user.FullName, user.Email, user.Phone, user.IdentityNumber, user.Status, user.Roles.Select(r => r.RoleName).ToArray(), user.CreatedAt, user.UpdatedAt);
    }

    public async Task UpdateUserRolesAsync(int adminUserId, int targetUserId, string[] newRoles, CancellationToken ct)
    {
        await RequireAdmin(adminUserId, ct);
        var targetUser = await repository.User(targetUserId, ct) ?? throw new WorkflowException(404, "Không tìm thấy người dùng.");

        targetUser.Roles.Clear();
        foreach (var roleName in newRoles.Distinct())
        {
            var role = await repository.Role(roleName, ct);
            if (role is not null) targetUser.Roles.Add(role);
        }

        targetUser.UpdatedAt = DateTime.UtcNow;
        repository.Add(new AuditLog { UserId = adminUserId, Action = "UpdateUserRoles", EntityType = "User", EntityId = targetUserId, Result = "Success", Timestamp = DateTime.UtcNow });
        await repository.Save(ct);
    }

    public async Task UpdateUserStatusAsync(int adminUserId, int targetUserId, string status, CancellationToken ct)
    {
        await RequireAdmin(adminUserId, ct);
        var targetUser = await repository.User(targetUserId, ct) ?? throw new WorkflowException(404, "Không tìm thấy người dùng.");

        targetUser.Status = status;
        targetUser.UpdatedAt = DateTime.UtcNow;
        repository.Add(new AuditLog { UserId = adminUserId, Action = "UpdateUserStatus", EntityType = "User", EntityId = targetUserId, Result = "Success", Timestamp = DateTime.UtcNow });
        await repository.Save(ct);
    }


    // 1. Lấy danh sách Audit Logs cho Step 2 (Giám sát & Cảnh báo)
    // 1. Lấy danh sách Audit Logs cho Step 2 (Giám sát & Cảnh báo)
    public async Task<PageDto<AuditLogDto>> GetAuditLogsAsync(int adminUserId, int page, int pageSize, string? action, CancellationToken ct)
    {
        await RequireAdmin(adminUserId, ct);
        ManagementService.Paging(page, pageSize);

        var query = repository.QueryAuditLogs();

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action == action);

        var total = await query.CountAsync(ct);
        var logs = await query.OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogDto(
                a.LogId,
                a.UserId,
                a.User != null ? a.User.Email : null,
                a.Action,
                a.EntityType,
                a.EntityId,
                a.Result,
                a.Ipaddress,
                a.Timestamp
            ))
            .ToListAsync(ct);

        return new PageDto<AuditLogDto>(page, pageSize, logs);
    }
}
