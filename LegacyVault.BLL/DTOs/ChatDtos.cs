namespace LegacyVault.BLL.DTOs;

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
    string Content,
    string MessageType = "Text"
);