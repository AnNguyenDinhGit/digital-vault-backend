using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.API.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/admin")]
public sealed class AdminPaymentsController(VnPayService service) : VaultController
{
    [HttpGet("subscription-plans")]
    public Task<IReadOnlyList<AdminSubscriptionPlanDto>> Plans(CancellationToken ct) => service.AdminPlans(ct);

    [HttpPost("subscription-plans")]
    public async Task<ActionResult<AdminSubscriptionPlanDto>> CreatePlan([FromBody] SaveSubscriptionPlanInput input, CancellationToken ct)
    {
        var plan = await service.CreatePlan(UserId, input, ct);
        return Created($"/api/admin/subscription-plans/{plan.PlanId}", plan);
    }

    [HttpPut("subscription-plans/{planId:int}")]
    public Task<AdminSubscriptionPlanDto> UpdatePlan(int planId, [FromBody] SaveSubscriptionPlanInput input, CancellationToken ct) =>
        service.UpdatePlan(UserId, planId, input, ct);

    [HttpDelete("subscription-plans/{planId:int}")]
    public async Task<IActionResult> DeactivatePlan(int planId, CancellationToken ct)
    {
        await service.DeactivatePlan(UserId, planId, ct);
        return NoContent();
    }

    [HttpGet("payment-transactions")]
    public Task<PaymentTransactionPageDto> Transactions([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] int? userId = null, [FromQuery] string? status = null, CancellationToken ct = default) =>
        service.AdminHistory(page, pageSize, userId, status, ct);

    [HttpGet("payment-transactions/{transactionId:long}")]
    public Task<AdminPaymentTransactionDto> Transaction(long transactionId, CancellationToken ct) =>
        service.AdminTransaction(transactionId, ct);
}
