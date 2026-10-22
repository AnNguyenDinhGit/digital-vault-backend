namespace LegacyVault.BLL.DTOs;

using System.ComponentModel.DataAnnotations;


public sealed record MessageDto(
    long MessageId,
    int ConversationId,
    int SenderId,
    string SenderName,
    string SenderRole,
    string Content,
    string MessageType,
    DateTime SentAt
);



public sealed record SendMessageInput(
    [Required(ErrorMessage = "Nội dung tin nhắn không được để trống.")]
    [StringLength(4000, MinimumLength = 1, ErrorMessage = "Nội dung tin nhắn phải từ 1 đến 4000 ký tự.")]
    string Content,

    [Required]
    string MessageType = "Text"
);