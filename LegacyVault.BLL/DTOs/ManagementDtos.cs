using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace LegacyVault.BLL.DTOs;
public sealed record ExecutorAssignmentDto(int VaultId, int ExecutorId, string Status, DateTime AssignedAt)
{
    public string? VaultName { get; init; }
    public OwnerSummaryDto? Owner { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateAssetInput
{
    [Required, StringLength(100)] public string Name { get; init; } = null!;
    [Required, StringLength(50), RegularExpression(@"^[\x20-\x7E]+$")] public string Type { get; init; } = null!;
    [StringLength(4000)] public string? Description { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AllocationInput([Range(typeof(decimal), "0.01", "100")] decimal Allocation);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExecutorInput([Range(1, int.MaxValue)] int ExecutorId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReviewInput([Required, StringLength(2000, MinimumLength = 1)] string Comment);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DeclineHandoverInput([Required, StringLength(2000, MinimumLength = 1)] string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReceiptInput([StringLength(2000)] string? Note);
public sealed record AssignmentDto(int AssignmentId, int AssetId, int BeneficiaryId, string FullName, decimal Allocation, string Status);
public sealed record ManagedDocumentDto(int DocumentId, string FileName, string FileType, bool IsEncrypted, string Status);
public sealed record ReviewDto(int VerificationId, int RequestId, int VerifierId, string Status, string? Comment, DateTime SubmittedAt, DateTime? ReviewedAt);
public sealed record ReceiptDto(int ReceiptId, int HandoverId, int BeneficiaryId, string Status, string? Note, DateTime ConfirmedAt);
public sealed record NotificationDto(int NotificationId, string Type, string Title, string Message, bool IsRead, DateTime CreatedAt);
public sealed record PageDto<T>(int Page, int PageSize, IReadOnlyList<T> Items);
