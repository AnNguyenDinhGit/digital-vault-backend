using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.API.Controllers;

[ApiController, Route("api/payments")]
public sealed class PaymentController(VnPayService service) : VaultController
{
    [AllowAnonymous, HttpGet("plans")]
    public Task<IReadOnlyList<SubscriptionPlanDto>> Plans(CancellationToken ct) => service.Plans(ct);

    [Authorize, HttpPost("vnpay/checkout")]
    public Task<PaymentCheckoutDto> Checkout(CreatePaymentInput input, CancellationToken ct) =>
        service.CreateCheckout(UserId, input.PlanId, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1", ct);

    [AllowAnonymous, HttpGet("vnpay/return")]
    public Task<PaymentStatusDto> Return(CancellationToken ct) => service.HandleCallback(ReadGatewayQuery(), ct);

    [AllowAnonymous, HttpGet("vnpay/ipn")]
    public async Task<IActionResult> Ipn(CancellationToken ct)
    {
        try
        {
            var result = await service.HandleCallback(ReadGatewayQuery(), ct);
            return Ok(new { RspCode = "00", Message = "Confirm Success", result.TransactionCode, result.Status });
        }
        catch (LegacyVault.BLL.DTOs.WorkflowException ex) when (ex.StatusCode == 404)
        {
            return Ok(new { RspCode = "01", Message = "Order not found" });
        }
        catch (LegacyVault.BLL.DTOs.WorkflowException)
        {
            return Ok(new { RspCode = "97", Message = "Invalid signature or payment data" });
        }
    }

    private IReadOnlyDictionary<string, string> ReadGatewayQuery() => Request.Query
        .ToDictionary(x => x.Key, x => x.Value.ToString(), StringComparer.Ordinal);
}
