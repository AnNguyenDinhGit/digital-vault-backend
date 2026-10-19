using LegacyVault.API.Hubs;
using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace LegacyVault.API.Controllers;

[ApiController]
[Route("api/handovers/{requestId:int}/messages")]
[Authorize(Roles = "Executor,LegalVerifier")]
public class ChatController(ChatService chatService, IHubContext<ChatHub> hubContext) : VaultController
{
    /// <summary>
    /// Lấy lịch sử tin nhắn của đơn bàn giao (Có phân trang)
    /// </summary>
    [HttpGet]
    public Task<PageDto<MessageDto>> GetMessages(
        int requestId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return chatService.GetMessagesAsync(UserId, requestId, page, pageSize, ct);
    }

    /// <summary>
    /// Gửi tin nhắn mới (Vừa lưu DB vừa broadcast qua SignalR Real-time)
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<MessageDto>> SendMessage(
        int requestId,
        [FromBody] SendMessageInput input,
        CancellationToken ct = default)
    {
        // 1. Lưu vào Database
        var savedMessage = await chatService.SaveMessageAsync(UserId, requestId, input, ct);

        // 2. Broadcast tin nhắn Real-time qua SignalR tới Group của đơn bàn giao này
        var groupName = $"handover_chat_{requestId}";
        await hubContext.Clients.Group(groupName).SendAsync("ReceiveMessage", savedMessage, cancellationToken: ct);

        return Ok(savedMessage);
    }
}