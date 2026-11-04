using System.Security.Claims;
using LegacyVault.BLL.DTOs;
using LegacyVault.DAL.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyVault.API.Filters;

public sealed class RequireActiveSubscriptionFilter(IPaymentRepository? paymentRepo = null) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpMethod = context.HttpContext.Request.Method;
        var path = context.HttpContext.Request.Path.Value ?? "";

        // Only enforce on state-mutating requests
        if (HttpMethods.IsPost(httpMethod) || HttpMethods.IsPut(httpMethod) ||
            HttpMethods.IsDelete(httpMethod) || HttpMethods.IsPatch(httpMethod))
        {
            // Allow life-confirmation even when subscription is expired
            if (!path.EndsWith("/life-confirmation", StringComparison.OrdinalIgnoreCase))
            {
                var repo = paymentRepo ?? context.HttpContext.RequestServices.GetService<IPaymentRepository>();
                if (repo != null)
                {
                    var userIdStr = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (int.TryParse(userIdStr, out var userId))
                    {
                        var isReadOnly = await repo.IsReadOnly(userId, DateTime.UtcNow, context.HttpContext.RequestAborted);
                        if (isReadOnly)
                        {
                            throw new WorkflowException(403, "Gói dịch vụ đã hết hạn. Tài khoản đang ở chế độ chỉ xem.", "SUBSCRIPTION_EXPIRED");
                        }
                    }
                }
            }
        }

        await next();
    }
}
