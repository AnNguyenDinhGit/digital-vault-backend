using System;
using System.Collections.Generic;

namespace LegacyVault.DAL.Entities;

public partial class TwoFactorAuth
{
    public int TwoFactorId { get; set; }

    public int UserId { get; set; }

    public string Method { get; set; } = null!;

    public string Secret { get; set; } = null!;

    public bool IsEnabled { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
