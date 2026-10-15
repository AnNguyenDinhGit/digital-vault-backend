using LegacyVault.BLL.DTOs;
using LegacyVault.DAL.Entities;

namespace LegacyVault.BLL.Extensions;

public static class MappingExtensions
{
    public static VaultDto ToDto(this DigitalVault v)
        => new(v.VaultId, v.VaultName, v.Description, v.Status);

    public static AssetDto ToDto(this DigitalAsset a)
        => new(
            a.AssetId,
            a.AssetName,
            a.AssetType,
            a.Description,
            a.Status,
            a.AssetDocuments
                .Where(d => d.Status == "Active")
                .Select(d => new DocumentDto(d.DocumentId, d.FileName, d.FileType, d.IsEncrypted))
                .ToArray()
        );

    public static HandoverDto ToDto(this HandoverRequest r)
        => new(
            r.RequestId,
            r.VaultId,
            r.RequestType,
            r.Status,
            r.InitiatedAt,
            r.HandoverDocuments
                .Select(d => new DocumentDto(d.HandoverDocumentId, d.FileName, d.FileType, d.IsEncrypted))
                .ToArray(),
            r.HandoverCases
                .Select(c => new CaseDto(c.HandoverId, c.Assignment.AssetId, c.Assignment.BeneficiaryId, c.Status))
                .ToArray()
        );

    public static AssignmentDto ToDto(this BeneficiaryAssignment a)
        => new(a.AssignmentId, a.AssetId, a.BeneficiaryId, a.Beneficiary.FullName, a.Allocation, a.Status);

    public static ReceiptDto ToDto(this BeneficiaryReceipt r)
        => new(r.ReceiptId, r.HandoverId, r.BeneficiaryId, r.Status, r.ConfirmationNote, r.ConfirmedAt);
}