using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LegacyVault.DAL.Entities
{
    public class AssetDocument
    {
        public int DocumentId { get; set; }
        public int AssetId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string StoragePath { get; set; } = string.Empty;
        public long FileSizeByte { get; set; }
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        public DigitalAsset? Asset { get; set; }
    }
}
