using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LegacyVault.DAL.Entities
{
    public class DigitalAsset
    {
        public int AssetId { get; set; }
        public int VaultId { get; set; }
        public string AssetName { get; set; } = string.Empty;
        public string AssetType { get; set; } = string.Empty;
        public decimal ValueEstimate { get; set; }
        public string? EncryptedData { get; set; }

        public ICollection<AssetDocument> AssetDocuments { get; set; } = new List<AssetDocument>();
    }
}
