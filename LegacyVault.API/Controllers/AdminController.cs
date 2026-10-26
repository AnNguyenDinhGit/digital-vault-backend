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

    [HttpGet("roles")]
    public Task<IReadOnlyList<RolePermissionDto>> GetRoles(CancellationToken ct)
        => adminService.GetRolesAsync(UserId, ct);

    [HttpGet("security/alerts")]
    public Task<SecurityDashboardDto> GetSecurityDashboard(CancellationToken ct)
        => adminService.GetSecurityDashboardAsync(UserId, ct);

    [HttpGet("system/config")]
    public Task<SystemConfigDto> GetSystemConfig(CancellationToken ct)
        => adminService.GetSystemConfigAsync(UserId, ct);

    [HttpPut("system/config")]
    public Task<SystemConfigDto> UpdateSystemConfig([FromBody] UpdateSystemConfigInput input, CancellationToken ct)
        => adminService.UpdateSystemConfigAsync(UserId, input, ct);

    [HttpGet("system/key-rotation")]
    public Task<KeyRotationSummaryDto> GetKeyRotationSummary(CancellationToken ct)
        => adminService.GetKeyRotationSummaryAsync(UserId, ct);

    [HttpPost("system/key-rotation/rotate")]
    public Task<KeyRotationSummaryDto> RotateEncryptionKeys(CancellationToken ct)
        => adminService.RotateEncryptionKeysAsync(UserId, ct);

    [HttpGet("integrations")]
    public Task<IReadOnlyList<ThirdPartyIntegrationDto>> GetIntegrations(CancellationToken ct)
        => adminService.GetIntegrationsAsync(UserId, ct);

    [HttpPost("integrations/{serviceId}/ping")]
    public Task<TestIntegrationResultDto> PingIntegration(string serviceId, CancellationToken ct)
        => adminService.PingIntegrationAsync(UserId, serviceId, ct);
}