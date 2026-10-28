using Azure;
using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Extensions;
using LegacyVault.BLL.Security;
using LegacyVault.DAL.Entities;
using LegacyVault.DAL.Repositories;
using LegacyVault.DAL.Storage;
using Microsoft.Extensions.Configuration;
using System.ComponentModel.DataAnnotations;
using System.Drawing.Printing;
using System.Net;
using System.Net.Mail;

namespace LegacyVault.BLL.Services;

public sealed class VaultService(
    IVaultRepository repository,
    IDocumentStore store,
    DocumentProtection protection,
    IOtpSender mailSender,
    IConfiguration configuration,
    IWorkflowRepository? workflow = null)
{
    public const string OwnerRole = "Owner";
    public const string ExecutorRole = "Executor";
    public const string BeneficiaryRole = "Beneficiary";
    public const string VerifierRole = "LegalVerifier";
    public const string AdminRole = "Admin";

    private async Task<User> Role(int userId, string role, CancellationToken ct)
    {
        var user = await repository.User(userId, ct);
        if (user is null || user.Status != "Active" || user.Authentications.Any(a => a.LockedUntil > DateTime.UtcNow) || !user.Roles.Any(x => x.RoleName == role))
            throw new WorkflowException(403, "The active user does not have the required role.");
        return user;
    }

    private static AssetDto Map(DigitalAsset a) => new(a.AssetId, a.AssetName, a.AssetType, a.Description, a.Status,
        a.AssetDocuments.Where(d => d.Status == "Active").Select(d => new DocumentDto(d.DocumentId, d.FileName, d.FileType, d.IsEncrypted)).ToArray());

    private static VaultDto Map(DigitalVault v) => new(v.VaultId, v.VaultName, v.Description, v.Status);

    private static void ValidateInput(object input)
    {
        if (!Validator.TryValidateObject(input, new ValidationContext(input), new List<ValidationResult>(), true))
            throw new WorkflowException(400, "Invalid request input.");
    }

    private static void Editable(DigitalVault vault)
    {
        if (vault.HandoverRequests.Any(r => r.Status != "Cancelled" && r.Status != "Declined")) throw new WorkflowException(409, "Handover records protect this vault from mutation.");
    }

    private async Task OperatorRole(int user, CancellationToken ct)
    {
        var account = await repository.User(user, ct);
        if (account is null || account.Status != "Active" || account.Authentications.Any(a => a.LockedUntil > DateTime.UtcNow) || !account.Roles.Any(r => r.RoleName == ExecutorRole))
            throw new WorkflowException(403, "Active Executor role required.");
    }

    private async Task<DigitalVault> OwnedVault(int user, int vault, CancellationToken ct)
    {
        await Role(user, OwnerRole, ct);
        var v = await repository.Vault(vault, ct);
        if (v is null || v.OwnerId != user) throw new WorkflowException(404, "Vault not found.");
        return v;
    }

    public async Task<PageDto<VaultDto>> OwnerVaults(int user, int page, int pageSize, CancellationToken ct)
    {
        await Role(user, OwnerRole, ct);
        ManagementService.Paging(page, pageSize);
        var allVaults = await repository.OwnerVaults(user, ct);
        var total = allVaults.Count;
        var items = allVaults.Skip((page - 1) * pageSize).Take(pageSize).Select(Map).ToArray();
        return new PageDto<VaultDto>(page, pageSize, items);
    }

    public async Task<VaultDto> OwnerVault(int user, int vault, CancellationToken ct) => Map(await OwnedVault(user, vault, ct));

    public async Task<VaultDto> CreateVault(int user, CreateVaultInput input, CancellationToken ct)
    {
        await Role(user, OwnerRole, ct);
        ValidateInput(input);
        var now = DateTime.UtcNow;
        var vault = new DigitalVault { OwnerId = user, VaultName = input.Name.Trim(), Description = input.Description?.Trim(), Status = "Active", CreatedAt = now, UpdatedAt = now };
        repository.Add(vault);
        repository.Add(new AuditLog { UserId = user, Action = "CreateVault", EntityType = "DigitalVault", EntityId = null, Result = "Success", Timestamp = now });
        await repository.Save(ct);
        return Map(vault);
    }

    public async Task<AssetDto> CreateAsset(int user, CreateAssetInput input, CancellationToken ct)
    {
        ValidateInput(input);
        var vault = await OwnedVault(user, input.VaultId, ct);
        if (vault.Status != "Active") throw new WorkflowException(409, "Vault is not active.");
        Editable(vault); vault.UpdatedAt = RecordVersion.Next(vault.UpdatedAt);
        var now = DateTime.UtcNow;
        var asset = new DigitalAsset { VaultId = vault.VaultId, AssetName = input.Name.Trim(), AssetType = input.Type.Trim(), Description = input.Description?.Trim(), Status = "Active", CreatedAt = now, UpdatedAt = now };
        repository.Add(asset);
        Audit(user, "CreateAsset", "DigitalVault", vault.VaultId, "Success");
        await repository.Save(ct);
        return Map(asset);
    }

    private static HandoverDto Map(HandoverRequest r) => r.ToDto();

    private async Task<DigitalAsset> Owned(int user, int asset, CancellationToken ct)
    {
        await Role(user, OwnerRole, ct);
        var a = await repository.Asset(asset, ct);
        if (a is null || a.Vault.OwnerId != user) throw new WorkflowException(404, "Asset not found.");
        return a;
    }

    private async Task<HandoverRequest> Executed(int user, int request, CancellationToken ct)
    {
        await OperatorRole(user, ct);
        var r = await repository.Request(request, ct);
        if (r is null || r.ExecutorId != user || !r.Vault.ExecutorAssignments.Any(x => x.ExecutorId == user && (x.Status == "Active" || x.Status == "AutoAssigned" || r.Status == "Declined")))
            throw new WorkflowException(404, "Handover request not found or executor assignment is inactive.");
        if (r.Vault.OwnerId == user || r.HandoverCases.Any(c => c.Assignment.BeneficiaryId == user))
            throw new WorkflowException(403, "Executor must be independent of the owner and beneficiaries.");
        return r;
    }

    public async Task<HandoverDto> CreateHandoverRequest(int user, int vaultId, string requestType, CancellationToken ct)
    {
        if (requestType != "Inheritance") throw new WorkflowException(400, "Only Inheritance request type is supported.");
        return await new ManagementService(repository, workflow ?? throw new WorkflowException(503, "Workflow persistence is not configured."), store, protection).CreateRequest(user, vaultId, ct);
    }

    public async Task<PageDto<AssetDto>> OwnerAssets(int user, int page, int pageSize, CancellationToken ct, int? vaultId = null)
    {
        await Role(user, OwnerRole, ct);
        ManagementService.Paging(page, pageSize);
        if (vaultId is <= 0) throw new WorkflowException(400, "vaultId must be positive.");
        if (vaultId is not null) _ = await OwnedVault(user, vaultId.Value, ct);
        var allAssets = await repository.OwnerAssets(user, ct, vaultId);
        var items = allAssets.Skip((page - 1) * pageSize).Take(pageSize).Select(Map).ToArray();
        return new PageDto<AssetDto>(page, pageSize, items);
    }

    public async Task<AssetDto> OwnerAsset(int user, int asset, CancellationToken ct, int? vaultId = null)
    {
        if (vaultId is <= 0) throw new WorkflowException(400, "vaultId must be positive.");
        if (vaultId is not null) _ = await OwnedVault(user, vaultId.Value, ct);
        var ownedAsset = await Owned(user, asset, ct);
        if (vaultId is not null && ownedAsset.VaultId != vaultId.Value) throw new WorkflowException(404, "Asset not found in the selected vault.");
        return Map(ownedAsset);
    }

    public async Task<PageDto<BeneficiaryDto>> Beneficiaries(int user, int? asset, int page, int pageSize, CancellationToken ct)
    {
        await Role(user, OwnerRole, ct);
        ManagementService.Paging(page, pageSize);
        var assets = asset.HasValue ? new List<DigitalAsset> { await Owned(user, asset.Value, ct) } : await repository.OwnerAssets(user, ct);
        var allItems = assets.SelectMany(a => a.BeneficiaryAssignments.Select(b => new BeneficiaryDto(a.AssetId, b.BeneficiaryId, b.Beneficiary.FullName, b.Allocation, b.Status))).ToArray();
        var items = allItems.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return new PageDto<BeneficiaryDto>(page, pageSize, items);
    }

    public async Task<BeneficiaryDto> AssignBeneficiary(int owner, int assetId, AssignBeneficiaryInput input, CancellationToken ct)
    {
        var asset = await Owned(owner, assetId, ct);
        ValidateInput(input);
        if (decimal.Round(input.Allocation, 2) != input.Allocation) throw new WorkflowException(400, "Allocation allows at most two decimal places.");
        if (asset.Status != "Active" || asset.Vault.Status != "Active") throw new WorkflowException(409, "Vault or asset is not active.");
        Editable(asset.Vault); asset.Vault.UpdatedAt = RecordVersion.Next(asset.Vault.UpdatedAt);

        var recipient = await repository.UserByEmail(input.Email.Trim().ToLowerInvariant(), ct);
        if (recipient is null || recipient.Status != "Active")
        {
            // Tự động gửi email mời đăng ký tài khoản cho người thụ hưởng nếu chưa tồn tại hoặc chưa Active
            string appUrl = configuration["AppUrl"] ?? "http://localhost:5173";
            string registerUrl = $"{appUrl}/register?email={Uri.EscapeDataString(input.Email)}";
            await SendInvitationEmailAsync(input.Email, registerUrl);

            throw new WorkflowException(404, "Active recipient account not found. An invitation email has been sent to the recipient to register first.");
        }

        if (recipient.UserId == owner) throw new WorkflowException(400, "An owner cannot designate themselves as beneficiary of their own asset.");
        if (asset.BeneficiaryAssignments.Any(x => x.BeneficiaryId == recipient.UserId && (x.Status == "Active" || x.Status == "Pending")))
            throw new WorkflowException(409, "This user is already a pending or active beneficiary of the asset.");
        if (asset.BeneficiaryAssignments.Where(x => x.Status == "Active").Sum(x => x.Allocation) + input.Allocation > 100m)
            throw new WorkflowException(409, "Total active beneficiary allocation cannot exceed 100 percent.");
        var now = DateTime.UtcNow;
        if (!recipient.Roles.Any(x => x.RoleName == BeneficiaryRole))
        {
            var role = await repository.Role(BeneficiaryRole, ct);
            if (role is null) { role = new Role { RoleName = BeneficiaryRole }; repository.Add(role); }
            recipient.Roles.Add(role);
            recipient.UpdatedAt = RecordVersion.Next(recipient.UpdatedAt);
        }
        var assignment = new BeneficiaryAssignment
        {
            AssetId = assetId,
            BeneficiaryId = recipient.UserId,
            Allocation = input.Allocation,
            Status = "Active",
            CreatedAt = now,
            UpdatedAt = now
        };
        repository.Add(assignment);
        asset.UpdatedAt = RecordVersion.Next(asset.UpdatedAt);
        Audit(owner, "AssignBeneficiary", "DigitalAsset", assetId, "Success");
        repository.Add(new Notification { UserId = recipient.UserId, Type = "Allocation", Title = "Beneficiary designated", Message = "An owner designated you as a beneficiary. Access requires a legally approved handover and identity verification.", CreatedAt = now });
        try
        {
            await repository.Save(ct);
        }
        catch (RepositoryDuplicateException) { throw new WorkflowException(409, "Beneficiary configuration changed. Reload and retry."); }
        return new BeneficiaryDto(assetId, recipient.UserId, recipient.FullName, assignment.Allocation, assignment.Status);
    }

    private async Task SendInvitationEmailAsync(string toEmail, string registerUrl)
    {
        var mailHost = configuration["MailOptions:Host"];
        var mailPort = int.Parse(configuration["MailOptions:Port"] ?? "587");
        var mailUser = configuration["MailOptions:Username"];
        var mailPass = configuration["MailOptions:Password"];
        var mailFrom = configuration["MailOptions:From"];

        using var client = new SmtpClient(mailHost, mailPort)
        {
            Credentials = new NetworkCredential(mailUser, mailPass),
            EnableSsl = true
        };

        var mailMessage = new MailMessage
        {
            From = new MailAddress(mailFrom),
            Subject = "Loi moi tro thanh nguoi thu huong tai san ky thuat so - LegacyVault",
            Body = $"Xin chao,\n\n" +
                   $"Ban da duoc chi dinh lam nguoi thu huong mot tai san tren he thong LegacyVault.\n" +
                   $"Vui long tao tai khoan bằng cach truy cap vao duong dan sau de tiep nhan quyen thu huong:\n\n" +
                   $"{registerUrl}\n\n" +
                   $"Tran trong,\nLegacyVault Team",
            IsBodyHtml = false
        };

        mailMessage.To.Add(toEmail);
        await client.SendMailAsync(mailMessage);
    }

    private static void Validate(UploadFile file)
    {
        if (file.Content.Length > 10 * 1024 * 1024) throw new WorkflowException(413, "Document exceeds 10 MB.");
        if (file.Content.Length == 0 || string.IsNullOrWhiteSpace(file.FileName) || file.FileName.Length > 255 || file.FileName is "." or ".." || file.FileName.Any(char.IsControl) || file.FileName.IndexOfAny(new[] { '/', '\\', '\0' }) >= 0)
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
        if (file.FileType == "text/plain")
        {
            try { var text = new System.Text.UTF8Encoding(false, true).GetString(bytes); if (text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))) throw new WorkflowException(400, "Invalid text content."); }
            catch (System.Text.DecoderFallbackException) { throw new WorkflowException(400, "Text must be UTF-8."); }
        }
    }

    private void RequireSignature(UploadFile file, byte[]? signature, int signer)
    {
        if (!protection.Verify(file.Content, signature, signer)) throw new WorkflowException(400, "Invalid signature, untrusted/expired certificate, or signer certificate not configured.");
    }
    private Task<string> Store(UploadFile file, int signer, byte[]? signature, CancellationToken ct, string? purpose = null) =>
        store.Write(protection.Encrypt(new ProtectedDocument(file, signer, signature, purpose)), ct);

    public async Task<DocumentDto> UploadOwnerDocument(int user, int assetId, int? beneficiaryId,
        UploadFile file, byte[]? signature, CancellationToken ct)
    {
        var asset = await Owned(user, assetId, ct);
        if (asset.Vault.Status != "Active" || asset.Status != "Active") throw new WorkflowException(409, "Vault or asset is not active.");
        Editable(asset.Vault);
        if (beneficiaryId is not null)
        {
            if (beneficiaryId == user) throw new WorkflowException(400, "Owner cannot inherit their own asset.");
            await Role(beneficiaryId.Value, BeneficiaryRole, ct);
            if (!asset.BeneficiaryAssignments.Any(a => a.BeneficiaryId == beneficiaryId && a.Status == "Active"))
                throw new WorkflowException(409, "Select an existing active allocation; upload does not create allocations.");
        }
        Validate(file); RequireSignature(file, signature, user);
        var path = await Store(file, user, signature, ct);
        try
        {
            var doc = new AssetDocument
            {
                AssetId = assetId,
                FileName = file.FileName,
                FileType = file.FileType,
                StoragePath = path,
                IsEncrypted = true,
                UploadedBy = user,
                Status = "Active",
                UploadedAt = DateTime.UtcNow
            };
            repository.Add(doc);
            asset.UpdatedAt = RecordVersion.Next(asset.UpdatedAt); asset.Vault.UpdatedAt = RecordVersion.Next(asset.Vault.UpdatedAt);
            Audit(user, "UploadAssetDocument", "DigitalAsset", assetId, "Alive_Signed");
            await repository.Save(ct); return new(doc.DocumentId, doc.FileName, doc.FileType, true);
        }
        catch { await store.Delete(path); throw; }
    }

    public async Task<PageDto<HandoverDto>> Requests(int user, int page, int pageSize, CancellationToken ct)
    {
        await OperatorRole(user, ct);
        ManagementService.Paging(page, pageSize);
        var allRequests = (await repository.Requests(user, ct))
            .Where(r => r.Vault.ExecutorAssignments.Any(x => x.ExecutorId == user && (x.Status == "Active" || x.Status == "AutoAssigned" || r.Status == "Declined")))
            .Select(Map)
            .ToArray();
        var items = allRequests.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return new PageDto<HandoverDto>(page, pageSize, items);
    }

    public async Task<HandoverDto> Request(int user, int request, CancellationToken ct)
    {
        var r = await Executed(user, request, ct);
        var documents = new List<DocumentDto>();
        foreach (var d in r.HandoverDocuments)
        {
            var content = await Read(d.StoragePath, ct);
            documents.Add(new DocumentDto(d.HandoverDocumentId, d.FileName, d.FileType, d.IsEncrypted, content.SignatureValid, content.Purpose));
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
        var doc = await protection.Read(store, path, ct);
        return new DocumentContent(doc.File.FileName, doc.File.FileType, doc.File.Content, protection.Verify(doc.File.Content, doc.Signature, doc.SignerId), doc.Purpose);
    }

    public Task<DocumentDto> UploadDeathCertificate(int user, int request, UploadFile file, byte[]? signature, CancellationToken ct) =>
        UploadHandoverDocument(user, request, file, signature, ct, "DeathCertificate");

    public async Task<DocumentDto> UploadHandoverDocument(int user, int request, UploadFile file, byte[]? signature, CancellationToken ct, string purpose = "Evidence")
    {
        var r = await Executed(user, request, ct);
        if (r.Status is not ("Draft" or "Rejected" or "Additional_Info")) throw new WorkflowException(409, "Documents are editable only before legal submission.");
        if (purpose is not ("DeathCertificate" or "Evidence")) throw new WorkflowException(400, "Unknown handover document purpose.");
        Validate(file); if (signature is not null) RequireSignature(file, signature, user);
        var path = await Store(file, user, signature, ct, purpose);
        try
        {
            var d = new HandoverDocument { RequestId = request, FileName = file.FileName, FileType = file.FileType, StoragePath = path, IsEncrypted = true, UploadedBy = user, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            repository.Add(d); r.Vault.UpdatedAt = RecordVersion.Next(r.Vault.UpdatedAt); Audit(user, purpose == "DeathCertificate" ? "UploadDeathCertificate" : "UploadHandoverDocument", "HandoverRequest", request, "Success");
            if (purpose == "DeathCertificate") Audit(user, "ReportOwnerDeceased", "User", r.Vault.OwnerId, "ReportedDeceased");
            await repository.Save(ct); return new(d.HandoverDocumentId, d.FileName, d.FileType, true);
        }
        catch { await store.Delete(path); throw; }
    }

    public async Task StartHandover(int user, int request, CancellationToken ct)
    {
        var r = await Executed(user, request, ct);
        if (r.Status != "Approved" || !r.LegalVerifications.Any(v => v.Status == "Approved")) throw new WorkflowException(409, "Approved legal verification required.");
        r.Status = "In_Progress"; r.Vault.UpdatedAt = RecordVersion.Next(r.Vault.UpdatedAt);
        Audit(user, "StartHandover", "HandoverRequest", request, "Success"); await repository.Save(ct);
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
            repository.Add(new HandoverDocument
            {
                RequestId = request,
                FileName = Path.GetFileName(file.FileName),
                FileType = file.FileType,
                StoragePath = path,
                IsEncrypted = true,
                UploadedBy = user,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            r.Status = "Completed"; r.CompletedAt = DateTime.UtcNow; r.Vault.UpdatedAt = RecordVersion.Next(r.Vault.UpdatedAt);
            foreach (var c in r.HandoverCases) { c.Status = "Completed"; c.CompletedAt = DateTime.UtcNow; }
            foreach (var recipient in r.HandoverCases.Select(c => c.Assignment.BeneficiaryId).Distinct())
                repository.Add(new Notification { UserId = recipient, Type = "Handover", Title = "Handover completed", Message = "Review your completed inheritance and confirm receipt.", CreatedAt = DateTime.UtcNow });
            Audit(user, "CompleteHandover", "HandoverRequest", request, "Success");
            await repository.Save(ct);
        }
        catch { await store.Delete(path); throw; }
    }

    public async Task<int> RequestLegal(int user, int request, int verifier, string? comment, UploadFile file, CancellationToken ct)
    {
        var r = await Executed(user, request, ct);
        if (r.Status is not ("Draft" or "Rejected" or "Additional_Info")) throw new WorkflowException(409, "Only draft, rejected or additional-info requests can be submitted.");
        await Role(verifier, VerifierRole, ct);
        if (verifier == user || verifier == r.Vault.OwnerId || r.HandoverCases.Any(c => c.Assignment.BeneficiaryId == verifier)) throw new WorkflowException(400, "An independent legal verifier is required.");
        if (comment?.Length > 2000) throw new WorkflowException(400, "Comment exceeds 2000 characters.");
        await HandoverEvidence.RequireDeathCertificate(r, store, protection, ct);
        Validate(file);
        var path = await Store(file, user, null, ct);
        try
        {
            var verification = new LegalVerification { RequestId = request, VerifierId = verifier, Comment = comment, Status = "Pending", SubmittedAt = DateTime.UtcNow };
            repository.Add(verification);
            repository.Add(new HandoverDocument
            {
                RequestId = request,
                FileName = Path.GetFileName(file.FileName),
                FileType = file.FileType,
                StoragePath = path,
                IsEncrypted = true,
                UploadedBy = user,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            r.Status = "Pending_Legal"; r.SubmittedAt = DateTime.UtcNow; r.Vault.UpdatedAt = RecordVersion.Next(r.Vault.UpdatedAt);
            Audit(user, "RequestLegalVerification", "HandoverRequest", request, "Success");
            repository.Add(new Notification { UserId = verifier, Type = "LegalReview", Title = "Legal review assigned", Message = "A handover is awaiting your independent legal review.", CreatedAt = DateTime.UtcNow });
            await repository.Save(ct);
            return verification.VerificationId;
        }
        catch { await store.Delete(path); throw; }
    }

    public async Task<PageDto<InheritanceDto>> Inherited(int user, int page, int pageSize, CancellationToken ct)
    {
        await Role(user, BeneficiaryRole, ct);
        ManagementService.Paging(page, pageSize);
        var allItems = (await repository.Inherited(user, ct)).Select(x => new InheritanceDto(x.HandoverId, x.Assignment.Allocation, Map(x.Assignment.Asset))).ToArray();
        var items = allItems.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        return new PageDto<InheritanceDto>(page, pageSize, items);
    }

    public async Task<InheritanceDto> InheritedAsset(int user, int asset, CancellationToken ct)
    {
        var result = await Inherited(user, 1, 100, ct);
        return result.Items.FirstOrDefault(x => x.Asset.AssetId == asset) ?? throw new WorkflowException(404, "Inherited asset not found.");
    }

    public async Task<DocumentContent> InheritedDocument(int user, int asset, int document, CancellationToken ct)
    {
        await InheritedAsset(user, asset, ct);
        var a = await repository.Asset(asset, ct) ?? throw new WorkflowException(404, "Asset not found.");
        var d = a.AssetDocuments.SingleOrDefault(x => x.DocumentId == document && x.Status == "Active") ?? throw new WorkflowException(404, "Released document not found.");
        return await Read(d.StoragePath, ct);
    }

    private void Audit(int user, string action, string type, int id, string result) => repository.Add(new AuditLog
    { UserId = user, Action = action, EntityType = type, EntityId = id, Result = result, Timestamp = DateTime.UtcNow });

    public async Task AcceptBeneficiary(int beneficiaryUserId, int assetId, CancellationToken ct)
    {
        await Role(beneficiaryUserId, BeneficiaryRole, ct);
        var asset = await repository.Asset(assetId, ct) ?? throw new WorkflowException(404, "Asset not found.");

        var assignment = asset.BeneficiaryAssignments.FirstOrDefault(x => x.BeneficiaryId == beneficiaryUserId && x.Status == "Pending")
            ?? throw new WorkflowException(404, "Pending beneficiary assignment not found.");

        assignment.Status = "Active";
        assignment.UpdatedAt = DateTime.UtcNow;
        asset.UpdatedAt = RecordVersion.Next(asset.UpdatedAt);

        Audit(beneficiaryUserId, "AcceptBeneficiaryAssignment", "DigitalAsset", assetId, "Success");
        await repository.Save(ct);
    }

    public async Task RejectBeneficiary(int beneficiaryUserId, int assetId, CancellationToken ct)
    {
        await Role(beneficiaryUserId, BeneficiaryRole, ct);
        var asset = await repository.Asset(assetId, ct) ?? throw new WorkflowException(404, "Asset not found.");

        var assignment = asset.BeneficiaryAssignments.FirstOrDefault(x => x.BeneficiaryId == beneficiaryUserId && x.Status == "Pending")
            ?? throw new WorkflowException(404, "Pending beneficiary assignment not found.");

        assignment.Status = "Rejected";
        assignment.UpdatedAt = DateTime.UtcNow;
        asset.UpdatedAt = RecordVersion.Next(asset.UpdatedAt);

        Audit(beneficiaryUserId, "RejectBeneficiaryAssignment", "DigitalAsset", assetId, "Success");
        await repository.Save(ct);
    }
}