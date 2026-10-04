namespace LegacyVault.BLL.DTOs;
public sealed record AssetDto(int AssetId, string Name, string Type, string? Description, string Status, IReadOnlyList<DocumentDto> Documents);
public sealed record DocumentDto(int DocumentId, string FileName, string FileType, bool IsEncrypted, bool? SignatureValid = null);
public sealed record BeneficiaryDto(int AssetId, int BeneficiaryId, string FullName, decimal Allocation, string Status);
public sealed record HandoverDto(int RequestId, int VaultId, string RequestType, string Status, DateTime InitiatedAt, IReadOnlyList<DocumentDto> Documents, IReadOnlyList<CaseDto> Cases);
public sealed record CaseDto(int HandoverId, int AssetId, int BeneficiaryId, string Status);
public sealed record InheritanceDto(int HandoverId, decimal Allocation, AssetDto Asset);
public sealed record UploadFile(string FileName, string FileType, byte[] Content);
public sealed record DocumentContent(string FileName, string FileType, byte[] Content, bool SignatureValid);
public sealed record LoginDto(int UserId, string[] Roles);
public sealed class WorkflowException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
