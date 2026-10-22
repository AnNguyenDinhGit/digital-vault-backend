using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Security;
using LegacyVault.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.API.Controllers;

[ApiController, Authorize, Route("api/owner")]
public sealed class OwnerManagementController(ManagementService service) : VaultController
{
    [HttpGet("executors/{executorId:int}"), Authorize(Roles = "Owner")]
    public Task<ExecutorDetailDto> ExecutorDetail(int executorId, [FromQuery] int? vaultId, CancellationToken ct) =>
        service.OwnerExecutorDetail(UserId, executorId, vaultId, ct);
    [HttpGet("executors"), Authorize(Roles = "Owner")]
    public Task<PageDto<ExecutorOptionDto>> Executors([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null, [FromQuery] int? vaultId = null, CancellationToken ct = default) =>
        service.OwnerExecutorOptions(UserId, page, pageSize, search, vaultId, ct);
    [HttpGet("life-confirmation"), Authorize(Roles = "Owner")]
    public Task<LifeConfirmationDto> LifeConfirmation(CancellationToken ct) => service.LifeConfirmation(UserId, ct);
    [HttpPost("life-confirmation"), Authorize(Roles = "Owner")]
    public Task<LifeConfirmationDto> ConfirmAlive(CancellationToken ct) => service.ConfirmAlive(UserId, ct);
    [HttpPut("vaults/{vaultId:int}")] public Task<VaultDto> UpdateVault(int vaultId, CreateVaultInput input, CancellationToken ct) => service.UpdateVault(UserId, vaultId, input, ct);
    [HttpPost("vaults/{vaultId:int}/close")] public async Task<IActionResult> Close(int vaultId, CancellationToken ct) { await service.CloseVault(UserId, vaultId, ct); return NoContent(); }
    [HttpPut("assets/{assetId:int}")] public Task<AssetDto> UpdateAsset(int assetId, UpdateAssetInput input, CancellationToken ct) => service.UpdateAsset(UserId, assetId, input, ct);
    [HttpPost("assets/{assetId:int}/revoke")] public async Task<IActionResult> RevokeAsset(int assetId, CancellationToken ct) { await service.RevokeAsset(UserId, assetId, ct); return NoContent(); }

    [HttpGet("assets/{assetId:int}/allocations")]
    public Task<PageDto<AssignmentDto>> Allocations(int assetId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        service.Allocations(UserId, assetId, page, pageSize, ct);

    [HttpPut("assets/{assetId:int}/allocations/{assignmentId:int}")] public Task<AssignmentDto> Allocation(int assetId, int assignmentId, AllocationInput input, CancellationToken ct) => service.ChangeAllocation(UserId, assetId, assignmentId, input, ct);
    [HttpPost("assets/{assetId:int}/allocations/{assignmentId:int}/revoke")] public Task<AssignmentDto> RevokeAllocation(int assetId, int assignmentId, CancellationToken ct) => service.ChangeAllocation(UserId, assetId, assignmentId, null, ct);

    [HttpGet("assets/{assetId:int}/documents")]
    public Task<PageDto<ManagedDocumentDto>> Documents(int assetId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        service.Documents(UserId, assetId, page, pageSize, ct);

    [HttpGet("assets/{assetId:int}/documents/{documentId:int}/content")] public async Task<IActionResult> Document(int assetId, int documentId, CancellationToken ct) => ContentFile(await service.OwnerDocument(UserId, assetId, documentId, ct));
    [HttpPost("assets/{assetId:int}/documents/{documentId:int}/revoke")] public async Task<IActionResult> RevokeDocument(int assetId, int documentId, CancellationToken ct) { await service.RevokeDocument(UserId, assetId, documentId, ct); return NoContent(); }
    [HttpPut("vaults/{vaultId:int}/executor")] public async Task<IActionResult> Executor(int vaultId, ExecutorInput input, CancellationToken ct) { await service.AssignExecutor(UserId, vaultId, input, ct); return NoContent(); }
    [HttpPost("vaults/{vaultId:int}/handovers")] public async Task<IActionResult> Create(int vaultId, CancellationToken ct) { var r = await service.CreateRequest(UserId, vaultId, ct); return Created($"/api/owner/handovers/{r.RequestId}", r); }
    [HttpGet("handovers")] public Task<PageDto<HandoverDto>> Requests([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? status = null, CancellationToken ct = default) => service.Requests(UserId, "Owner", page, pageSize, status, ct);
    [HttpGet("handovers/{requestId:int}")] public Task<HandoverDto> Detail(int requestId, CancellationToken ct) => service.OwnerRequest(UserId, requestId, ct);
    [HttpPost("handovers/{requestId:int}/cancel")] public async Task<IActionResult> Cancel(int requestId, CancellationToken ct) { await service.Cancel(UserId, requestId, ct); return NoContent(); }
}

[ApiController, Authorize(Roles = "Executor"), Route("api/executor/assignments")]
public sealed class ExecutorAssignmentsController(ManagementService service) : VaultController
{
    [HttpGet]
    public Task<PageDto<ExecutorAssignmentDto>> Assignments([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        service.ExecutorAssignments(UserId, page, pageSize, ct);

    [HttpPost("{vaultId:int}/handovers")]
    public async Task<IActionResult> Create(int vaultId, CancellationToken ct)
    {
        var request = await service.CreateAssignedRequest(UserId, vaultId, ct);
        return Created($"/api/executor/handovers/{request.RequestId}", request);
    }
}

[ApiController, Authorize(Roles = "Executor"), Route("api/executor/handovers")]
public sealed class ExecutorWorkflowController(ManagementService service) : VaultController
{
    [HttpPost("{requestId:int}/accept")]
    public Task<HandoverDto> Accept(int requestId, CancellationToken ct) => service.AcceptRequest(UserId, requestId, ct);
    [HttpPost("{requestId:int}/decline")]
    public Task<HandoverDto> Decline(int requestId, DeclineHandoverInput input, CancellationToken ct) => service.DeclineRequest(UserId, requestId, input, ct);
    [HttpGet("overview")]
    public Task<PageDto<HandoverOverviewDto>> Overview([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null, [FromQuery] int? ownerId = null, [FromQuery] bool assignedOnly = false, CancellationToken ct = default) =>
        service.ExecutorOverview(UserId, page, pageSize, status, ownerId, assignedOnly, ct);

    [HttpGet("completed")]
    public Task<PageDto<HandoverOverviewDto>> GetCompletedHandovers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default) =>
        service.ExecutorOverview(UserId, page, pageSize, "Completed", null, false, ct);
    [HttpGet("{requestId:int}/owner-status")]
    public Task<OwnerLifeStatusDto> OwnerStatus(int requestId, CancellationToken ct) => service.ExecutorOwnerStatus(UserId, requestId, ct);
    [HttpGet("{requestId:int}/history")]
    public Task<IReadOnlyList<LegalHistoryDto>> History(int requestId, CancellationToken ct) => service.ExecutorHistory(UserId, requestId, ct);
    [HttpGet("{requestId:int}/documents")]
    public Task<IReadOnlyList<DocumentDto>> Documents(int requestId, CancellationToken ct) => service.ExecutorDocuments(UserId, requestId, ct);
    [HttpGet("{requestId:int}/legal-verifiers")]
    public Task<IReadOnlyList<VerifierOptionDto>> Verifiers(int requestId, CancellationToken ct) => service.LegalVerifiers(UserId, requestId, ct);
}

[ApiController, Authorize, Route("api/legal/handovers")]
public sealed class LegalController(ManagementService service) : VaultController
{
    [HttpGet("{requestId:int}/owner-status")]
    public Task<OwnerLifeStatusDto> OwnerStatus(int requestId, CancellationToken ct) => service.LegalOwnerStatus(UserId, requestId, ct);
    [HttpGet("{requestId:int}/history")]
    public Task<IReadOnlyList<LegalHistoryDto>> History(int requestId, CancellationToken ct) => service.LegalHistory(UserId, requestId, ct);
    [HttpGet("{requestId:int}/documents")]
    public Task<IReadOnlyList<DocumentDto>> Documents(int requestId, CancellationToken ct) => service.LegalDocuments(UserId, requestId, ct);
    [HttpGet] public Task<PageDto<HandoverDto>> Requests([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? status = null, CancellationToken ct = default) => service.Requests(UserId, "LegalVerifier", page, pageSize, status, ct);
    [HttpGet("{requestId:int}")] public async Task<IActionResult> Detail(int requestId, CancellationToken ct) { var value = await service.LegalDetail(UserId, requestId, ct); return Ok(new { request = value.Request, verification = value.Verification }); }
    [HttpGet("{requestId:int}/documents/{documentId:int}/content")] public async Task<IActionResult> Document(int requestId, int documentId, CancellationToken ct) => ContentFile(await service.LegalDocument(UserId, requestId, documentId, false, ct));
    [HttpGet("{requestId:int}/asset-documents/{documentId:int}/content")] public async Task<IActionResult> AssetDocument(int requestId, int documentId, CancellationToken ct) => ContentFile(await service.LegalDocument(UserId, requestId, documentId, true, ct));
    [HttpPost("{requestId:int}/approve")] public Task<ReviewDto> Approve(int requestId, ReviewInput input, CancellationToken ct) => service.Review(UserId, requestId, "Approved", input, ct);
    [HttpPost("{requestId:int}/reject")] public Task<ReviewDto> Reject(int requestId, ReviewInput input, CancellationToken ct) => service.Review(UserId, requestId, "Rejected", input, ct);
    [HttpPost("{requestId:int}/request-additional-information")] public Task<ReviewDto> Additional(int requestId, ReviewInput input, CancellationToken ct) => service.Review(UserId, requestId, "Additional_Info", input, ct);
}

[ApiController, Authorize, Route("api/beneficiary/handovers")]
public sealed class BeneficiaryHandoverController(IBeneficiaryService service) : VaultController
{
    [HttpGet] public Task<PageDto<CaseDto>> Cases([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => service.Cases(UserId, SessionId, page, pageSize, ct);
    [HttpGet("{handoverId:int}")] public Task<CaseDto> Case(int handoverId, CancellationToken ct) => service.Detail(UserId, SessionId, handoverId, ct);
    [HttpGet("{handoverId:int}/documents/{documentId:int}/content")] public async Task<IActionResult> Document(int handoverId, int documentId, CancellationToken ct) => ContentFile(await service.Document(UserId, SessionId, handoverId, documentId, ct));
    [HttpPost("{handoverId:int}/receipt")] public Task<ReceiptDto> Receipt(int handoverId, ReceiptInput input, CancellationToken ct) => service.Confirm(UserId, SessionId, handoverId, input, ct);
    [HttpGet("{handoverId:int}/receipt")] public Task<ReceiptDto> ReadReceipt(int handoverId, CancellationToken ct) => service.Receipt(UserId, SessionId, handoverId, ct);
    [HttpGet("{handoverId:int}/documents")] public Task<IReadOnlyList<DocumentDto>> Documents(int handoverId, CancellationToken ct) => service.Documents(UserId, SessionId, handoverId, ct);
}

[ApiController, Authorize, Route("api/notifications")]
public sealed class NotificationsController(ManagementService service) : VaultController
{
    [HttpGet] public Task<PageDto<NotificationDto>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) => service.Notifications(UserId, page, pageSize, ct);
    [HttpPost("{notificationId:int}/read")] public async Task<IActionResult> Read(int notificationId, CancellationToken ct) { await service.ReadNotification(UserId, notificationId, ct); return NoContent(); }
}
