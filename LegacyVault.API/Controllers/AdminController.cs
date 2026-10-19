using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public sealed class AdminController(AdminService adminService) : VaultController
{
    [HttpGet("users")]
    public Task<PageDto<UserAdminDto>> GetUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? role = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
        => adminService.GetUsersAsync(UserId, page, pageSize, role, status, ct);

    [HttpPost("users")]
    public async Task<ActionResult<UserAdminDto>> CreateUser([FromBody] CreateUserByAdminInput input, CancellationToken ct)
    {
        var result = await adminService.CreateUserAsync(UserId, input, ct);
        return CreatedAtAction(nameof(GetUsers), new { id = result.UserId }, result);
    }

    [HttpPut("users/{userId:int}/roles")]
    public async Task<IActionResult> UpdateRoles(int userId, [FromBody] UpdateUserRolesInput input, CancellationToken ct)
    {
        await adminService.UpdateUserRolesAsync(UserId, userId, input.Roles, ct);
        return NoContent();
    }

    [HttpPut("users/{userId:int}/status")]
    public async Task<IActionResult> UpdateStatus(int userId, [FromBody] UpdateUserStatusInput input, CancellationToken ct)
    {
        await adminService.UpdateUserStatusAsync(UserId, userId, input.Status, ct);
        return NoContent();
    }

    [HttpGet("audit-logs")]
    public Task<PageDto<AuditLogDto>> GetAuditLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? action = null,
        CancellationToken ct = default)
        => adminService.GetAuditLogsAsync(UserId, page, pageSize, action, ct);
}