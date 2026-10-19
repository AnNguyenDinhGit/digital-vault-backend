using System;
using System.Collections.Generic;

namespace LegacyVault.DAL.Entities;

public partial class Conversation
{
    public int ConversationId { get; set; }
    public int? RequestId { get; set; } // Gắn cuộc trò chuyện với Đơn bàn giao

    public string? Title { get; set; }

    public string Type { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // THÊM DÒNG NÀY (Navigation Property)
    public virtual HandoverRequest? Request { get; set; }

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
}
