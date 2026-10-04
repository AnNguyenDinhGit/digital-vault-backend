using System;
using System.Collections.Generic;

namespace LegacyVault.DAL.Entities;

public partial class DigitalVault
{
    public int VaultId { get; set; }

    public int OwnerId { get; set; }

    public string VaultName { get; set; } = null!;

    public string? Description { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<DigitalAsset> DigitalAssets { get; set; } = new List<DigitalAsset>();

    public virtual ICollection<ExecutorAssignment> ExecutorAssignments { get; set; } = new List<ExecutorAssignment>();

    public virtual ICollection<HandoverRequest> HandoverRequests { get; set; } = new List<HandoverRequest>();

    public virtual User Owner { get; set; } = null!;
}
