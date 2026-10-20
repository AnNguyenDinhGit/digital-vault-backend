using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Security;
namespace LegacyVault.BLL.Services;
// Replaces the unconnected EF/placeholder implementation with guarded repository-backed workflows.
public sealed class BeneficiaryService(ManagementService management, OtpService otp) : IBeneficiaryService
{
    public Task<PageDto<CaseDto>> Cases(int user, string session, int page, int size, CancellationToken ct)
    {
        otp.RequireVerified(session);
        return management.Cases(user, page, size, ct);
    }
    public Task<CaseDto> Detail(int user, string session, int id, CancellationToken ct) { otp.RequireVerified(session); return management.CaseDetail(user, id, ct); }
    public Task<IReadOnlyList<DocumentDto>> Documents(int user, string session, int id, CancellationToken ct) { otp.RequireVerified(session); return management.CaseDocuments(user, id, ct); }
    public Task<DocumentContent> Document(int user, string session, int id, int document, CancellationToken ct) { otp.RequireVerified(session); return management.CaseDocument(user, id, document, ct); }
    public Task<ReceiptDto> Receipt(int user, string session, int id, CancellationToken ct) { otp.RequireVerified(session); return management.Receipt(user, id, ct); }
    public Task<ReceiptDto> Confirm(int user, string session, int id, ReceiptInput input, CancellationToken ct) { otp.RequireVerified(session); return management.ConfirmReceipt(user, id, input, ct); }
}