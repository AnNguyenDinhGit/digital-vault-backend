using LegacyVault.DAL.Context;
using LegacyVault.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace LegacyVault.DAL.Repositories;

public sealed record HandoverOverviewRow(int RequestId, int VaultId, string VaultName, int OwnerId, string OwnerName,
    int ExecutorId, string ExecutorName, string RequestType, string Status, DateTime InitiatedAt,
    DateTime? SubmittedAt, DateTime? CompletedAt, bool IsAssignedToMe);
public sealed record AdminHandoverRow(int RequestId, int VaultId, string VaultName, int OwnerId, string OwnerName,
    int ExecutorId, string ExecutorName, string RequestType, string Status, DateTime InitiatedAt,
    DateTime? SubmittedAt, DateTime? CompletedAt);
public sealed record ExecutorOptionRow(int UserId, string FullName);
public sealed record OwnerDeclineRow(int RequestId, string? Message, DateTime? DeclinedAt);

public interface IWorkflowRepository
{
    Task<List<OwnerDeclineRow>> OwnerDeclines(int ownerId, int[] requestIds, CancellationToken ct);
    Task<List<ExecutorOptionRow>> ExecutorOptions(int ownerId, int? vaultId, string? search, int page, int size, DateTime now, CancellationToken ct);
    Task<DateTime?> LastLifeConfirmation(int ownerId, CancellationToken ct);
    Task<DateTime> ConfirmLife(int ownerId, DateTime now, CancellationToken ct);
    Task<List<HandoverRequest>> OwnerDeathReports(int ownerId, CancellationToken ct);
    Task<List<HandoverOverviewRow>> Overview(int executor, int page, int size, string? status, int? ownerId, bool assignedOnly, CancellationToken ct);
    Task<(int TotalCount, List<AdminHandoverRow> Items)> AdminHandovers(int page, int size, string? status, int? ownerId, CancellationToken ct);
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
    public Task<List<OwnerDeclineRow>> OwnerDeclines(int ownerId, int[] requestIds, CancellationToken ct) =>
        db.HandoverRequests.AsNoTracking().Where(r => requestIds.Contains(r.RequestId) && r.Vault.OwnerId == ownerId && r.Status == "Declined")
            .Select(r => new OwnerDeclineRow(r.RequestId,
                db.Notifications.Where(n => n.UserId == ownerId && n.Title == "Executor declined handover" && n.Message.StartsWith("Handover #" + r.RequestId + ": "))
                    .OrderByDescending(n => n.NotificationId).Select(n => n.Message).FirstOrDefault(),
                db.AuditLogs.Where(a => a.Action == "DeclineHandover" && a.EntityType == "HandoverRequest" && a.EntityId == r.RequestId && a.UserId == r.ExecutorId)
                    .OrderByDescending(a => a.Timestamp).Select(a => (DateTime?)a.Timestamp).FirstOrDefault()))
            .ToListAsync(ct);

    public Task<List<ExecutorOptionRow>> ExecutorOptions(int ownerId, int? vaultId, string? search, int page, int size, DateTime now, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking().Where(u => u.UserId != ownerId && u.Status == "Active" &&
            u.Roles.Any(r => r.RoleName == "Executor") && !u.Authentications.Any(a => a.LockedUntil > now));
        if (vaultId is not null)
            query = query.Where(u => !db.BeneficiaryAssignments.Any(b => b.Asset.VaultId == vaultId && b.BeneficiaryId == u.UserId && b.Status == "Active"));
        if (search is not null)
        {
            var term = search.ToUpperInvariant();
            query = query.Where(u => u.FullName.ToUpper().Contains(term));
        }
        return query.OrderBy(u => u.FullName).ThenBy(u => u.UserId).Skip((page - 1) * size).Take(size)
            .Select(u => new ExecutorOptionRow(u.UserId, u.FullName)).ToListAsync(ct);
    }

    public Task<DateTime?> LastLifeConfirmation(int ownerId, CancellationToken ct) => db.ProofOfLives.AsNoTracking()
        .Where(p => p.UserId == ownerId).OrderByDescending(p => p.LastPingAt)
        .Select(p => (DateTime?)p.LastPingAt).FirstOrDefaultAsync(ct);

    public async Task<DateTime> ConfirmLife(int ownerId, DateTime now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        // Serialize clicks for the same Owner, including first-row creation on SQL Server.
        if (db.Database.IsSqlServer())
            _ = await db.Users.FromSqlInterpolated($"SELECT * FROM [USERS] WITH (UPDLOCK, HOLDLOCK) WHERE [UserId] = {ownerId}").SingleAsync(ct);
        var proof = await db.ProofOfLives.Where(p => p.UserId == ownerId)
            .OrderByDescending(p => p.LastPingAt).ThenByDescending(p => p.CheckId).FirstOrDefaultAsync(ct);
        if (proof is null)
        {
            proof = new ProofOfLife { UserId = ownerId, FrequencyDays = 30, GracePeriodDays = 14, CreatedAt = now };
            db.ProofOfLives.Add(proof);
        }
        if (proof.FrequencyDays is < 1 or > 3650 || proof.GracePeriodDays is < 0 or > 3650)
            throw new InvalidOperationException("Proof-of-life schedule is invalid.");
        // A request that waited for the lock must not move the clock backwards.
        if (proof.LastPingAt > now) now = proof.LastPingAt;
        proof.LastPingAt = now; proof.NextPingDue = now.AddDays(proof.FrequencyDays);
        proof.Status = "Active"; proof.MissedCount = 0; proof.UpdatedAt = now;
        db.AuditLogs.Add(new AuditLog { UserId = ownerId, Action = "ConfirmAlive", EntityType = "User", EntityId = ownerId, Result = "Success", Timestamp = now });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return now;
    }

    public Task<List<HandoverRequest>> OwnerDeathReports(int ownerId, CancellationToken ct) => db.HandoverRequests.AsNoTracking()
        .Include(r => r.HandoverDocuments).AsSplitQuery()
        .Where(r => r.Vault.OwnerId == ownerId && r.Status != "Cancelled" && r.Status != "Rejected" && r.HandoverDocuments.Any())
        .ToListAsync(ct);

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

    public async Task<(int TotalCount, List<AdminHandoverRow> Items)> AdminHandovers(int page, int size, string? status, int? ownerId, CancellationToken ct)
    {
        var query = db.HandoverRequests.AsNoTracking().AsQueryable();
        if (status is not null) query = query.Where(r => r.Status == status);
        if (ownerId is not null) query = query.Where(r => r.Vault.OwnerId == ownerId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(r => r.InitiatedAt).ThenByDescending(r => r.RequestId)
            .Skip((page - 1) * size).Take(size)
            .Select(r => new AdminHandoverRow(r.RequestId, r.VaultId, r.Vault.VaultName,
                r.Vault.OwnerId, r.Vault.Owner.FullName, r.ExecutorId, r.Executor.FullName,
                r.RequestType, r.Status, r.InitiatedAt, r.SubmittedAt, r.CompletedAt))
            .ToListAsync(ct);
        return (total, items);
    }
    public Task<List<HandoverRequest>> ApprovedOwnerRequests(int ownerId, CancellationToken ct) => db.HandoverRequests.AsNoTracking()
        .Include(r => r.HandoverDocuments).Include(r => r.LegalVerifications).AsSplitQuery()
        .Where(r => r.Vault.OwnerId == ownerId && (r.Status == "Approved" || r.Status == "In_Progress" || r.Status == "Completed") &&
            r.LegalVerifications.Any(v => v.Status == "Approved" && v.ReviewedAt != null))
        .OrderByDescending(r => r.RequestId).ToListAsync(ct);
    public Task<List<ExecutorAssignment>> ExecutorAssignments(int user, CancellationToken ct) => db.ExecutorAssignments.AsNoTracking()
        .Include(a => a.Vault).ThenInclude(v => v.Owner)
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
    private IQueryable<HandoverRequest> Requests => db.HandoverRequests.Include(r => r.Executor).Include(r => r.Vault).ThenInclude(v => v.Owner)
        .Include(r => r.Vault).ThenInclude(v => v.ExecutorAssignments).Include(r => r.Vault).ThenInclude(v => v.DigitalAssets).ThenInclude(a => a.AssetDocuments)
        .Include(r => r.HandoverDocuments).Include(r => r.LegalVerifications).ThenInclude(v => v.Verifier)
        .Include(r => r.HandoverCases).ThenInclude(c => c.BeneficiaryReceipts)
        .Include(r => r.HandoverCases).ThenInclude(c => c.Assignment).ThenInclude(a => a.Asset)
        .Include(r => r.HandoverCases).ThenInclude(c => c.Assignment).ThenInclude(a => a.Beneficiary).AsSplitQuery();
    public Task<HandoverRequest?> RequestGraph(int id, CancellationToken ct) => Requests.SingleOrDefaultAsync(r => r.RequestId == id, ct);
    public Task<List<HandoverRequest>> UserRequests(int user, string kind, int page, int size, string? status, CancellationToken ct)
    {
        var query = kind switch { "Owner" => Requests.Where(r => r.Vault.OwnerId == user), "LegalVerifier" => Requests.Where(r => r.LegalVerifications.Any(v => v.VerifierId == user) && r.Vault.OwnerId != user && r.ExecutorId != user && !r.HandoverCases.Any(c => c.Assignment.BeneficiaryId == user)), _ => Requests.Where(r => r.ExecutorId == user && r.Vault.ExecutorAssignments.Any(a => a.ExecutorId == user && (a.Status == "Active" || a.Status == "AutoAssigned" || r.Status == "Declined"))) };
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
