using LegacyVault.BLL.DTOs;
using LegacyVault.DAL.Entities;
using LegacyVault.DAL.Repositories;
using LegacyVault.DAL.Storage;

namespace LegacyVault.BLL.Security;

public static class HandoverEvidence
{
    public static async Task RequireDeathCertificate(HandoverRequest request, IDocumentStore store, DocumentProtection protection, CancellationToken ct)
    {
        if (!string.Equals(request.RequestType, "DeadManSwitch", StringComparison.OrdinalIgnoreCase))
            return;

        if (await FindDeathCertificate(request, store, protection, ct) is null)
            throw new WorkflowException(409, "The assigned Executor must upload a death certificate before legal submission or approval.");
    }

    public static async Task EnsureOwnerNotDeceased(int ownerId, IWorkflowRepository? workflow, IDocumentStore store, DocumentProtection protection, CancellationToken ct)
    {
        if (workflow is null) return;
        foreach (var verified in await workflow.ApprovedOwnerRequests(ownerId, ct))
        {
            if (string.Equals(verified.RequestType, "DeadManSwitch", StringComparison.OrdinalIgnoreCase) ||
                await FindDeathCertificate(verified, store, protection, ct) is not null)
            {
                throw new WorkflowException(409, "Chủ sở hữu đã được xác nhận qua đời theo hồ sơ bàn giao di sản, không thể tạo mới hoặc chỉnh sửa kho và tài sản.");
            }
        }
        foreach (var report in await workflow.OwnerDeathReports(ownerId, ct))
        {
            if (await FindDeathCertificate(report, store, protection, ct) is not null)
            {
                throw new WorkflowException(409, "Chủ sở hữu đang có hồ sơ báo tử kèm giấy chứng tử, không thể tạo mới hoặc chỉnh sửa kho và tài sản.");
            }
        }
    }
    public static async Task<HandoverDocument?> FindDeathCertificate(HandoverRequest request, IDocumentStore store, DocumentProtection protection, CancellationToken ct)
    {
        foreach (var document in request.HandoverDocuments.Where(d => d.UploadedBy == request.ExecutorId))
        {
            try
            {
                var envelope = await protection.Read(store, document.StoragePath, ct);
                if (envelope.Purpose == "DeathCertificate" && envelope.SignerId == request.ExecutorId) return document;
            }
            catch (WorkflowException ex) when (ex.StatusCode == 503)
            {
            }
        }
        return null;
    }
}
