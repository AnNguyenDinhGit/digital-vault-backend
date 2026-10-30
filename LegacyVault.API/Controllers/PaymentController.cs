using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

namespace LegacyVault.API.Controllers;

[ApiController, Route("api/payments")]
public sealed class PaymentController(VnPayService service, ILogger<PaymentController> logger) : VaultController
{
    [AllowAnonymous, HttpGet("plans")]
    public Task<IReadOnlyList<SubscriptionPlanDto>> Plans(CancellationToken ct) => service.Plans(ct);

    [Authorize, HttpGet("me/subscription")]
    public Task<CurrentSubscriptionDto> CurrentSubscription(CancellationToken ct) => service.CurrentSubscription(UserId, ct);

    [Authorize, HttpGet("me/transactions")]
    public Task<PaymentTransactionPageDto> MyTransactions([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null, CancellationToken ct = default) =>
        service.UserHistory(UserId, page, pageSize, status, ct);

    [Authorize, HttpPost("vnpay/checkout")]
    public Task<PaymentCheckoutDto> Checkout(CreatePaymentInput input, CancellationToken ct) =>
        service.CreateCheckout(UserId, input.PlanId, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1", ct);

    [AllowAnonymous, HttpGet("vnpay/return")]
    public Task<PaymentStatusDto> Return(CancellationToken ct) => service.HandleReturn(ReadGatewayQuery(), ct);

    [AllowAnonymous, HttpGet("vnpay/ipn")]
    public async Task<IActionResult> Ipn(CancellationToken ct)
    {
        try
        {
            var result = await service.HandleIpn(ReadGatewayQuery(), ct);
            return Ack(result.AlreadyProcessed ? "02" : "00", result.AlreadyProcessed ? "Order already confirmed" : "Confirm Success");
        }
        catch (LegacyVault.BLL.DTOs.WorkflowException ex) when (ex.StatusCode == 404)
        {
            return Ack("01", "Order not found");
        }
        catch (LegacyVault.BLL.DTOs.WorkflowException ex) when (ex.StatusCode == 409)
        {
            return Ack("02", "Order already confirmed with a different result");
        }
        catch (LegacyVault.BLL.DTOs.WorkflowException ex)
        {
            var (code, message) = ex.Message switch
            {
                var value when value.Contains("signature", StringComparison.OrdinalIgnoreCase) => ("97", "Invalid signature"),
                var value when value.Contains("amount", StringComparison.OrdinalIgnoreCase) => ("04", "Invalid amount"),
                _ => ("99", "Invalid payment data")
            };
            logger.LogWarning("VNPay IPN rejected with HTTP status {StatusCode} and code {ResponseCode}.", ex.StatusCode, code);
            return Ack(code, message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "VNPay IPN processing failed.");
            return Ack("99", "System error");
        }
    }

    private IActionResult Ack(string code, string message) => Ok(new VnPayIpnAck(code, message));

    private IReadOnlyDictionary<string, string> ReadGatewayQuery() => Request.Query
        .ToDictionary(x => x.Key, x => x.Value.ToString(), StringComparer.Ordinal);
}

public sealed record VnPayIpnAck(
    [property: JsonPropertyName("RspCode")] string RspCode,
    [property: JsonPropertyName("Message")] string Message);
