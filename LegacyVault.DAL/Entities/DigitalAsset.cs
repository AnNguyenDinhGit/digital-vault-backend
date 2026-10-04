using System;
using System.Collections.Generic;
<<<<<<< HEAD
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
=======

namespace LegacyVault.DAL.Entities;

public partial class DigitalAsset
{
    public int AssetId { get; set; }

    public int VaultId { get; set; }

    public string AssetName { get; set; } = null!;

    public string AssetType { get; set; } = null!;

    public string? Description { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<AssetDocument> AssetDocuments { get; set; } = new List<AssetDocument>();

    public virtual ICollection<BeneficiaryAssignment> BeneficiaryAssignments { get; set; } = new List<BeneficiaryAssignment>();

    public virtual ICollection<EncryptionKey> EncryptionKeys { get; set; } = new List<EncryptionKey>();

    public virtual DigitalVault Vault { get; set; } = null!;
>>>>>>> a4348ee039e341d95316f69f42cacc3f656357d7
}
