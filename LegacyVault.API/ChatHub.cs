using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace LegacyVault.API.Hubs;

[Authorize]
public class ChatHub : Hub
{
    // Tham gia vào phòng chat của một đơn bàn giao cụ thể
    public async Task JoinHandoverChat(int requestId)
    {
        var groupName = GetGroupName(requestId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
    }

    // Rời khỏi phòng chat
    public async Task LeaveHandoverChat(int requestId)
    {
        var groupName = GetGroupName(requestId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }

    private static string GetGroupName(int requestId) => $"handover_chat_{requestId}";
}