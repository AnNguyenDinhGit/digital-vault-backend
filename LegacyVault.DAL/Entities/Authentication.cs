using System;
using System.Collections.Generic;

namespace LegacyVault.DAL.Entities;

public partial class Authentication
{
    public int AuthId { get; set; }

    public int UserId { get; set; }

    public string PasswordHash { get; set; } = null!;

    public int FailedLoginCount { get; set; }

    public DateTime? LockedUntil { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
