using LegacyVault.DAL.Context;
using LegacyVault.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.DAL.Repositories;

public sealed record HandoverOverviewRow(int RequestId, int VaultId, string VaultName, int OwnerId, string OwnerName,
    int ExecutorId, string ExecutorName, string RequestType, string Status, DateTime InitiatedAt,
    DateTime? SubmittedAt, DateTime? CompletedAt, bool IsAssignedToMe);

public interface IWorkflowRepository
{
    Task<List<HandoverOverviewRow>> Overview(int executor, int page, int size, string? status, int? ownerId, bool assignedOnly, CancellationToken ct);
    Task<List<HandoverRequest>> ApprovedOwnerRequests(int ownerId, CancellationToken ct);
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
    public Task<List<HandoverOverviewRow>> Overview(int executor, int page, int size, string? status, int? ownerId, bool assignedOnly, CancellationToken ct)
    {
        var query = db.HandoverRequests.AsNoTracking().AsQueryable();
        if (status is not null) query = query.Where(r => r.Status == status);
        if (ownerId is not null) query = query.Where(r => r.Vault.OwnerId == ownerId);
        if (assignedOnly) query = query.Where(r => r.ExecutorId == executor && r.Vault.ExecutorAssignments.Any(a => a.ExecutorId == executor && (a.Status == "Active" || a.Status == "AutoAssigned")));
        return query.OrderByDescending(r => r.RequestId).Skip((page - 1) * size).Take(size)
            .Select(r => new HandoverOverviewRow(r.RequestId, r.VaultId, r.Vault.VaultName, r.Vault.OwnerId, r.Vault.Owner.FullName,
                r.ExecutorId, r.Executor.FullName, r.RequestType, r.Status, r.InitiatedAt, r.SubmittedAt, r.CompletedAt,
                r.ExecutorId == executor && r.Vault.ExecutorAssignments.Any(a => a.ExecutorId == executor && (a.Status == "Active" || a.Status == "AutoAssigned"))))
            .ToListAsync(ct);
    }
    public Task<List<HandoverRequest>> ApprovedOwnerRequests(int ownerId, CancellationToken ct) => db.HandoverRequests.AsNoTracking()
        .Include(r => r.HandoverDocuments).Include(r => r.LegalVerifications).AsSplitQuery()
        .Where(r => r.Vault.OwnerId == ownerId && (r.Status == "Approved" || r.Status == "In_Progress" || r.Status == "Completed") &&
            r.LegalVerifications.Any(v => v.Status == "Approved" && v.ReviewedAt != null))
        .OrderByDescending(r => r.RequestId).ToListAsync(ct);
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
        .Include(r => r.HandoverDocuments).Include(r => r.LegalVerifications).ThenInclude(v => v.Verifier)
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
