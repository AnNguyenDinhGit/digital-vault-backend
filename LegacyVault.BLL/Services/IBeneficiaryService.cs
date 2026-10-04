using LegacyVault.DAL.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LegacyVault.BLL.Services
{
    public interface IBeneficiaryService
    {
        Task<IEnumerable<BeneficiaryReceipt>> GetNotificationsAsync(int beneficiaryId);
        Task<IEnumerable<DigitalAsset>> GetInheritedAssetsAsync(int beneficiaryId, int vaultId);
        Task<bool> ConfirmReceiptAndSignAsync(int receiptId, string signatureHash, string? note);
        Task<bool> NotifyExecutorAsync(int receiptId, string message);
        Task<AssetDocument?> GetDocumentForDownloadAsync(int documentId, int beneficiaryId);
        Task<string?> DecryptAssetDataAsync(int assetId, string decryptKey);
    }
}
