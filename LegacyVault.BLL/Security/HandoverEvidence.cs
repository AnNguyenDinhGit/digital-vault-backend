using LegacyVault.BLL.DTOs;
using LegacyVault.DAL.Entities;
using LegacyVault.DAL.Storage;

namespace LegacyVault.BLL.Security;

public static class HandoverEvidence
{
    public static async Task RequireDeathCertificate(HandoverRequest request, IDocumentStore store, DocumentProtection protection, CancellationToken ct)
    {
        foreach (var document in request.HandoverDocuments.Where(d => d.UploadedBy == request.ExecutorId))
        {
            var envelope = protection.Decrypt(await store.Read(document.StoragePath, ct));
            if (envelope.Purpose == "DeathCertificate" && envelope.SignerId == request.ExecutorId) return;
        }
        throw new WorkflowException(409, "The assigned Executor must upload a death certificate before legal submission or approval.");
    }
}
