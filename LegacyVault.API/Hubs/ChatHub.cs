using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using LegacyVault.BLL.Services;

namespace LegacyVault.API.Hubs;

[Authorize]
public class ChatHub(ChatService chatService) : Hub
{
    // Tham gia vào phòng chat của một đơn bàn giao cụ thể (có kiểm tra phân quyền)
    public async Task JoinHandoverChat(int requestId, CancellationToken ct)
    {
        // 1. Lấy thông tin định danh (UserId) từ Token của người dùng đang kết nối
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? Context.User?.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
        {
            throw new HubException("Phiên đăng nhập không hợp lệ hoặc thiếu thông tin định danh.");
        }

        // 2. Kiểm tra quyền truy cập thôngผ่าน ChatService xem user có được phân công vào đơn này không
        bool hasAccess = await chatService.UserHasAccessToHandoverAsync(userId, requestId, ct);
        if (!hasAccess)
        {
            throw new HubException("Bạn không có quyền tham gia phòng chat của đơn bàn giao này.");
        }

        // 3. Nếu hợp lệ, cho phép thêm connection vào nhóm SignalR
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