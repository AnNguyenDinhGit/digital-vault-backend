using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LegacyVault.BLL.DTOs;
using LegacyVault.DAL.Context;
using LegacyVault.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.BLL.Services;

public class ChatService(LegacyVaultDbContext db)
{
    // 1. Lấy hoặc Tạo cuộc trò chuyện riêng cho Đơn bàn giao
    public async Task<Conversation> GetOrCreateConversationAsync(int requestId, CancellationToken ct)
    {
        var conversation = await db.Conversations
            .FirstOrDefaultAsync(c => c.RequestId == requestId, ct);

        if (conversation is null)
        {
            conversation = new Conversation
            {
                RequestId = requestId,
                Title = $"Chat - Handover #{requestId}",
                Type = "Handover",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync(ct);
        }

        return conversation;
    }

    // 2. Lấy Lịch sử Tin nhắn có Phân trang (Pagination)
    public async Task<PageDto<MessageDto>> GetMessagesAsync(int userId, int requestId, int page, int pageSize, CancellationToken ct)
    {
        ManagementService.Paging(page, pageSize);

        var conversation = await GetOrCreateConversationAsync(requestId, ct);

        var query = db.Messages
            .AsNoTracking()
            .Include(m => m.Sender)
            .ThenInclude(u => u.Roles)
            .Where(m => m.ConversationId == conversation.ConversationId);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(m => m.SentAt) // Mới nhất xếp trước để phân trang
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MessageDto(
                m.MessageId,
                m.ConversationId,
                m.SenderId,
                m.Sender.FullName,
                m.Sender.Roles.Select(r => r.RoleName).FirstOrDefault() ?? "User",
                m.Content,
                m.MessageType,
                m.SentAt
            ))
            .ToListAsync(ct);

        return new PageDto<MessageDto>(page, pageSize, items);
    }

    // 3. Lưu tin nhắn mới vào DB
    public async Task<MessageDto> SaveMessageAsync(int senderId, int requestId, SendMessageInput input, CancellationToken ct)
    {
        var conversation = await GetOrCreateConversationAsync(requestId, ct);
        var sender = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.UserId == senderId, ct)
            ?? throw new WorkflowException(404, "User không tồn tại.");

        var message = new Message
        {
            ConversationId = conversation.ConversationId,
            SenderId = senderId,
            Content = input.Content.Trim(),
            MessageType = input.MessageType,
            SentAt = DateTime.UtcNow,
            IsRead = false
        };

        db.Messages.Add(message);
        conversation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return new MessageDto(
            message.MessageId,
            conversation.ConversationId,
            senderId,
            sender.FullName,
            sender.Roles.Select(r => r.RoleName).FirstOrDefault() ?? "User",
            message.Content,
            message.MessageType,
            message.SentAt
        );
    }
}
