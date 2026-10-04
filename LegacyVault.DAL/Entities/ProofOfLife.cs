using System;
using System.Collections.Generic;

namespace LegacyVault.DAL.Entities;

public partial class ProofOfLife
{
    public int CheckId { get; set; }

    public int UserId { get; set; }

    public int FrequencyDays { get; set; }

    public int GracePeriodDays { get; set; }

    public DateTime LastPingAt { get; set; }

    public DateTime NextPingDue { get; set; }

    public string Status { get; set; } = null!;

    public int MissedCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
