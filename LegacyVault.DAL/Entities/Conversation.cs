using System;
using System.Collections.Generic;

namespace LegacyVault.DAL.Entities;

public partial class Conversation
{
    public int ConversationId { get; set; }

    public string? Title { get; set; }

    public string Type { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
}
