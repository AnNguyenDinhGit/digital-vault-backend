using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.API.Controllers;

// Trang "Nhật ký hoạt động" và "Cài đặt bảo mật" của Chủ sở hữu. Không gắn bộ lọc gói dịch vụ
// để người dùng hết hạn gói vẫn xem nhật ký và chỉnh thông tin tài khoản của chính mình.
[ApiController, Authorize, Route("api/owner")]
public sealed class OwnerAccountController(OwnerAccountService service) : VaultController
{
    [HttpGet("activity-logs")]
    public Task<List<OwnerActivityLogDto>> ActivityLogs(CancellationToken ct) =>
        service.ActivityLogs(UserId, ct);

    [HttpGet("profile")]
    public Task<OwnerProfileDto> Profile(CancellationToken ct) =>
        service.Profile(UserId, ct);

    [HttpPut("profile")]
    public Task<OwnerProfileDto> UpdateProfile([FromBody] OwnerProfileInput input, CancellationToken ct) =>
        service.UpdateProfile(UserId, input, ct);

    [HttpPut("security/two-factor")]
    public Task<TwoFactorStatusDto> SetTwoFactor([FromBody] TwoFactorToggleInput input, CancellationToken ct) =>
        service.SetTwoFactor(UserId, input, ct);
}

