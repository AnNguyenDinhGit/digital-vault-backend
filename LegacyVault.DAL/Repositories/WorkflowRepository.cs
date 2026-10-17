using LegacyVault.DAL.Context;
using LegacyVault.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.DAL.Repositories;

public interface IWorkflowRepository
{
    Task<DigitalVault?> VaultGraph(int id, CancellationToken ct);
    Task<DigitalAsset?> AssetGraph(int id, CancellationToken ct);
    Task<HandoverRequest?> RequestGraph(int id, CancellationToken ct);
    Task<List<HandoverRequest>> UserRequests(int user, string kind, int page, int size, string? status, CancellationToken ct);
    Task<List<HandoverCase>> BeneficiaryCases(int user, int page, int size, CancellationToken ct);
    Task<HandoverCase?> Case(int id, CancellationToken ct);
    Task<List<Notification>> Notifications(int user, int page, int size, CancellationToken ct);
    Task<Notification?> Notification(int user, int id, CancellationToken ct);
    Task<List<ExecutorAssignment>> ExecutorAssignments(int user, CancellationToken ct);
}
public sealed class WorkflowRepository(LegacyVaultDbContext db) : IWorkflowRepository
{
    public Task<List<ExecutorAssignment>> ExecutorAssignments(int user, CancellationToken ct) => db.ExecutorAssignments.AsNoTracking()
        .Where(a => a.ExecutorId == user && (a.Status == "Active" || a.Status == "AutoAssigned"))
        .OrderByDescending(a => a.AssignedAt).ToListAsync(ct);
    private IQueryable<DigitalVault> Vaults => db.DigitalVaults.Include(v => v.Owner).Include(v => v.ExecutorAssignments).ThenInclude(a => a.Executor).ThenInclude(u => u.Roles)
        .Include(v => v.HandoverRequests).Include(v => v.DigitalAssets).ThenInclude(a => a.AssetDocuments)
        .Include(v => v.DigitalAssets).ThenInclude(a => a.BeneficiaryAssignments).ThenInclude(b => b.Beneficiary).AsSplitQuery();
    public Task<DigitalVault?> VaultGraph(int id, CancellationToken ct) => Vaults.SingleOrDefaultAsync(v => v.VaultId == id, ct);
    public async Task<DigitalAsset?> AssetGraph(int id, CancellationToken ct)
    {
        var vaultId = await db.DigitalAssets.Where(a => a.AssetId == id).Select(a => (int?)a.VaultId).SingleOrDefaultAsync(ct);
        return vaultId is null ? null : (await VaultGraph(vaultId.Value, ct))?.DigitalAssets.SingleOrDefault(a => a.AssetId == id);
    }
    private IQueryable<HandoverRequest> Requests => db.HandoverRequests.Include(r => r.Vault).ThenInclude(v => v.Owner)
        .Include(r => r.Vault).ThenInclude(v => v.ExecutorAssignments).Include(r => r.Vault).ThenInclude(v => v.DigitalAssets).ThenInclude(a => a.AssetDocuments)
        .Include(r => r.HandoverDocuments).Include(r => r.LegalVerifications)
        .Include(r => r.HandoverCases).ThenInclude(c => c.BeneficiaryReceipts)
        .Include(r => r.HandoverCases).ThenInclude(c => c.Assignment).ThenInclude(a => a.Beneficiary).AsSplitQuery();
    public Task<HandoverRequest?> RequestGraph(int id, CancellationToken ct) => Requests.SingleOrDefaultAsync(r => r.RequestId == id, ct);
    public Task<List<HandoverRequest>> UserRequests(int user, string kind, int page, int size, string? status, CancellationToken ct)
    {
        var query = kind switch { "Owner" => Requests.Where(r => r.Vault.OwnerId == user), "LegalVerifier" => Requests.Where(r => r.LegalVerifications.Any(v => v.VerifierId == user) && r.Vault.OwnerId != user && r.ExecutorId != user && !r.HandoverCases.Any(c => c.Assignment.BeneficiaryId == user)), _ => Requests.Where(r => r.ExecutorId == user && r.Vault.ExecutorAssignments.Any(a => a.ExecutorId == user && (a.Status == "Active" || a.Status == "AutoAssigned"))) };
        if (status is not null) query = query.Where(r => r.Status == status);
        return query.OrderByDescending(r => r.RequestId).Skip((page - 1) * size).Take(size).ToListAsync(ct);
    }
    private IQueryable<HandoverCase> Cases => db.HandoverCases.Include(c => c.Request).ThenInclude(r => r.Vault)
        .Include(c => c.Request).ThenInclude(r => r.LegalVerifications).Include(c => c.Request).ThenInclude(r => r.HandoverDocuments)
        .Include(c => c.Assignment).ThenInclude(a => a.Asset).ThenInclude(a => a.AssetDocuments).Include(c => c.BeneficiaryReceipts).AsSplitQuery();
    public Task<List<HandoverCase>> BeneficiaryCases(int user, int page, int size, CancellationToken ct) => Cases.Where(c => c.Assignment.BeneficiaryId == user && c.Assignment.Status == "Active" && c.Request.LegalVerifications.Any(v => v.Status == "Approved") && (c.Status == "In_Progress" || c.Status == "Completed" || c.Status == "Received")).OrderBy(c => c.HandoverId).Skip((page - 1) * size).Take(size).ToListAsync(ct);
    public Task<HandoverCase?> Case(int id, CancellationToken ct) => Cases.SingleOrDefaultAsync(c => c.HandoverId == id, ct);
    public Task<List<Notification>> Notifications(int user, int page, int size, CancellationToken ct) => db.Notifications.Where(n => n.UserId == user).OrderByDescending(n => n.NotificationId).Skip((page - 1) * size).Take(size).ToListAsync(ct);
    public Task<Notification?> Notification(int user, int id, CancellationToken ct) => db.Notifications.SingleOrDefaultAsync(n => n.UserId == user && n.NotificationId == id, ct);
}
