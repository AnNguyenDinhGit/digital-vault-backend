namespace LegacyVault.BLL.DTOs;

public sealed record OwnerSummaryDto(int UserId, string FullName);
public sealed record VerifierOptionDto(int UserId, string FullName);
public sealed record HandoverOverviewDto(int RequestId, int VaultId, string VaultName, OwnerSummaryDto Owner,
    int ExecutorId, string ExecutorName, string RequestType, string Status, DateTime InitiatedAt,
    DateTime? SubmittedAt, DateTime? CompletedAt, bool IsAssignedToMe);
public sealed record OwnerLifeStatusDto(int OwnerId, string FullName, string Status, int RequestId,
    int? EvidenceRequestId, int? DeathCertificateDocumentId, int? VerifierId, DateTime? VerifiedAt);
public sealed record LegalHistoryDto(int VerificationId, int RequestId, int VerifierId, string VerifierName,
    string Status, string? Comment, DateTime SubmittedAt, DateTime? ReviewedAt);
