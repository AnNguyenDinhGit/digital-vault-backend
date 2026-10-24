using System.ComponentModel.DataAnnotations;
using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Extensions;
using LegacyVault.BLL.Security;
using LegacyVault.DAL.Entities;
using LegacyVault.DAL.Repositories;
using LegacyVault.DAL.Storage;

namespace LegacyVault.BLL.Services;

public sealed class ManagementService(IVaultRepository repository, IWorkflowRepository workflow, IDocumentStore store, DocumentProtection protection)
{
    private static bool Assigned(ExecutorAssignment a) => a.Status is "Active" or "AutoAssigned";
    public async Task<VaultExecutorDto> OwnerVaultExecutor(int user, int vaultId, CancellationToken ct)
    {
        var vault = await Owned(user, vaultId, ct);
        var active = vault.ExecutorAssignments.Where(Assigned).ToArray();
        if (active.Length > 1) throw new WorkflowException(409, "Multiple active Executor assignments require correction.");
        var assignment = active.SingleOrDefault() ?? vault.ExecutorAssignments.OrderByDescending(a => a.AssignedAt).ThenByDescending(a => a.ExecutorAssignmentId).FirstOrDefault()
            ?? throw new WorkflowException(404, "Executor assignment not found.");
        return new(vaultId, assignment.ExecutorId, assignment.Executor.FullName, assignment.Status, DateTime.SpecifyKind(assignment.AssignedAt, DateTimeKind.Utc));
    }

    public async Task RevokeExecutor(int user, int vaultId, CancellationToken ct)
    {
        var vault = await Owned(user, vaultId, ct);
        if (vault.Status != "Active" || vault.HandoverRequests.Any(r => r.Status is not ("Pending_Executor" or "Cancelled" or "Declined")))
            throw new WorkflowException(409, "Executor cannot be revoked after handover acceptance or processing.");
        var active = vault.ExecutorAssignments.Where(Assigned).ToArray();
        if (active.Length == 0) throw new WorkflowException(404, "Active Executor assignment not found.");
        if (active.Length > 1) throw new WorkflowException(409, "Multiple active Executor assignments require correction.");
        foreach (var request in vault.HandoverRequests.Where(r => r.Status == "Pending_Executor"))
        {
            var graph = await workflow.RequestGraph(request.RequestId, ct) ?? throw new WorkflowException(404, "Request not found.");
            graph.Status = "Cancelled";
            foreach (var item in graph.HandoverCases) item.Status = "Cancelled";
            Audit(user, "CancelHandover", "HandoverRequest", graph.RequestId);
        }
        active[0].Status = "Revoked"; Touch(vault);
        Audit(user, "RevokeExecutor", "DigitalVault", vaultId);
        Notify(active[0].ExecutorId, "Assignment", "Vault assignment revoked", "The Owner revoked your vault assignment.");
        await repository.Save(ct);
    }

    private async Task<HandoverDto[]> OwnerHandoverDetails(int owner, HandoverRequest[] requests, CancellationToken ct)
    {
        var ids = requests.Where(r => r.Status == "Declined").Select(r => r.RequestId).ToArray();
        var declines = ids.Length == 0 ? new Dictionary<int, OwnerDeclineRow>() : (await workflow.OwnerDeclines(owner, ids, ct)).ToDictionary(r => r.RequestId);
        return requests.Select(r =>
        {
            var dto = Map(r);
            if (!declines.TryGetValue(r.RequestId, out var decline)) return dto;
            var prefix = $"Handover #{r.RequestId}: ";
            return dto with { DeclineReason = decline.Message?.StartsWith(prefix, StringComparison.Ordinal) == true ? decline.Message[prefix.Length..] : null,
                DeclinedAt = decline.DeclinedAt is { } date ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : null };
        }).ToArray();
    }

    public async Task<ExecutorDetailDto> OwnerExecutorDetail(int user, int executorId, int? vaultId, CancellationToken ct)
    {
        await Require(user, ct, VaultService.OwnerRole);
        if (executorId <= 0 || vaultId is <= 0) throw new WorkflowException(400, "Executor and vault IDs must be positive.");
        var vault = vaultId is null ? null : await Owned(user, vaultId.Value, ct);
        var executor = await repository.User(executorId, ct);
        if (executor is null || executor.UserId == user || executor.Status != "Active" ||
            !executor.Roles.Any(r => r.RoleName == VaultService.ExecutorRole) || executor.Authentications.Any(a => a.LockedUntil > DateTime.UtcNow) ||
            (vault is not null && vault.DigitalAssets.Any(a => a.BeneficiaryAssignments.Any(b => b.Status == "Active" && b.BeneficiaryId == executorId))))
            throw new WorkflowException(404, "Eligible Executor not found.");
        return new(executor.UserId, executor.FullName, VaultService.ExecutorRole, executor.Status, DateTime.SpecifyKind(executor.CreatedAt, DateTimeKind.Utc));
    }

    public async Task<HandoverDto> AcceptRequest(int user, int id, CancellationToken ct)
    {
        var request = await OperatorRequest(user, id, ct);
        if (request.Status != "Pending_Executor") throw new WorkflowException(409, "Only an awaiting Executor request can be accepted.");
        if (request.Vault.Status != "Active") throw new WorkflowException(409, "Vault is not active.");
        Ready(request.Vault);
        request.Status = "Draft"; Touch(request.Vault);
        Audit(user, "AcceptHandover", "HandoverRequest", id);
        Notify(request.Vault.OwnerId, "Handover", "Executor accepted handover", "The assigned Executor accepted your handover request.");
        await repository.Save(ct); return Map(request);
    }

    public async Task<HandoverDto> DeclineRequest(int user, int id, DeclineHandoverInput input, CancellationToken ct)
    {
        Validate(input);
        if (string.IsNullOrWhiteSpace(input.Reason)) throw new WorkflowException(400, "Decline reason is required.");
        var request = await OperatorRequest(user, id, ct);
        if (request.Status != "Pending_Executor") throw new WorkflowException(409, "Only an awaiting Executor request can be declined.");
        request.Status = "Declined";
        foreach (var item in request.HandoverCases) item.Status = "Cancelled";
        foreach (var assignment in request.Vault.ExecutorAssignments.Where(a => a.ExecutorId == user && Assigned(a))) assignment.Status = "Revoked";
        Touch(request.Vault);
        Audit(user, "DeclineHandover", "HandoverRequest", id);
        Notify(request.Vault.OwnerId, "Handover", "Executor declined handover", $"Handover #{id}: {input.Reason.Trim()}");
        await repository.Save(ct); return Map(request);
    }

    public async Task<PageDto<ExecutorOptionDto>> OwnerExecutorOptions(int user, int page, int size, string? search, int? vaultId, CancellationToken ct)
    {
        await Require(user, ct, VaultService.OwnerRole);
        Paging(page, size);
        if (search?.Length > 100 || vaultId is <= 0) throw new WorkflowException(400, "search must be at most 100 characters; vaultId must be positive.");
        if (vaultId is not null) _ = await Owned(user, vaultId.Value, ct);
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var rows = await workflow.ExecutorOptions(user, vaultId, search, page, size, DateTime.UtcNow, ct);
        return new(page, size, rows.Select(r => new ExecutorOptionDto(r.UserId, r.FullName)).ToArray());
    }

    public async Task<LifeConfirmationDto> LifeConfirmation(int user, CancellationToken ct)
    {
        await Require(user, ct, VaultService.OwnerRole);
        return new(user, await LastConfirmedAlive(user, ct));
    }

    public async Task<LifeConfirmationDto> ConfirmAlive(int user, CancellationToken ct)
    {
        await Require(user, ct, VaultService.OwnerRole);
        if ((await workflow.ApprovedOwnerRequests(user, ct)).Count != 0)
            throw new WorkflowException(409, "A legally confirmed handover exists. Resolve the conflicting legal record before confirming alive.");
        foreach (var request in await workflow.OwnerDeathReports(user, ct))
            if (await HandoverEvidence.FindDeathCertificate(request, store, protection, ct) is not null)
                throw new WorkflowException(409, "A death report or legally confirmed handover exists. Resolve the conflicting report before confirming alive.");

        var now = await workflow.ConfirmLife(user, DateTime.UtcNow, ct);
        return new(user, DateTime.SpecifyKind(now, DateTimeKind.Utc));
    }

    private async Task<DateTime?> LastConfirmedAlive(int ownerId, CancellationToken ct)
    {
        var timestamp = await workflow.LastLifeConfirmation(ownerId, ct);
        return timestamp is null ? null : DateTime.SpecifyKind(timestamp.Value, DateTimeKind.Utc);
    }

    public async Task<PageDto<HandoverOverviewDto>> ExecutorOverview(int user, int page, int size, string? status, int? ownerId, bool assignedOnly, CancellationToken ct)
    {
        await Require(user, ct, VaultService.ExecutorRole); Paging(page, size); ValidateStatus(status);
        if (ownerId is <= 0) throw new WorkflowException(400, "ownerId must be positive.");
        var rows = await workflow.Overview(user, page, size, status, ownerId, assignedOnly, ct);
        return new(page, size, rows.Select(r => new HandoverOverviewDto(r.RequestId, r.VaultId, r.VaultName,
            new(r.OwnerId, r.OwnerName), r.ExecutorId, r.ExecutorName, r.RequestType, r.Status, r.InitiatedAt,
            r.SubmittedAt, r.CompletedAt, r.IsAssignedToMe)).ToArray());
    }
    private static void ValidateStatus(string? status)
    {
        if (status is not null && status is not ("Pending_Executor" or "Declined" or "Draft" or "Cancelled" or "Pending_Legal" or "Additional_Info" or "Rejected" or "Approved" or "In_Progress" or "Completed"))
            throw new WorkflowException(400, "Unknown handover status filter.");
    }
    public async Task<PageDto<ExecutorAssignmentDto>> ExecutorAssignments(int user, int page, int size, CancellationToken ct)
    {
        await Require(user, ct, VaultService.ExecutorRole);
        Paging(page, size);
        var all = (await workflow.ExecutorAssignments(user, ct)).Select(a => new ExecutorAssignmentDto(a.VaultId, user, a.Status, a.AssignedAt)
        { VaultName = a.Vault.VaultName, Owner = new(a.Vault.OwnerId, a.Vault.Owner.FullName) }).ToArray();
        var items = all.Skip((page - 1) * size).Take(size).ToArray();
        return new PageDto<ExecutorAssignmentDto>(page, size, items);
    }
    private static void Validate(object input)
    {
        if (!Validator.TryValidateObject(input, new ValidationContext(input), new List<ValidationResult>(), true)) throw new WorkflowException(400, "Invalid input.");
    }
    private async Task<User> Require(int userId, CancellationToken ct, params string[] roles)
    {
        var user = await repository.User(userId, ct);
        if (user is null || user.Status != "Active" || user.Authentications.Any(a => a.LockedUntil > DateTime.UtcNow) || !user.Roles.Any(r => roles.Contains(r.RoleName)))
            throw new WorkflowException(403, "Required active role is missing.");
        return user;
    }
    private async Task<DigitalVault> Owned(int user, int id, CancellationToken ct)
    {
        await Require(user, ct, VaultService.OwnerRole);
        var v = await workflow.VaultGraph(id, ct);
        return v is not null && v.OwnerId == user ? v : throw new WorkflowException(404, "Vault not found.");
    }
    private async Task<DigitalAsset> Asset(int user, int id, CancellationToken ct)
    {
        await Require(user, ct, VaultService.OwnerRole);
        var a = await workflow.AssetGraph(id, ct);
        return a is not null && a.Vault.OwnerId == user ? a : throw new WorkflowException(404, "Asset not found.");
    }
    private static void Mutable(DigitalVault v)
    {
        if (v.Status != "Active" || v.HandoverRequests.Any(r => r.Status != "Cancelled" && r.Status != "Declined"))
            throw new WorkflowException(409, "Vault is inactive or has a protected handover record.");
    }
    private static void Active(DigitalAsset a)
    {
        Mutable(a.Vault);
        if (a.Status != "Active") throw new WorkflowException(409, "Asset is inactive.");
    }
    private static void Touch(DigitalVault v) => v.UpdatedAt = RecordVersion.Next(v.UpdatedAt);
    private void Audit(int user, string action, string type, int id) => repository.Add(new AuditLog { UserId = user, Action = action, EntityType = type, EntityId = id, Result = "Success", Timestamp = DateTime.UtcNow });
    private void Notify(int user, string type, string title, string message) => repository.Add(new Notification { UserId = user, Type = type, Title = title, Message = message, CreatedAt = DateTime.UtcNow });
    private static HandoverDto Map(HandoverRequest r) => r.ToDto();
    private static AssignmentDto Map(BeneficiaryAssignment a) => new(a.AssignmentId, a.AssetId, a.BeneficiaryId, a.Beneficiary.FullName, a.Allocation, a.Status);
    private static ReceiptDto Map(BeneficiaryReceipt r) => new(r.ReceiptId, r.HandoverId, r.BeneficiaryId, r.Status, r.ConfirmationNote, r.ConfirmedAt);
    public async Task<VaultDto> UpdateVault(int user, int id, CreateVaultInput input, CancellationToken ct)
    {
        Validate(input); var v = await Owned(user, id, ct); Mutable(v);
        v.VaultName = input.Name.Trim(); v.Description = input.Description?.Trim(); Touch(v);
        Audit(user, "UpdateVault", "DigitalVault", id); await repository.Save(ct); return new(id, v.VaultName, v.Description, v.Status);
    }
    public async Task CloseVault(int user, int id, CancellationToken ct)
    {
        var v = await Owned(user, id, ct); Mutable(v); v.Status = "Closed"; Touch(v);
        Audit(user, "CloseVault", "DigitalVault", id); await repository.Save(ct);
    }
    public async Task<AssetDto> UpdateAsset(int user, int id, UpdateAssetInput input, CancellationToken ct)
    {
        Validate(input); var a = await Asset(user, id, ct); Active(a);
        a.AssetName = input.Name.Trim(); a.AssetType = input.Type.Trim(); a.Description = input.Description?.Trim(); a.UpdatedAt = RecordVersion.Next(a.UpdatedAt); Touch(a.Vault);
        Audit(user, "UpdateAsset", "DigitalAsset", id); await repository.Save(ct);
        return new(id, a.AssetName, a.AssetType, a.Description, a.Status, a.AssetDocuments.Where(d => d.Status != "Revoked").Select(d => new DocumentDto(d.DocumentId, d.FileName, d.FileType, d.IsEncrypted)).ToArray());
    }
    public async Task RevokeAsset(int user, int id, CancellationToken ct)
    {
        var a = await Asset(user, id, ct); Active(a); a.Status = "Revoked"; a.UpdatedAt = RecordVersion.Next(a.UpdatedAt); Touch(a.Vault);
        Audit(user, "RevokeAsset", "DigitalAsset", id); await repository.Save(ct);
    }
    public async Task<PageDto<AssignmentDto>> Allocations(int user, int id, int page, int size, CancellationToken ct)
    {
        Paging(page, size);
        var all = (await Asset(user, id, ct)).BeneficiaryAssignments.Select(Map).ToArray();
        var items = all.Skip((page - 1) * size).Take(size).ToArray();
        return new PageDto<AssignmentDto>(page, size, items);
    }
    public async Task<AssignmentDto> ChangeAllocation(int user, int id, int assignmentId, AllocationInput? input, CancellationToken ct)
    {
        var a = await Asset(user, id, ct); Active(a);
        var b = a.BeneficiaryAssignments.SingleOrDefault(x => x.AssignmentId == assignmentId) ?? throw new WorkflowException(404, "Assignment not found.");
        if (b.Status != "Active") throw new WorkflowException(409, "Assignment is not active.");
        if (input is not null)
        {
            Validate(input);
            var recipient = await repository.User(b.BeneficiaryId, ct);
            if (recipient is null || recipient.Status != "Active" || recipient.UserId == user) throw new WorkflowException(409, "Recipient unavailable.");
            if (decimal.Round(input.Allocation, 2) != input.Allocation) throw new WorkflowException(400, "Allocation precision exceeds two decimals.");
            if (a.BeneficiaryAssignments.Where(x => x.Status == "Active" && x.AssignmentId != assignmentId).Sum(x => x.Allocation) + input.Allocation > 100) throw new WorkflowException(409, "Allocation total exceeds 100 percent.");
            b.Allocation = input.Allocation;
        }
        else b.Status = "Revoked";
        b.UpdatedAt = DateTime.UtcNow; a.UpdatedAt = RecordVersion.Next(a.UpdatedAt); Touch(a.Vault);
        Audit(user, input is null ? "RevokeAllocation" : "UpdateAllocation", "BeneficiaryAssignment", assignmentId);
        Notify(b.BeneficiaryId, "Allocation", "Allocation changed", "Your asset allocation was changed by its owner.");
        await repository.Save(ct); return Map(b);
    }
    public async Task<PageDto<ManagedDocumentDto>> Documents(int user, int asset, int page, int size, CancellationToken ct)
    {
        Paging(page, size);
        var all = (await Asset(user, asset, ct)).AssetDocuments.Select(d => new ManagedDocumentDto(d.DocumentId, d.FileName, d.FileType, d.IsEncrypted, d.Status)).ToArray();
        var items = all.Skip((page - 1) * size).Take(size).ToArray();
        return new PageDto<ManagedDocumentDto>(page, size, items);
    }
    public async Task<DocumentContent> OwnerDocument(int user, int asset, int document, CancellationToken ct)
    {
        var a = await Asset(user, asset, ct);
        var d = a.AssetDocuments.SingleOrDefault(d => d.DocumentId == document && d.Status != "Revoked") ?? throw new WorkflowException(404, "Document not found.");
        return await Read(d.StoragePath, ct);
    }
    public async Task RevokeDocument(int user, int asset, int document, CancellationToken ct)
    {
        var a = await Asset(user, asset, ct); Active(a);
        var d = a.AssetDocuments.SingleOrDefault(d => d.DocumentId == document) ?? throw new WorkflowException(404, "Document not found.");
        if (d.Status == "Revoked") throw new WorkflowException(409, "Document already revoked.");
        d.Status = "Revoked"; a.UpdatedAt = RecordVersion.Next(a.UpdatedAt); Touch(a.Vault);
        Audit(user, "RevokeDocument", "AssetDocument", document); await repository.Save(ct);
    }
    private async Task<DocumentContent> Read(string path, CancellationToken ct)
    {
        var content = await protection.Read(store, path, ct);
        return new(content.File.FileName, content.File.FileType, content.File.Content, protection.Verify(content.File.Content, content.Signature, content.SignerId), content.Purpose);
    }
    public async Task AssignExecutor(int user, int vault, ExecutorInput input, CancellationToken ct)
    {
        Validate(input); var v = await Owned(user, vault, ct); Mutable(v);
        await Require(input.ExecutorId, ct, VaultService.ExecutorRole);
        if (input.ExecutorId == user || v.DigitalAssets.Any(a => a.BeneficiaryAssignments.Any(b => b.Status == "Active" && b.BeneficiaryId == input.ExecutorId))) throw new WorkflowException(400, "Executor must be independent.");
        foreach (var a in v.ExecutorAssignments.Where(Assigned)) a.Status = "Revoked";
        repository.Add(new ExecutorAssignment { VaultId = vault, ExecutorId = input.ExecutorId, Status = "Active", AssignedAt = DateTime.UtcNow }); Touch(v);
        Notify(input.ExecutorId, "Assignment", "Vault assigned", "An Owner assigned you to a vault. Review your assignments for the handover workflow.");
        Audit(user, "AssignExecutor", "DigitalVault", vault); await repository.Save(ct);
    }
    private static void Ready(DigitalVault v)
    {
        var assets = v.DigitalAssets.Where(a => a.Status == "Active").ToArray();
        if (assets.Length == 0 || assets.Any(a => a.BeneficiaryAssignments.Where(b => b.Status == "Active").Sum(b => b.Allocation) != 100 || !a.AssetDocuments.Any(d => d.Status == "Active")))
            throw new WorkflowException(409, "Every active asset needs documents and exactly 100 percent active allocation.");
        if (assets.Any(a => a.BeneficiaryAssignments.Any(b => b.Status == "Active" && b.Beneficiary.Status != "Active"))) throw new WorkflowException(409, "Beneficiary unavailable.");
    }
    private HandoverRequest NewRequest(int actor, DigitalVault v, int executor)
    {
        Ready(v); Touch(v);
        var request = new HandoverRequest { VaultId = v.VaultId, Vault = v, ExecutorId = executor, RequestType = "Inheritance", Status = "Pending_Executor", InitiatedAt = DateTime.UtcNow };
        foreach (var a in v.DigitalAssets.Where(a => a.Status == "Active"))
            foreach (var b in a.BeneficiaryAssignments.Where(b => b.Status == "Active")) request.HandoverCases.Add(new HandoverCase { AssignmentId = b.AssignmentId, Assignment = b, Status = "Pending" });
        repository.Add(request); Audit(actor, "CreateHandover", "DigitalVault", v.VaultId);
        Notify(executor, "Handover", "Handover assigned", "A handover has been assigned to you.");
        return request;
    }
    public async Task<HandoverDto> CreateRequest(int user, int vault, CancellationToken ct)
    {
        var v = await Owned(user, vault, ct); Mutable(v); Ready(v);
        var existing = v.ExecutorAssignments.Where(Assigned).ToArray();
        if (existing.Length > 1) throw new WorkflowException(409, "Multiple active executors require administrative correction.");
        int executor;
        if (existing.Length == 1) { executor = existing[0].ExecutorId; await Require(executor, ct, VaultService.ExecutorRole); }
        else
        {
            var candidates = (await repository.UsersByRole(VaultService.ExecutorRole, ct)).Where(u => u.Status == "Active" && u.Roles.Any(r => r.RoleName == VaultService.ExecutorRole) && !u.Authentications.Any(a => a.LockedUntil > DateTime.UtcNow) && u.UserId != user && !v.DigitalAssets.Any(a => a.BeneficiaryAssignments.Any(b => b.Status == "Active" && b.BeneficiaryId == u.UserId))).ToArray();
            if (candidates.Length == 0) throw new WorkflowException(503, "No independent active Executor is available. Provision an eligible Executor before retrying.");
            executor = candidates[System.Security.Cryptography.RandomNumberGenerator.GetInt32(candidates.Length)].UserId;
            await Require(executor, ct, VaultService.ExecutorRole);
            repository.Add(new ExecutorAssignment { VaultId = vault, ExecutorId = executor, Status = "AutoAssigned", AssignedAt = DateTime.UtcNow });
            Audit(user, "AutoAssignExecutor", "DigitalVault", vault);
        }
        if (executor == v.OwnerId || v.DigitalAssets.Any(a => a.BeneficiaryAssignments.Any(b => b.Status == "Active" && b.BeneficiaryId == executor)))
            throw new WorkflowException(409, "Assigned Executor must be independent of the owner and beneficiaries.");
        var r = NewRequest(user, v, executor); r.Executor = await Require(executor, ct, VaultService.ExecutorRole);
        await repository.Save(ct); return Map(r);
    }
    public async Task<HandoverDto> CreateAssignedRequest(int user, int vault, CancellationToken ct)
    {
        await Require(user, ct, VaultService.ExecutorRole);
        var v = await workflow.VaultGraph(vault, ct) ?? throw new WorkflowException(404, "Assigned vault not found.");
        var assignments = v.ExecutorAssignments.Where(Assigned).ToArray();
        if (assignments.Length != 1 || assignments[0].ExecutorId != user) throw new WorkflowException(404, "Assigned vault not found.");
        if (v.OwnerId == user || v.DigitalAssets.Any(a => a.BeneficiaryAssignments.Any(b => b.Status == "Active" && b.BeneficiaryId == user)))
            throw new WorkflowException(403, "Executor must be independent.");
        Mutable(v); Ready(v);
        var request = NewRequest(user, v, user); request.Status = "Draft";
        Audit(user, "AcceptHandover", "DigitalVault", vault);
        await repository.Save(ct); return Map(request);
    }
    public static void Paging(int page, int size) { if (page < 1 || page > 100000 || size is < 1 or > 100) throw new WorkflowException(400, "page >= 1; pageSize must be 1-100."); }
    public async Task<PageDto<HandoverDto>> Requests(int user, string kind, int page, int size, string? status, CancellationToken ct)
    {
        await Require(user, ct, kind == "Operator" ? new[] { VaultService.ExecutorRole } : new[] { kind }); Paging(page, size);
        ValidateStatus(status);
        var requests = (await workflow.UserRequests(user, kind, page, size, status, ct)).ToArray();
        return new(page, size, kind == "Owner" ? await OwnerHandoverDetails(user, requests, ct) : requests.Select(Map).ToArray());
    }
    public async Task<HandoverRequest> OperatorRequest(int user, int id, CancellationToken ct)
    {
        await Require(user, ct, VaultService.ExecutorRole);
        var r = await workflow.RequestGraph(id, ct);
        if (r is null || r.ExecutorId != user || !r.Vault.ExecutorAssignments.Any(a => a.ExecutorId == user && (Assigned(a) || r.Status == "Declined"))) throw new WorkflowException(404, "Assigned request not found.");
        if (r.Vault.OwnerId == user || r.HandoverCases.Any(c => c.Assignment.BeneficiaryId == user)) throw new WorkflowException(403, "Executor must be independent.");
        return r;
    }
    public async Task<OwnerLifeStatusDto> ExecutorOwnerStatus(int user, int id, CancellationToken ct) => await OwnerStatus(await OperatorRequest(user, id, ct), ct);
    public async Task<OwnerLifeStatusDto> LegalOwnerStatus(int user, int id, CancellationToken ct) => await OwnerStatus((await Legal(user, id, ct)).Request, ct);
    private async Task<OwnerLifeStatusDto> OwnerStatus(HandoverRequest request, CancellationToken ct)
    {
        var ownerId = request.Vault.OwnerId; var ownerName = request.Vault.Owner?.FullName ?? "";
        var lastConfirmedAlive = await LastConfirmedAlive(ownerId, ct);
        foreach (var verified in await workflow.ApprovedOwnerRequests(ownerId, ct))
        {
            var death = await HandoverEvidence.FindDeathCertificate(verified, store, protection, ct);
            if (death is null) continue;
            var approval = verified.LegalVerifications.Where(v => v.Status == "Approved" && v.ReviewedAt != null).OrderByDescending(v => v.ReviewedAt).First();
            return new(ownerId, ownerName, "ConfirmedDeceased", request.RequestId, verified.RequestId, death.HandoverDocumentId, approval.VerifierId, approval.ReviewedAt) { LastConfirmedAliveAt = lastConfirmedAlive };
        }
        var evidence = request.Status == "Cancelled" ? null : await HandoverEvidence.FindDeathCertificate(request, store, protection, ct);
        var state = evidence is null ? "NotConfirmed" : request.Status == "Rejected" ? "Rejected" : "ReportedDeceased";
        return new(ownerId, ownerName, state, request.RequestId, evidence is null ? null : request.RequestId, evidence?.HandoverDocumentId, null, null) { LastConfirmedAliveAt = lastConfirmedAlive };
    }
    private static IReadOnlyList<LegalHistoryDto> History(HandoverRequest r) => r.LegalVerifications.OrderByDescending(v => v.SubmittedAt).ThenByDescending(v => v.VerificationId)
        .Select(v => new LegalHistoryDto(v.VerificationId, r.RequestId, v.VerifierId, v.Verifier?.FullName ?? "", v.Status, v.Comment, v.SubmittedAt, v.ReviewedAt)).ToArray();
    public async Task<IReadOnlyList<LegalHistoryDto>> ExecutorHistory(int user, int id, CancellationToken ct) => History(await OperatorRequest(user, id, ct));
    public async Task<IReadOnlyList<LegalHistoryDto>> LegalHistory(int user, int id, CancellationToken ct) => History((await Legal(user, id, ct)).Request);
    private async Task<IReadOnlyList<DocumentDto>> EvidenceMetadata(HandoverRequest request, CancellationToken ct)
    {
        var result = new List<DocumentDto>();
        foreach (var d in request.HandoverDocuments)
        {
            var content = await Read(d.StoragePath, ct);
            result.Add(new(d.HandoverDocumentId, d.FileName, d.FileType, d.IsEncrypted, content.SignatureValid, content.Purpose));
        }
        return result;
    }
    public async Task<IReadOnlyList<DocumentDto>> ExecutorDocuments(int user, int id, CancellationToken ct) => await EvidenceMetadata(await OperatorRequest(user, id, ct), ct);
    public async Task<IReadOnlyList<DocumentDto>> LegalDocuments(int user, int id, CancellationToken ct) => await EvidenceMetadata((await Legal(user, id, ct)).Request, ct);
    public async Task<IReadOnlyList<VerifierOptionDto>> LegalVerifiers(int user, int id, CancellationToken ct)
    {
        var r = await OperatorRequest(user, id, ct);
        return (await repository.UsersByRole(VaultService.VerifierRole, ct)).Where(v => v.UserId != user && v.UserId != r.Vault.OwnerId &&
            !r.HandoverCases.Any(c => c.Assignment.BeneficiaryId == v.UserId))
            .Select(v => new VerifierOptionDto(v.UserId, v.FullName)).ToArray();
    }
    public async Task<HandoverDto> OwnerRequest(int user, int id, CancellationToken ct)
    {
        await Require(user, ct, VaultService.OwnerRole);
        var r = await workflow.RequestGraph(id, ct);
        if (r is null || r.Vault.OwnerId != user) throw new WorkflowException(404, "Request not found.");
        return (await OwnerHandoverDetails(user, new[] { r }, ct))[0];
    }
    public async Task Cancel(int user, int id, CancellationToken ct)
    {
        await OwnerRequest(user, id, ct); var r = (await workflow.RequestGraph(id, ct))!;
        if (r.Status is not ("Pending_Executor" or "Draft" or "Rejected")) throw new WorkflowException(409, "Only awaiting Executor, draft or rejected requests can be cancelled.");
        r.Status = "Cancelled"; foreach (var c in r.HandoverCases) c.Status = "Cancelled"; Touch(r.Vault);
        Audit(user, "CancelHandover", "HandoverRequest", id); await repository.Save(ct);
    }
    private async Task<(HandoverRequest Request, LegalVerification Verification)> Legal(int user, int id, CancellationToken ct)
    {
        await Require(user, ct, VaultService.VerifierRole);
        var r = await workflow.RequestGraph(id, ct) ?? throw new WorkflowException(404, "Request not found.");
        var verification = r.LegalVerifications.Where(v => v.VerifierId == user).OrderByDescending(v => v.SubmittedAt).ThenByDescending(v => v.VerificationId).FirstOrDefault();
        if (verification is null) throw new WorkflowException(404, "Assigned verification not found.");
        if (r.ExecutorId == user || r.Vault.OwnerId == user || r.HandoverCases.Any(c => c.Assignment.BeneficiaryId == user)) throw new WorkflowException(403, "Conflicting legal verifier.");
        return (r, verification);
    }
    public async Task<(HandoverDto Request, ReviewDto Verification)> LegalDetail(int user, int id, CancellationToken ct)
    {
        var (r, v) = await Legal(user, id, ct); return (Map(r), new(v.VerificationId, id, user, v.Status, v.Comment, v.SubmittedAt, v.ReviewedAt));
    }
    public async Task<PageDto<LegalAssetDto>> LegalAssets(int user, int id, int page, int size, CancellationToken ct)
    {
        var (request, _) = await Legal(user, id, ct);
        Paging(page, size);
        var assetIds = request.HandoverCases.Select(c => c.Assignment.AssetId).ToHashSet();
        var items = request.Vault.DigitalAssets.Where(a => assetIds.Contains(a.AssetId)).OrderBy(a => a.AssetId)
            .Skip((page - 1) * size).Take(size)
            .Select(a => new LegalAssetDto(a.AssetId, a.AssetName, a.AssetType, a.Description, a.Status,
                a.AssetDocuments.Where(d => d.Status != "Revoked").OrderBy(d => d.DocumentId)
                    .Select(d => new ManagedDocumentDto(d.DocumentId, d.FileName, d.FileType, d.IsEncrypted, d.Status)).ToArray()))
            .ToArray();
        return new(page, size, items);
    }

    public async Task<DocumentContent> LegalDocument(int user, int id, int document, bool assetDocument, CancellationToken ct)
    {
        var (r, _) = await Legal(user, id, ct);
        if (assetDocument)
        {
            var d = r.Vault.DigitalAssets.Where(a => r.HandoverCases.Any(c => c.Assignment.AssetId == a.AssetId)).SelectMany(a => a.AssetDocuments).SingleOrDefault(d => d.DocumentId == document && d.Status != "Revoked") ?? throw new WorkflowException(404, "Document not found.");
            return await Read(d.StoragePath, ct);
        }
        var doc = r.HandoverDocuments.SingleOrDefault(d => d.HandoverDocumentId == document) ?? throw new WorkflowException(404, "Document not found.");
        return await Read(doc.StoragePath, ct);
    }
    public async Task<ReviewDto> Review(int user, int id, string decision, ReviewInput input, CancellationToken ct)
    {
        Validate(input); if (string.IsNullOrWhiteSpace(input.Comment)) throw new WorkflowException(400, "Review comment required.");
        if (decision is not ("Approved" or "Rejected" or "Additional_Info")) throw new WorkflowException(400, "Unsupported legal decision.");
        var (r, v) = await Legal(user, id, ct);
        if (r.Status != "Pending_Legal" || v.Status != "Pending" || r.LegalVerifications.Where(x => x.Status == "Pending").Any(x => x.VerifierId != user)) throw new WorkflowException(409, "Verification is not pending exclusively for this reviewer.");
        if (decision == "Approved")
        {
            await Require(r.ExecutorId, ct, VaultService.ExecutorRole);
            if (!r.Vault.ExecutorAssignments.Any(a => a.ExecutorId == r.ExecutorId && Assigned(a))) throw new WorkflowException(409, "Executor assignment is no longer active.");
            await HandoverEvidence.RequireDeathCertificate(r, store, protection, ct);
            if (r.Vault.Status != "Active" || r.HandoverCases.Count == 0 || !r.HandoverDocuments.Any() || r.HandoverCases.Any(c => c.Assignment.Status != "Active" || c.Assignment.Beneficiary.Status != "Active")) throw new WorkflowException(409, "Handover evidence and active cases are required.");
            var caseAssets = r.Vault.DigitalAssets.Where(a => r.HandoverCases.Any(c => c.Assignment.AssetId == a.AssetId)).ToArray();
            if (caseAssets.Length != r.HandoverCases.Select(c => c.Assignment.AssetId).Distinct().Count() || caseAssets.Any(a => a.Status != "Active" || !a.AssetDocuments.Any(d => d.Status == "Active")))
                throw new WorkflowException(409, "Every handover asset needs an active asset record and signed document.");
            foreach (var d in r.Vault.DigitalAssets.Where(a => r.HandoverCases.Any(c => c.Assignment.AssetId == a.AssetId)).SelectMany(a => a.AssetDocuments).Where(d => d.Status is "Active" or "Pending_Legal"))
            {
                var content = await Read(d.StoragePath, ct);
                if (d.Status == "Active" && !content.SignatureValid) throw new WorkflowException(409, "Living-owner document signature is no longer valid.");
            }
            foreach (var d in r.HandoverDocuments) _ = await Read(d.StoragePath, ct);
            r.Status = "Approved";
            Audit(user, "ConfirmOwnerDeceased", "User", r.Vault.OwnerId);
            foreach (var c in r.HandoverCases) c.Status = "In_Progress";
            foreach (var d in r.Vault.DigitalAssets.Where(a => r.HandoverCases.Any(c => c.Assignment.AssetId == a.AssetId)).SelectMany(a => a.AssetDocuments).Where(d => d.Status == "Pending_Legal")) d.Status = "Active";
            foreach (var recipient in r.HandoverCases.Select(c => c.Assignment.BeneficiaryId).Distinct()) Notify(recipient, "Inheritance", "Inheritance available", "Verify your identity to review the assigned inheritance.");
        }
        else r.Status = decision == "Additional_Info" ? "Additional_Info" : "Rejected";
        v.Status = decision; v.Comment = input.Comment.Trim(); v.ReviewedAt = DateTime.UtcNow; Touch(r.Vault);
        Audit(user, "LegalReview", "LegalVerification", v.VerificationId); Notify(r.ExecutorId, "LegalReview", "Legal review updated", "Review the assigned handover for the legal decision.");
        await repository.Save(ct); return new(v.VerificationId, id, user, v.Status, v.Comment, v.SubmittedAt, v.ReviewedAt);
    }
    public async Task<PageDto<CaseDto>> Cases(int user, int page, int size, CancellationToken ct)
    {
        await Require(user, ct, VaultService.BeneficiaryRole); Paging(page, size);
        return new(page, size, (await workflow.BeneficiaryCases(user, page, size, ct)).Select(c => new CaseDto(c.HandoverId, c.Assignment.AssetId, user, c.Status)).ToArray());
    }
    private async Task<HandoverCase> BeneficiaryCase(int user, int id, CancellationToken ct)
    {
        await Require(user, ct, VaultService.BeneficiaryRole); var c = await workflow.Case(id, ct);
        return c is not null && c.Assignment.BeneficiaryId == user && c.Assignment.Status == "Active" && c.Request.LegalVerifications.Any(v => v.Status == "Approved") && c.Status is "In_Progress" or "Completed" or "Received" ? c : throw new WorkflowException(404, "Inheritance case not found.");
    }
    public async Task<CaseDto> CaseDetail(int user, int id, CancellationToken ct) { var c = await BeneficiaryCase(user, id, ct); return new(id, c.Assignment.AssetId, user, c.Status); }
    public async Task<DocumentContent> CaseDocument(int user, int id, int document, CancellationToken ct)
    {
        var c = await BeneficiaryCase(user, id, ct); var d = c.Request.HandoverDocuments.SingleOrDefault(d => d.HandoverDocumentId == document) ?? throw new WorkflowException(404, "Document not found."); return await Read(d.StoragePath, ct);
    }
    public async Task<IReadOnlyList<DocumentDto>> CaseDocuments(int user, int id, CancellationToken ct)
    {
        var c = await BeneficiaryCase(user, id, ct);
        return c.Request.HandoverDocuments.Select(d => new DocumentDto(d.HandoverDocumentId, d.FileName, d.FileType, d.IsEncrypted)).ToArray();
    }
    public async Task<ReceiptDto> Receipt(int user, int id, CancellationToken ct)
    {
        var c = await BeneficiaryCase(user, id, ct);
        return Map(c.BeneficiaryReceipts.SingleOrDefault(r => r.BeneficiaryId == user) ?? throw new WorkflowException(404, "Receipt not found."));
    }
    public async Task<ReceiptDto> ConfirmReceipt(int user, int id, ReceiptInput input, CancellationToken ct)
    {
        Validate(input); var c = await BeneficiaryCase(user, id, ct);
        if (c.Status != "Completed" || c.Request.Status != "Completed" || c.BeneficiaryReceipts.Any()) throw new WorkflowException(409, "Only a completed, unreceived handover can be confirmed.");
        var receipt = new BeneficiaryReceipt { HandoverId = id, BeneficiaryId = user, Status = "Confirmed", ConfirmationNote = input.Note?.Trim(), ConfirmedAt = DateTime.UtcNow };
        repository.Add(receipt); c.Status = "Received"; Touch(c.Request.Vault);
        Audit(user, "ConfirmReceipt", "HandoverCase", id); Notify(c.Request.ExecutorId, "Receipt", "Receipt confirmed", "The beneficiary confirmed receipt of their handover.");
        await repository.Save(ct); return Map(receipt);
    }
    public async Task<PageDto<NotificationDto>> Notifications(int user, int page, int size, CancellationToken ct)
    {
        Paging(page, size); var rows = await workflow.Notifications(user, page, size, ct); return new(page, size, rows.Select(n => new NotificationDto(n.NotificationId, n.Type, n.Title, n.Message, n.IsRead, n.CreatedAt)).ToArray());
    }
    public async Task ReadNotification(int user, int id, CancellationToken ct)
    {
        var n = await workflow.Notification(user, id, ct) ?? throw new WorkflowException(404, "Notification not found."); n.IsRead = true; await repository.Save(ct);
    }
}
