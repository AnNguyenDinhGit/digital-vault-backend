using System;
using System.Collections.Generic;

namespace LegacyVault.DAL.Entities;

public partial class BeneficiaryAssignment
{
    public int AssignmentId { get; set; }

    public int AssetId { get; set; }

    public int BeneficiaryId { get; set; }

    public decimal Allocation { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual DigitalAsset Asset { get; set; } = null!;

    public virtual User Beneficiary { get; set; } = null!;

    public virtual ICollection<HandoverCase> HandoverCases { get; set; } = new List<HandoverCase>();
}
