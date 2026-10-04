using System;
using System.Collections.Generic;

namespace LegacyVault.DAL.Entities;

public partial class AuditLog
{
    public long LogId { get; set; }

    public int? UserId { get; set; }

    public string Action { get; set; } = null!;

    public string? EntityType { get; set; }

    public int? EntityId { get; set; }

    public DateTime Timestamp { get; set; }

    public string? Ipaddress { get; set; }

    public string Result { get; set; } = null!;

    public virtual User? User { get; set; }
}
