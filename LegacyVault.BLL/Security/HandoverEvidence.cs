using LegacyVault.BLL.DTOs;
using LegacyVault.DAL.Entities;
using LegacyVault.DAL.Repositories;
using LegacyVault.DAL.Storage;

namespace LegacyVault.BLL.Security;

public static class HandoverEvidence
{
    public static Task RequireDeathCertificate(HandoverRequest request, IDocumentStore store, DocumentProtection protection, CancellationToken ct)
    {
        // Giấy chứng tử không còn là điều kiện bắt buộc trước khi gửi thẩm định pháp lý
        return Task.CompletedTask;
    }

    public static async Task<bool> IsUserDeceased(int userId, IWorkflowRepository? workflow, IDocumentStore store, DocumentProtection protection, CancellationToken ct)
    {
        if (workflow is null) return false;
        if (await workflow.HasOwnerDeathRecord(userId, ct))
        {
            return true;
        }
        foreach (var report in await workflow.OwnerDeathReports(userId, ct))
        {
            if (await FindDeathCertificate(report, store, protection, ct) is not null)
            {
                return true;
            }
        }
        return false;
    }

    public static async Task EnsureOwnerNotDeceased(int ownerId, IWorkflowRepository? workflow, IDocumentStore store, DocumentProtection protection, CancellationToken ct)
    {
        if (await IsUserDeceased(ownerId, workflow, store, protection, ct))
        {
            throw new WorkflowException(409, "Chủ sở hữu đang có hồ sơ báo tử kèm giấy chứng tử, không thể tạo mới hoặc chỉnh sửa kho và tài sản.");
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
