using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Security;
using LegacyVault.DAL.Entities;
using LegacyVault.DAL.Repositories;
using LegacyVault.DAL.Storage;
using System.ComponentModel.DataAnnotations;

namespace LegacyVault.BLL.Services;

public sealed class VaultService(IVaultRepository repository, IDocumentStore store, DocumentProtection protection)
{
    public const string OwnerRole = "Owner";
    public const string ExecutorRole = "Executor";
    public const string BeneficiaryRole = "Beneficiary";
    public const string VerifierRole = "LegalVerifier";
    public const string AdminRole = "Admin";

    private async Task<User> Role(int userId, string role, CancellationToken ct)
    {
        var user = await repository.User(userId, ct);
        if (user is null || user.Status != "Active" || !user.Roles.Any(x => x.RoleName == role))
            throw new WorkflowException(403, "The active user does not have the required role.");
        return user;
    }
    private static AssetDto Map(DigitalAsset a) => new(a.AssetId, a.AssetName, a.AssetType, a.Description, a.Status,
        a.AssetDocuments.Select(d => new DocumentDto(d.DocumentId, d.FileName, d.FileType, d.IsEncrypted)).ToArray());
    private static VaultDto Map(DigitalVault v) => new(v.VaultId, v.VaultName, v.Description, v.Status);
    private static void ValidateInput(object input)
    {
        if (!Validator.TryValidateObject(input, new ValidationContext(input), new List<ValidationResult>(), true))
            throw new WorkflowException(400, "Invalid request input.");
    }
    private async Task<DigitalVault> OwnedVault(int user, int vault, CancellationToken ct)
    {
        await Role(user, OwnerRole, ct);
        var v = await repository.Vault(vault, ct);
        if (v is null || v.OwnerId != user) throw new WorkflowException(404, "Vault not found.");
        return v;
    }
    public async Task<IReadOnlyList<VaultDto>> OwnerVaults(int user, CancellationToken ct)
    {
        await Role(user, OwnerRole, ct);
        return (await repository.OwnerVaults(user, ct)).Select(Map).ToArray();
    }
    public async Task<VaultDto> OwnerVault(int user, int vault, CancellationToken ct) => Map(await OwnedVault(user, vault, ct));
    public async Task<VaultDto> CreateVault(int user, CreateVaultInput input, CancellationToken ct)
    {
        await Role(user, OwnerRole, ct);
        ValidateInput(input);
        var now = DateTime.UtcNow;
        var vault = new DigitalVault { OwnerId = user, VaultName = input.Name.Trim(), Description = input.Description?.Trim(), Status = "Active", CreatedAt = now, UpdatedAt = now };
        repository.Add(vault);
        await repository.Save(ct);
        return Map(vault);
    }
    public async Task<AssetDto> CreateAsset(int user, CreateAssetInput input, CancellationToken ct)
    {
        ValidateInput(input);
        var vault = await OwnedVault(user, input.VaultId, ct);
        if (vault.Status != "Active") throw new WorkflowException(409, "Vault is not active.");
        var now = DateTime.UtcNow;
        var asset = new DigitalAsset { VaultId = vault.VaultId, AssetName = input.Name.Trim(), AssetType = input.Type.Trim(), Description = input.Description?.Trim(), Status = "Active", CreatedAt = now, UpdatedAt = now };
        repository.Add(asset);
        await repository.Save(ct);
        return Map(asset);
    }
    private static HandoverDto Map(HandoverRequest r) => new(r.RequestId, r.VaultId, r.RequestType, r.Status, r.InitiatedAt,
        r.HandoverDocuments.Select(d => new DocumentDto(d.HandoverDocumentId, d.FileName, d.FileType, d.IsEncrypted)).ToArray(),
        r.HandoverCases.Select(c => new CaseDto(c.HandoverId, c.Assignment.AssetId, c.Assignment.BeneficiaryId, c.Status)).ToArray());
    private async Task<DigitalAsset> Owned(int user, int asset, CancellationToken ct)
    {
        await Role(user, OwnerRole, ct);
        var a = await repository.Asset(asset, ct);
        if (a is null || a.Vault.OwnerId != user) throw new WorkflowException(404, "Asset not found.");
        return a;
    }

    private async Task<int> GetOrAssignRandomExecutor(DigitalVault vault, CancellationToken ct)
    {
        // Kiểm tra xem Vault đã có Executor (người dùng chọn hoặc hệ thống tự gán) chưa
        var activeAssignment = vault.ExecutorAssignments
            .FirstOrDefault(x => x.Status == "Active" || x.Status == "AutoAssigned");

        if (activeAssignment is not null)
            return activeAssignment.ExecutorId;

        // Lấy danh sách Executor đang Active trên toàn hệ thống
        var availableExecutors = await repository.UsersByRole(ExecutorRole, ct);
        var activeExecutors = availableExecutors.Where(u => u.Status == "Active").ToList();

        if (activeExecutors.Count == 0)
            throw new WorkflowException(500, "No active executors available in the system.");

        // Chọn ngẫu nhiên 1 Executor
        var randomExecutor = activeExecutors[Random.Shared.Next(activeExecutors.Count)];

        // Lưu thông tin phân công tự động vào DB
        var autoAssignment = new ExecutorAssignment
        {
            VaultId = vault.VaultId,
            ExecutorId = randomExecutor.UserId,
            Status = "AutoAssigned",
            AssignedAt = DateTime.UtcNow
        };

        repository.Add(autoAssignment);
        await repository.Save(ct);

        return randomExecutor.UserId;
    }
    private async Task<HandoverRequest> Executed(int user, int request, CancellationToken ct)
    {
        await Role(user, ExecutorRole, ct);
        var r = await repository.Request(request, ct);
        if (r is null || r.ExecutorId != user || !r.Vault.ExecutorAssignments.Any(x => x.ExecutorId == user && (x.Status == "Active" || x.Status == "AutoAssigned")))
            throw new WorkflowException(404, "Handover request not found or executor assignment is inactive.");
        return r;
    }

    public async Task<HandoverDto> CreateHandoverRequest(int user, int vaultId, string requestType, CancellationToken ct)
    {
        var vault = await repository.Vault(vaultId, ct);
        if (vault is null || vault.Status != "Active")
            throw new WorkflowException(404, "Vault not found or inactive.");

        // Tự động lấy Executor do Owner chọn, hoặc random gán Executor mới nếu Vault chưa có
        var executorId = await GetOrAssignRandomExecutor(vault, ct);

        var now = DateTime.UtcNow;
        var request = new HandoverRequest
        {
            VaultId = vaultId,
            ExecutorId = executorId,
            RequestType = requestType,
            Status = "Draft",
            InitiatedAt = now
        };

        repository.Add(request);
        Audit(user, "CreateHandoverRequest", "DigitalVault", vaultId, "Success");
        await repository.Save(ct);

        return Map(request);
    }
    public async Task<IReadOnlyList<AssetDto>> OwnerAssets(int user, CancellationToken ct)
    {
        await Role(user, OwnerRole, ct);
        return (await repository.OwnerAssets(user, ct)).Select(Map).ToArray();
    }
    public async Task<AssetDto> OwnerAsset(int user, int asset, CancellationToken ct) => Map(await Owned(user, asset, ct));
    public async Task<IReadOnlyList<BeneficiaryDto>> Beneficiaries(int user, int? asset, CancellationToken ct)
    {
        await Role(user, OwnerRole, ct);
        var assets = asset.HasValue ? new List<DigitalAsset> { await Owned(user, asset.Value, ct) } : await repository.OwnerAssets(user, ct);
        return assets.SelectMany(a => a.BeneficiaryAssignments.Select(b => new BeneficiaryDto(a.AssetId, b.BeneficiaryId, b.Beneficiary.FullName, b.Allocation, b.Status))).ToArray();
    }
    public async Task<BeneficiaryDto> AssignBeneficiary(int owner, int assetId, AssignBeneficiaryInput input, CancellationToken ct)
    {
        var asset = await Owned(owner, assetId, ct);
        ValidateInput(input);
        if (decimal.Round(input.Allocation, 2) != input.Allocation) throw new WorkflowException(400, "Allocation allows at most two decimal places.");
        if (asset.Status != "Active" || asset.Vault.Status != "Active") throw new WorkflowException(409, "Vault or asset is not active.");
        var recipient = await repository.UserByEmail(input.Email.Trim().ToLowerInvariant(), ct);
        if (recipient is null || recipient.Status != "Active") throw new WorkflowException(404, "Active recipient account not found. Ask the recipient to register first.");
        if (recipient.UserId == owner) throw new WorkflowException(400, "An owner cannot designate themselves as beneficiary of their own asset.");
        if (asset.BeneficiaryAssignments.Any(x => x.BeneficiaryId == recipient.UserId && x.Status == "Active"))
            throw new WorkflowException(409, "This user is already an active beneficiary of the asset.");
        if (asset.BeneficiaryAssignments.Where(x => x.Status == "Active").Sum(x => x.Allocation) + input.Allocation > 100m)
            throw new WorkflowException(409, "Total active beneficiary allocation cannot exceed 100 percent.");
        var now = DateTime.UtcNow;
        if (!recipient.Roles.Any(x => x.RoleName == BeneficiaryRole))
        {
            var role = await repository.Role(BeneficiaryRole, ct);
            if (role is null) { role = new Role { RoleName = BeneficiaryRole }; repository.Add(role); }
            recipient.Roles.Add(role);
            recipient.UpdatedAt = now;
        }
        var assignment = new BeneficiaryAssignment { AssetId = assetId, BeneficiaryId = recipient.UserId, Allocation = input.Allocation,
            Status = "Active", CreatedAt = now, UpdatedAt = now };
        repository.Add(assignment);
        // Updating the asset protects the allocation sum against concurrent assignments.
        asset.UpdatedAt = now;
        Audit(owner, "AssignBeneficiary", "DigitalAsset", assetId, "Success");
        try { await repository.Save(ct); }
        catch (RepositoryDuplicateException) { throw new WorkflowException(409, "Beneficiary configuration changed. Reload and retry."); }
        return new BeneficiaryDto(assetId, recipient.UserId, recipient.FullName, assignment.Allocation, assignment.Status);
    }
    private static void Validate(UploadFile file)
    {
        if (file.Content.Length is 0 or > 10 * 1024 * 1024 || string.IsNullOrWhiteSpace(file.FileName) || file.FileName.Length > 255)
            throw new WorkflowException(400, "A document of 1 byte to 10 MB with a valid filename is required.");
        if (file.FileType.Length > 50 || !new[] { "application/pdf", "image/png", "image/jpeg", "text/plain" }.Contains(file.FileType))
            throw new WorkflowException(400, "Supported document types: PDF, PNG, JPEG and plain text.");
        var bytes = file.Content;
        var valid = file.FileType switch
        {
            "application/pdf" => bytes.AsSpan().StartsWith("%PDF-"u8),
            "image/png" => bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/jpeg" => bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255,
            _ => !bytes.Contains((byte)0)
        };
        if (!valid) throw new WorkflowException(400, "Document content does not match its declared type.");
    }
    private void RequireSignature(UploadFile file, byte[]? signature, int signer)
    {
        if (!protection.Verify(file.Content, signature, signer)) throw new WorkflowException(400, "Invalid signature, untrusted/expired certificate, or signer certificate not configured.");
    }
    private Task<string> Store(UploadFile file, int signer, byte[]? signature, CancellationToken ct) =>
        store.Write(protection.Encrypt(new ProtectedDocument(file, signer, signature)), ct);

    public async Task<DocumentDto> UploadOwnerDocument(int user, int assetId, int beneficiaryId, bool ownerAlive,
        UploadFile file, byte[]? signature, UploadFile? deathCertificate, CancellationToken ct)
    {
        var asset = await Owned(user, assetId, ct);
        if (asset.Vault.Status != "Active" || asset.Status != "Active") throw new WorkflowException(409, "Vault or asset is not active.");
        if (beneficiaryId == user) throw new WorkflowException(400, "An owner cannot designate themselves as beneficiary of their own asset.");
        await Role(beneficiaryId, BeneficiaryRole, ct);
        Validate(file);
        if (ownerAlive) RequireSignature(file, signature, user);
        else
        {
            if (deathCertificate is null) throw new WorkflowException(400, "Death certificate is required when ownerAlive is false.");
            Validate(deathCertificate);
        }
        // Existing allocations must remain valid; do not silently overwrite other beneficiaries.
        var assignment = asset.BeneficiaryAssignments.SingleOrDefault(x => x.BeneficiaryId == beneficiaryId && x.Status == "Active");
        if (assignment is null && asset.BeneficiaryAssignments.Any(x => x.Status == "Active"))
            throw new WorkflowException(409, "Asset already has active beneficiaries; select an existing beneficiary.");
        var stored = new List<string>();
        try
        {
            var path = await Store(file, user, ownerAlive ? signature : null, ct); stored.Add(path);
            var doc = new AssetDocument { AssetId = assetId, FileName = Path.GetFileName(file.FileName), FileType = file.FileType,
                StoragePath = path, IsEncrypted = true, UploadedBy = user, Status = ownerAlive ? "Active" : "Pending_Legal", UploadedAt = DateTime.UtcNow };
            repository.Add(doc);
            if (deathCertificate is not null && !ownerAlive)
            {
                var deathPath = await Store(deathCertificate, user, null, ct); stored.Add(deathPath);
                repository.Add(new AssetDocument { AssetId = assetId, FileName = Path.GetFileName(deathCertificate.FileName), FileType = deathCertificate.FileType,
                    StoragePath = deathPath, IsEncrypted = true, UploadedBy = user, Status = "Pending_Legal", UploadedAt = DateTime.UtcNow });
            }
            if (assignment is null) repository.Add(new BeneficiaryAssignment { AssetId = assetId, BeneficiaryId = beneficiaryId, Allocation = 100,
                Status = "Active", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            asset.UpdatedAt = DateTime.UtcNow;
            Audit(user, "UploadAssetDocument", "DigitalAsset", assetId, ownerAlive ? "Alive_Signed" : "Pending_Legal");
            await repository.Save(ct);
            return new DocumentDto(doc.DocumentId, doc.FileName, doc.FileType, true);
        }
        catch { foreach (var path in stored) await store.Delete(path); throw; }
    }
    public async Task<IReadOnlyList<HandoverDto>> Requests(int user, CancellationToken ct)
    {
        await Role(user, ExecutorRole, ct);
        return (await repository.Requests(user, ct)).Where(r => r.Vault.ExecutorAssignments.Any(x => x.ExecutorId == user && x.Status == "Active")).Select(Map).ToArray();
    }
    public async Task<HandoverDto> Request(int user, int request, CancellationToken ct)
    {
        var r = await Executed(user, request, ct);
        var documents = new List<DocumentDto>();
        foreach (var d in r.HandoverDocuments)
        {
            var content = await Read(d.StoragePath, ct);
            documents.Add(new DocumentDto(d.HandoverDocumentId, d.FileName, d.FileType, d.IsEncrypted, content.SignatureValid));
        }
        return Map(r) with { Documents = documents };
    }
    public async Task<DocumentContent> RequestDocument(int user, int request, int document, CancellationToken ct)
    {
        var r = await Executed(user, request, ct);
        var d = r.HandoverDocuments.SingleOrDefault(x => x.HandoverDocumentId == document) ?? throw new WorkflowException(404, "Document not found.");
        return await Read(d.StoragePath, ct);
    }
    private async Task<DocumentContent> Read(string path, CancellationToken ct)
    {
        var doc = protection.Decrypt(await store.Read(path, ct));
        return new DocumentContent(doc.File.FileName, doc.File.FileType, doc.File.Content, protection.Verify(doc.File.Content, doc.Signature, doc.SignerId));
    }
    public async Task Complete(int user, int request, UploadFile file, byte[] signature, CancellationToken ct)
    {
        var r = await Executed(user, request, ct);
        if (r.Status != "Approved" && r.Status != "In_Progress") throw new WorkflowException(409, "Request must be approved or in progress.");
        if (!r.LegalVerifications.Any(x => x.Status == "Approved") || r.HandoverCases.Count == 0)
            throw new WorkflowException(409, "Approved legal verification and handover cases are required.");
        Validate(file); RequireSignature(file, signature, user);
        var path = await Store(file, user, signature, ct);
        try
        {
            repository.Add(new HandoverDocument { RequestId = request, FileName = Path.GetFileName(file.FileName), FileType = file.FileType,
                StoragePath = path, IsEncrypted = true, UploadedBy = user, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            r.Status = "Completed"; r.CompletedAt = DateTime.UtcNow;
            foreach (var c in r.HandoverCases) { c.Status = "Completed"; c.CompletedAt = DateTime.UtcNow; }
            Audit(user, "CompleteHandover", "HandoverRequest", request, "Success");
            await repository.Save(ct);
        }
        catch { await store.Delete(path); throw; }
    }
    public async Task<int> RequestLegal(int user, int request, int verifier, string? comment, UploadFile file, CancellationToken ct)
    {
        var r = await Executed(user, request, ct);
        if (r.Status != "Draft" && r.Status != "Rejected") throw new WorkflowException(409, "Only draft or rejected requests can be submitted.");
        await Role(verifier, VerifierRole, ct);
        if (verifier == user || verifier == r.Vault.OwnerId) throw new WorkflowException(400, "An independent legal verifier is required.");
        if (comment?.Length > 2000) throw new WorkflowException(400, "Comment exceeds 2000 characters.");
        Validate(file);
        var path = await Store(file, user, null, ct);
        try
        {
            var verification = new LegalVerification { RequestId = request, VerifierId = verifier, Comment = comment, Status = "Pending", SubmittedAt = DateTime.UtcNow };
            repository.Add(verification);
            repository.Add(new HandoverDocument { RequestId = request, FileName = Path.GetFileName(file.FileName), FileType = file.FileType,
                StoragePath = path, IsEncrypted = true, UploadedBy = user, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            r.Status = "Pending_Legal"; r.SubmittedAt = DateTime.UtcNow;
            Audit(user, "RequestLegalVerification", "HandoverRequest", request, "Success");
            await repository.Save(ct);
            return verification.VerificationId;
        }
        catch { await store.Delete(path); throw; }
    }
    public async Task<IReadOnlyList<InheritanceDto>> Inherited(int user, CancellationToken ct)
    {
        await Role(user, BeneficiaryRole, ct);
        return (await repository.Inherited(user, ct)).Select(x => new InheritanceDto(x.HandoverId, x.Assignment.Allocation, Map(x.Assignment.Asset))).ToArray();
    }
    public async Task<InheritanceDto> InheritedAsset(int user, int asset, CancellationToken ct) =>
        (await Inherited(user, ct)).FirstOrDefault(x => x.Asset.AssetId == asset) ?? throw new WorkflowException(404, "Inherited asset not found.");
    public async Task<DocumentContent> InheritedDocument(int user, int asset, int document, CancellationToken ct)
    {
        await InheritedAsset(user, asset, ct);
        var a = await repository.Asset(asset, ct) ?? throw new WorkflowException(404, "Asset not found.");
        var d = a.AssetDocuments.SingleOrDefault(x => x.DocumentId == document && x.Status == "Active") ?? throw new WorkflowException(404, "Released document not found.");
        return await Read(d.StoragePath, ct);
    }
    private void Audit(int user, string action, string type, int id, string result) => repository.Add(new AuditLog
    { UserId = user, Action = action, EntityType = type, EntityId = id, Result = result, Timestamp = DateTime.UtcNow });

}
