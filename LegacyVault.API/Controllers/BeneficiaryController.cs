using LegacyVault.BLL.Services;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BeneficiaryController : ControllerBase
{
    private readonly IBeneficiaryService _beneficiaryService;

    public BeneficiaryController(IBeneficiaryService beneficiaryService)
    {
        _beneficiaryService = beneficiaryService;
    }

    // 1. Nhận thông báo khi nhận bàn giao
    [HttpGet("{beneficiaryId}/notifications")]
    public async Task<IActionResult> GetNotifications(int beneficiaryId)
    {
        var notifications = await _beneficiaryService.GetNotificationsAsync(beneficiaryId);
        return Ok(notifications);
    }

    // 2. Xem chi tiết danh sách tài sản thừa kế
    [HttpGet("{beneficiaryId}/vaults/{vaultId}/assets")]
    public async Task<IActionResult> GetInheritedAssets(int beneficiaryId, int vaultId)
    {
        var assets = await _beneficiaryService.GetInheritedAssetsAsync(beneficiaryId, vaultId);
        return Ok(assets);
    }

    // 3. Xác nhận tài sản được bàn giao & chữ ký số
    [HttpPost("receipts/{receiptId}/confirm")]
    public async Task<IActionResult> ConfirmReceipt(int receiptId, [FromBody] ConfirmReceiptRequest request)
    {
        var success = await _beneficiaryService.ConfirmReceiptAndSignAsync(receiptId, request.SignatureHash, request.Note);
        if (!success) return NotFound("Receipt not found.");
        return Ok(new { message = "Xác nhận nhận tài sản và ký số thành công." });
    }

    // 4. Thông báo lại cho người thi hành
    [HttpPost("receipts/{receiptId}/notify-executor")]
    public async Task<IActionResult> NotifyExecutor(int receiptId, [FromBody] string message)
    {
        var success = await _beneficiaryService.NotifyExecutorAsync(receiptId, message);
        if (!success) return NotFound("Receipt not found.");
        return Ok(new { message = "Đã gửi thông báo tới Người thi hành." });
    }

    // 5. Tải xuống tài liệu bàn giao
    [HttpGet("documents/{documentId}/download")]
    public async Task<IActionResult> DownloadDocument(int documentId, [FromQuery] int beneficiaryId)
    {
        var doc = await _beneficiaryService.GetDocumentForDownloadAsync(documentId, beneficiaryId);
        if (doc == null) return NotFound("Document not found.");
        return Ok(new { doc.FileName, doc.StoragePath, doc.FileSizeByte });
    }

    // 6. Nhập mật khẩu mã hóa để xem/giải mã tài liệu
    [HttpPost("assets/{assetId}/decrypt")]
    public async Task<IActionResult> DecryptAsset(int assetId, [FromBody] DecryptRequest request)
    {
        var decryptedData = await _beneficiaryService.DecryptAssetDataAsync(assetId, request.DecryptKey);
        if (decryptedData == null) return BadRequest("Khóa giải mã không đúng hoặc tài sản không tồn tại.");
        return Ok(new { data = decryptedData });
    }
}

public record ConfirmReceiptRequest(string SignatureHash, string? Note);
public record DecryptRequest(string DecryptKey);