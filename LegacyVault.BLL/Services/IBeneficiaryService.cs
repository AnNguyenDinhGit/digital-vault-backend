using LegacyVault.BLL.DTOs;
namespace LegacyVault.BLL.Services;
public interface IBeneficiaryService
{
    Task<PageDto<CaseDto>> Cases(int user, string session, int page, int size, CancellationToken ct);
    Task<CaseDto> Detail(int user, string session, int id, CancellationToken ct);
    Task<IReadOnlyList<DocumentDto>> Documents(int user, string session, int id, CancellationToken ct);
    Task<DocumentContent> Document(int user, string session, int id, int document, CancellationToken ct);
    Task<ReceiptDto> Receipt(int user, string session, int id, CancellationToken ct);
    Task<ReceiptDto> Confirm(int user, string session, int id, ReceiptInput input, CancellationToken ct);
}