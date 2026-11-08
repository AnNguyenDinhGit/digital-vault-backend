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

    [HttpGet("subscription-plans/statistics")]
    public Task<IReadOnlyList<SubscriptionPlanStatisticsDto>> PlanStatistics(CancellationToken ct) => service.AdminPlanStatistics(ct);

    [HttpGet("subscription-plans/stats")]
    public Task<SubscriptionPlanMonthlyStatsDto> PlanMonthlyStats([FromQuery] string? month = null, CancellationToken ct = default) =>
        service.AdminPlanMonthlyStats(month, ct);

    [HttpGet("subscription-plans/{planId:int}/subscribers")]
    public Task<PlanSubscriberPageDto> PlanSubscribers(
        int planId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken ct = default) =>
        service.AdminPlanSubscribers(planId, page, pageSize, status, ct);

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
    public async Task<IActionResult> DeactivatePlan(int planId, [FromQuery] bool permanent = false, CancellationToken ct = default)
    {
        if (permanent)
        {
            await service.DeleteInactivePlan(UserId, planId, ct);
            return NoContent();
        }
        await service.DeactivatePlan(UserId, planId, ct);
        return NoContent();
    }

    [HttpDelete("subscription-plans/{planId:int}/permanent")]
    public async Task<IActionResult> DeleteInactivePlan(int planId, CancellationToken ct)
    {
        await service.DeleteInactivePlan(UserId, planId, ct);
        return NoContent();
    }

    [HttpDelete("subscription-plans/inactive")]
    public async Task<ActionResult<DeleteInactivePlansResultDto>> DeleteAllInactivePlans(CancellationToken ct) =>
        Ok(await service.DeleteAllInactivePlans(UserId, ct));

    [HttpGet("user-subscriptions")]
    public Task<AdminUserSubscriptionPageDto> UserSubscriptions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] int? planId = null,
        CancellationToken ct = default) =>
        service.AdminUserSubscriptions(page, pageSize, search, status, planId, ct);

    [HttpGet("payment-transactions")]
    public Task<PaymentTransactionPageDto> Transactions([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] int? userId = null, [FromQuery] string? status = null, CancellationToken ct = default) =>
        service.AdminHistory(page, pageSize, userId, status, ct);

    [HttpGet("payment-transactions/{transactionId:long}")]
    public Task<AdminPaymentTransactionDto> Transaction(long transactionId, CancellationToken ct) =>
        service.AdminTransaction(transactionId, ct);
}
