using System;
using System.Collections.Generic;

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
}
