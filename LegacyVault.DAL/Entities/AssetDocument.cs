using System;
using System.Collections.Generic;
<<<<<<< HEAD
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
=======

namespace LegacyVault.DAL.Entities;

public partial class AssetDocument
{
    public int DocumentId { get; set; }

    public int AssetId { get; set; }

    public string FileName { get; set; } = null!;

    public string FileType { get; set; } = null!;

    public string StoragePath { get; set; } = null!;

    public bool IsEncrypted { get; set; }

    public int UploadedBy { get; set; }

    public string Status { get; set; } = null!;

    public DateTime UploadedAt { get; set; }

    public virtual DigitalAsset Asset { get; set; } = null!;

    public virtual User UploadedByNavigation { get; set; } = null!;
>>>>>>> a4348ee039e341d95316f69f42cacc3f656357d7
}
