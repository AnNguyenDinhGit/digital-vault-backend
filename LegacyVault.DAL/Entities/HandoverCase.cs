using System;
using System.Collections.Generic;

namespace LegacyVault.DAL.Entities;

public partial class HandoverCase
{
    public int HandoverId { get; set; }

    public int RequestId { get; set; }

    public int AssignmentId { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? CompletedAt { get; set; }

    public virtual BeneficiaryAssignment Assignment { get; set; } = null!;

    public virtual ICollection<BeneficiaryReceipt> BeneficiaryReceipts { get; set; } = new List<BeneficiaryReceipt>();

    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();

    public virtual HandoverRequest Request { get; set; } = null!;
}
