using System;
using System.Collections.Generic;

namespace LegacyVault.DAL.Entities;

public partial class EncryptionKey
{
    public int KeyId { get; set; }

    public int AssetId { get; set; }

    public string EncryptedKey { get; set; } = null!;

    public string Algorithm { get; set; } = null!;

    public int KeyVersion { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? RotatedAt { get; set; }

    public virtual DigitalAsset Asset { get; set; } = null!;
}
