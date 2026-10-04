using System;
using System.Collections.Generic;

namespace LegacyVault.DAL.Entities;

public partial class Notification
{
    public int NotificationId { get; set; }

    public int UserId { get; set; }

    public int? HandoverId { get; set; }

    public string Type { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string Message { get; set; } = null!;

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual HandoverCase? Handover { get; set; }

    public virtual User User { get; set; } = null!;
}
