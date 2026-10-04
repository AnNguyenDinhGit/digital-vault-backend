using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LegacyVault.DAL.Context;
using LegacyVault.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.BLL.Services
{
    public class BeneficiaryService : IBeneficiaryService
    {
        private readonly LegacyVaultDbContext _context;

        public BeneficiaryService(LegacyVaultDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<BeneficiaryReceipt>> GetNotificationsAsync(int beneficiaryId)
        {
            return await _context.BeneficiaryReceipts
                .Where(r => r.BeneficiaryId == beneficiaryId)
                .ToListAsync();
        }

        public async Task<IEnumerable<DigitalAsset>> GetInheritedAssetsAsync(int beneficiaryId, int vaultId)
        {
            // Kiểm tra xem Beneficiary có quyền tiếp nhận kho số vaultId hay không
            var hasAccess = await _context.BeneficiaryReceipts
                .AnyAsync(r => r.BeneficiaryId == beneficiaryId);

            if (!hasAccess) return new List<DigitalAsset>();

            return await _context.DigitalAssets
                .Include(a => a.AssetDocuments)
                .Where(a => a.VaultId == vaultId)
                .ToListAsync();
        }

        public async Task<bool> ConfirmReceiptAndSignAsync(int receiptId, string signatureHash, string? note)
        {
            var receipt = await _context.BeneficiaryReceipts.FindAsync(receiptId);
            if (receipt == null) return false;

            receipt.Status = "Confirmed";
            receipt.DigitalSignatureHash = signatureHash;
            receipt.Note = note;
            receipt.ReceivedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> NotifyExecutorAsync(int receiptId, string message)
        {
            var receipt = await _context.BeneficiaryReceipts.FindAsync(receiptId);
            if (receipt == null) return false;

            receipt.Note = $"[Notice to Executor]: {message}";
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<AssetDocument?> GetDocumentForDownloadAsync(int documentId, int beneficiaryId)
        {
            return await _context.AssetDocuments
                .FirstOrDefaultAsync(d => d.DocumentId == documentId);
        }

        public async Task<string?> DecryptAssetDataAsync(int assetId, string decryptKey)
        {
            var asset = await _context.DigitalAssets.FindAsync(assetId);
            if (asset == null || string.IsNullOrEmpty(asset.EncryptedData)) return null;

            // Xử lý giải mã dữ liệu Client-side hoặc Server-side cơ bản
            return asset.EncryptedData;
        }
    }
}
