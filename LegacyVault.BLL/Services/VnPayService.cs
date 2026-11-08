using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.ComponentModel.DataAnnotations;
using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Security;
using LegacyVault.DAL.Entities;
using LegacyVault.DAL.Repositories;

namespace LegacyVault.BLL.Services;

public sealed class VnPayOptions
{
    public string TmnCode { get; set; } = "";
    public string HashSecret { get; set; } = "";
    public string PaymentUrl { get; set; } = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html";
    public string ReturnUrl { get; set; } = "";
    public string IpnUrl { get; set; } = "";
    public string Version { get; set; } = "2.1.0";
    public string Command { get; set; } = "pay";
    public string CurrencyCode { get; set; } = "VND";
    public string Locale { get; set; } = "vn";
    public string OrderType { get; set; } = "other";
    public string FeResearch { get; set; } = "";
}

public sealed class VnPayService(IPaymentRepository repository, VnPayOptions options)
{
    public async Task<IReadOnlyList<SubscriptionPlanDto>> Plans(CancellationToken ct) =>
        (await repository.ActivePlans(ct)).Select(x => new SubscriptionPlanDto(x.PlanId, x.PlanName, x.MaxVaults, x.MaxStorageBytes, x.Price, x.DurationDays)).ToArray();

    public async Task<CurrentSubscriptionDto> CurrentSubscription(int userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var current = await repository.CurrentSubscription(userId, now, ct);
        var scheduled = await repository.ScheduledSubscription(userId, ct);
        var isReadOnly = await repository.IsReadOnly(userId, now, ct);

        UserSubscriptionDto? subDto = current is null ? null : ToUserSubDto(current);
        UserSubscriptionDto? scheduledDto = scheduled is null ? null : ToUserSubDto(scheduled);

        DateTime? upgradeWindowEndsAt = null;
        DateTime? renewalWindowStartsAt = null;
        int? daysLeft = null;

        if (current is not null)
        {
            if (current.Plan.Price > 0)
            {
                upgradeWindowEndsAt = current.StartDate.AddDays(3);
                renewalWindowStartsAt = current.EndDate.AddDays(-5);
                var remaining = (current.EndDate - now).TotalDays;
                daysLeft = Math.Max(0, (int)Math.Ceiling(remaining));
            }
            else
            {
                daysLeft = null;
            }
        }

        return new(
            HasActiveSubscription: current is not null,
            Subscription: subDto,
            ScheduledSubscription: scheduledDto,
            UpgradeWindowEndsAt: upgradeWindowEndsAt,
            RenewalWindowStartsAt: renewalWindowStartsAt,
            DaysLeft: daysLeft,
            IsReadOnly: isReadOnly);
    }

    public async Task<SubscriptionQuoteDto> GetQuote(int userId, int planId, CancellationToken ct)
    {
        var newPlan = await repository.Plan(planId, ct)
            ?? throw new WorkflowException(404, "Subscription plan not found.");

        if (!newPlan.IsActive)
            throw new WorkflowException(404, "Subscription plan is not active.");

        var now = DateTime.UtcNow;
        var currentPaid = await repository.CurrentPaidSubscription(userId, now, ct);
        var scheduled = await repository.ScheduledSubscription(userId, ct);
        var duration = newPlan.DurationDays > 0 ? newPlan.DurationDays : 30;

        if (currentPaid != null)
        {
            var start = currentPaid.StartDate;
            var end = currentPaid.EndDate;
            var upgradeWindowEnd = start.AddDays(3);
            var renewalWindowStart = end.AddDays(-5);

            // Window 1: now <= start + 3 ngày
            if (now <= upgradeWindowEnd)
            {
                if (newPlan.Price > currentPaid.Plan.Price)
                {
                    var amountDue = newPlan.Price - currentPaid.Plan.Price;
                    return new SubscriptionQuoteDto(
                        Allowed: true,
                        ReasonCode: null,
                        ReasonMessage: null,
                        ChangeType: "Upgrade",
                        AmountDue: amountDue,
                        StartsAt: now,
                        EndsAt: now.AddDays(duration));
                }
                else
                {
                    return new SubscriptionQuoteDto(
                        Allowed: false,
                        ReasonCode: "UPGRADE_PRICE_TOO_LOW",
                        ReasonMessage: "Trong 3 ngày đầu chỉ được nâng cấp lên gói có giá cao hơn gói hiện tại.",
                        ChangeType: null,
                        AmountDue: 0m,
                        StartsAt: null,
                        EndsAt: null);
                }
            }

            // Window 2: start + 3 ngày < now < end - 5 ngày
            if (now > upgradeWindowEnd && now < renewalWindowStart)
            {
                return new SubscriptionQuoteDto(
                    Allowed: false,
                    ReasonCode: "PLAN_CHANGE_LOCKED",
                    ReasonMessage: $"Chỉ đổi gói được từ {renewalWindowStart:yyyy-MM-dd HH:mm:ss}.",
                    ChangeType: null,
                    AmountDue: 0m,
                    StartsAt: null,
                    EndsAt: null);
            }

            // Window 3: end - 5 ngày <= now < end
            if (now >= renewalWindowStart && now < end)
            {
                if (scheduled != null)
                {
                    return new SubscriptionQuoteDto(
                        Allowed: false,
                        ReasonCode: "SCHEDULED_PLAN_EXISTS",
                        ReasonMessage: "Bạn đã có một gói đăng ký chờ kích hoạt. Chỉ cho phép tối đa một gói chờ.",
                        ChangeType: null,
                        AmountDue: 0m,
                        StartsAt: null,
                        EndsAt: null);
                }

                var changeType = newPlan.PlanId == currentPaid.PlanId ? "Renew" : "Scheduled";
                return new SubscriptionQuoteDto(
                    Allowed: true,
                    ReasonCode: null,
                    ReasonMessage: null,
                    ChangeType: changeType,
                    AmountDue: newPlan.Price,
                    StartsAt: end,
                    EndsAt: end.AddDays(duration));
            }
        }

        // Window 4 / Case B: now >= end hoặc chưa có gói trả phí
        if (scheduled != null)
        {
            return new SubscriptionQuoteDto(
                Allowed: false,
                ReasonCode: "SCHEDULED_PLAN_EXISTS",
                ReasonMessage: "Bạn đã có một gói đăng ký chờ kích hoạt. Chỉ cho phép tối đa một gói chờ.",
                ChangeType: null,
                AmountDue: 0m,
                StartsAt: null,
                EndsAt: null);
        }

        var lastSub = await repository.LastSubscription(userId, ct);
        var defaultChangeType = (lastSub != null && lastSub.PlanId == newPlan.PlanId) ? "Renew" : "New";

        return new SubscriptionQuoteDto(
            Allowed: true,
            ReasonCode: null,
            ReasonMessage: null,
            ChangeType: defaultChangeType,
            AmountDue: newPlan.Price,
            StartsAt: now,
            EndsAt: now.AddDays(duration));
    }

    public async Task<PaymentTransactionPageDto> UserHistory(int userId, int page, int pageSize, string? status, CancellationToken ct)
    {
        ValidatePaging(page, pageSize);
        ValidateStatus(status);
        var total = await repository.CountUserTransactions(userId, status, ct);
        var rows = await repository.UserTransactions(userId, status, (page - 1) * pageSize, pageSize, ct);
        return new(page, pageSize, total, rows.Select(ToDto).ToArray());
    }

    public async Task<IReadOnlyList<AdminSubscriptionPlanDto>> AdminPlans(CancellationToken ct)
    {
        var plans = await repository.AllPlans(ct);
        var stats = await repository.PlanUsageStatistics(DateTime.UtcNow, ct);
        return plans.Select(plan =>
        {
            stats.TryGetValue(plan.PlanId, out var row);
            return ToAdminDto(plan, row?.ActiveUsersCount ?? 0, row?.TotalSubscribersCount ?? 0);
        }).ToArray();
    }

    public async Task<IReadOnlyList<SubscriptionPlanStatisticsDto>> AdminPlanStatistics(CancellationToken ct)
    {
        var plans = await repository.AllPlans(ct);
        var stats = await repository.PlanUsageStatistics(DateTime.UtcNow, ct);
        return plans.Select(plan =>
        {
            stats.TryGetValue(plan.PlanId, out var row);
            return new SubscriptionPlanStatisticsDto(
                PlanId: plan.PlanId,
                PlanName: plan.PlanName,
                Price: plan.Price,
                DurationDays: plan.DurationDays,
                IsActive: plan.IsActive,
                ActiveUsersCount: row?.ActiveUsersCount ?? 0,
                ScheduledUsersCount: row?.ScheduledUsersCount ?? 0,
                ExpiredUsersCount: row?.ExpiredUsersCount ?? 0,
                TotalSubscribersCount: row?.TotalSubscribersCount ?? 0,
                TotalRevenue: row?.TotalRevenue ?? 0m);
        }).ToArray();
    }

    public async Task<SubscriptionPlanMonthlyStatsDto> AdminPlanMonthlyStats(string? month, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        DateTime monthStart;
        if (string.IsNullOrWhiteSpace(month))
        {
            monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        }
        else if (DateTime.TryParseExact(month.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            monthStart = new DateTime(parsed.Year, parsed.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        }
        else
        {
            throw new WorkflowException(400, "Invalid month format. Expected yyyy-MM (e.g., 2026-10).");
        }

        var monthEnd = monthStart.AddMonths(1);
        var rows = await repository.PlanMonthlyStatistics(monthStart, monthEnd, now, ct);
        var items = rows.Select(r => new SubscriptionPlanMonthlyStatItemDto(
            PlanId: r.PlanId,
            PlanName: r.PlanName,
            SoldCount: r.SoldCount,
            Revenue: r.Revenue,
            ActiveUsers: r.ActiveUsers)).ToArray();

        return new SubscriptionPlanMonthlyStatsDto(
            Month: monthStart.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            TotalSold: items.Sum(x => x.SoldCount),
            TotalRevenue: items.Sum(x => x.Revenue),
            Items: items);
    }

    public async Task<PlanSubscriberPageDto> AdminPlanSubscribers(
        int planId,
        int page,
        int pageSize,
        string? status,
        CancellationToken ct)
    {
        ValidatePaging(page, pageSize);
        if (planId <= 0) throw new WorkflowException(400, "planId must be positive.");
        if (!await repository.PlanExistsAny(planId, ct))
        {
            throw new WorkflowException(404, "Subscription plan not found.");
        }

        string? normalizedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            normalizedStatus = status.Trim() switch
            {
                var s when s.Equals("Active", StringComparison.OrdinalIgnoreCase) => "Active",
                var s when s.Equals("Scheduled", StringComparison.OrdinalIgnoreCase) => "Scheduled",
                var s when s.Equals("Expired", StringComparison.OrdinalIgnoreCase) => "Expired",
                var s when s.Equals("Replaced", StringComparison.OrdinalIgnoreCase) => "Replaced",
                _ => throw new WorkflowException(400, "Status must be Active, Scheduled, Expired or Replaced.")
            };
        }

        var now = DateTime.UtcNow;
        var (total, rows) = await repository.PlanSubscribers(
            planId,
            normalizedStatus,
            now,
            (page - 1) * pageSize,
            pageSize,
            ct);

        var items = rows.Select(row =>
        {
            var effectiveStatus = row.Status == "Scheduled" && row.StartDate > now
                ? "Scheduled"
                : (row.Status == "Active" || row.Status == "Scheduled") && row.EndDate > now
                    ? "Active"
                    : row.Status == "Replaced"
                        ? "Replaced"
                        : "Expired";

            return new PlanSubscriberItemDto(
                SubscriptionId: row.SubscriptionId,
                UserId: row.UserId,
                FullName: row.User?.FullName ?? "",
                Email: row.User?.Email ?? "",
                Status: effectiveStatus,
                StartDate: DateTime.SpecifyKind(row.StartDate, DateTimeKind.Utc),
                EndDate: DateTime.SpecifyKind(row.EndDate, DateTimeKind.Utc));
        }).ToArray();

        return new PlanSubscriberPageDto(page, pageSize, total, items);
    }

    public async Task<AdminSubscriptionPlanDto> CreatePlan(int adminUserId, SaveSubscriptionPlanInput input, CancellationToken ct)
    {
        ValidatePlan(input);
        var plan = new SubscriptionPlan
        {
            PlanName = input.PlanName.Trim(), MaxVaults = input.MaxVaults, MaxStorageBytes = input.MaxStorageBytes,
            Price = input.Price, DurationDays = input.DurationDays, IsActive = input.IsActive, CreatedAt = DateTime.UtcNow
        };
        await repository.AddPlan(plan, adminUserId, ct);
        return ToAdminDto(plan);
    }

    public async Task<AdminSubscriptionPlanDto> UpdatePlan(int adminUserId, int planId, SaveSubscriptionPlanInput input, CancellationToken ct)
    {
        ValidatePlan(input);
        var plan = await repository.Plan(planId, ct) ?? throw new WorkflowException(404, "Subscription plan not found.");
        plan.PlanName = input.PlanName.Trim(); plan.MaxVaults = input.MaxVaults; plan.MaxStorageBytes = input.MaxStorageBytes;
        plan.Price = input.Price; plan.DurationDays = input.DurationDays; plan.IsActive = input.IsActive;
        repository.AuditPlanChange(adminUserId, "UpdateSubscriptionPlan", planId);
        await repository.Save(ct);
        return ToAdminDto(plan);
    }

    public async Task DeactivatePlan(int adminUserId, int planId, CancellationToken ct)
    {
        var plan = await repository.Plan(planId, ct) ?? throw new WorkflowException(404, "Subscription plan not found.");
        plan.IsActive = false;
        repository.AuditPlanChange(adminUserId, "DeactivateSubscriptionPlan", planId);
        await repository.Save(ct);
    }

    public async Task DeleteInactivePlan(int adminUserId, int planId, CancellationToken ct)
    {
        var plan = await repository.Plan(planId, ct) ?? throw new WorkflowException(404, "Không tìm thấy gói dịch vụ.");
        if (plan.IsActive)
        {
            throw new WorkflowException(
                409,
                "Chỉ được xóa các gói dịch vụ đã ngừng sử dụng (IsActive = false). Vui lòng ngừng kích hoạt gói trước khi xóa.",
                "PLAN_STILL_ACTIVE");
        }

        await repository.DeleteInactivePlan(plan, adminUserId, ct);
    }

    public async Task<DeleteInactivePlansResultDto> DeleteAllInactivePlans(int adminUserId, CancellationToken ct)
    {
        var (deletedIds, skippedIds) = await repository.DeleteAllInactivePlans(adminUserId, DateTime.UtcNow, ct);
        return new DeleteInactivePlansResultDto(
            DeletedCount: deletedIds.Count,
            DeletedPlanIds: deletedIds,
            SkippedPlanIds: skippedIds,
            Message: $"Đã xóa {deletedIds.Count} gói dịch vụ ngừng sử dụng.");
    }

    public async Task<AdminUserSubscriptionPageDto> AdminUserSubscriptions(
        int page,
        int pageSize,
        string? search,
        string? status,
        int? planId,
        CancellationToken ct)
    {
        ValidatePaging(page, pageSize);
        if (planId is <= 0) throw new WorkflowException(400, "planId must be positive.");
        if (search?.Length > 100) throw new WorkflowException(400, "search must be at most 100 characters.");

        string? normalizedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            normalizedStatus = status.Trim() switch
            {
                var s when s.Equals("Active", StringComparison.OrdinalIgnoreCase) => "Active",
                var s when s.Equals("Scheduled", StringComparison.OrdinalIgnoreCase) => "Scheduled",
                var s when s.Equals("Expired", StringComparison.OrdinalIgnoreCase) => "Expired",
                _ => throw new WorkflowException(400, "Status must be Active, Scheduled or Expired.")
            };
        }

        var now = DateTime.UtcNow;
        var (total, rows) = await repository.AdminUserSubscriptions(
            string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            normalizedStatus,
            planId,
            now,
            (page - 1) * pageSize,
            pageSize,
            ct);

        var items = rows.Select(row =>
        {
            var effectiveStatus = row.Status == "Scheduled" && row.StartDate > now
                ? "Scheduled"
                : row.Status != "Expired" && row.EndDate > now
                    ? "Active"
                    : "Expired";

            return new AdminUserSubscriptionDto(
                SubscriptionId: row.SubscriptionId,
                UserId: row.UserId,
                UserEmail: row.User?.Email,
                PlanId: row.PlanId,
                PlanName: row.Plan?.PlanName,
                Price: row.Plan?.Price ?? 0m,
                Status: effectiveStatus,
                StartDate: DateTime.SpecifyKind(row.StartDate, DateTimeKind.Utc),
                EndDate: DateTime.SpecifyKind(row.EndDate, DateTimeKind.Utc));
        }).ToArray();

        return new AdminUserSubscriptionPageDto(page, pageSize, total, items);
    }

    public async Task<PaymentTransactionPageDto> AdminHistory(int page, int pageSize, int? userId, string? status, CancellationToken ct)
    {
        ValidatePaging(page, pageSize);
        ValidateStatus(status);
        var total = await repository.CountTransactions(userId, status, ct);
        var rows = await repository.Transactions(userId, status, (page - 1) * pageSize, pageSize, ct);
        return new(page, pageSize, total, rows.Select(ToDto).ToArray());
    }

    public async Task<AdminPaymentTransactionDto> AdminTransaction(long transactionId, CancellationToken ct)
    {
        var row = await repository.Transaction(transactionId, ct) ?? throw new WorkflowException(404, "Payment transaction not found.");
        return new(row.TransactionId, row.UserId, row.User?.Email, row.TransactionCode, row.Amount, row.PaymentMethod, row.Status,
            row.CreatedAt, row.SubscriptionId, row.Subscription?.PlanId, row.Subscription?.Plan?.PlanName);
    }

    public async Task<PaymentCheckoutDto> CreateCheckout(int userId, int planId, string ipAddress, CancellationToken ct)
    {
        var quote = await GetQuote(userId, planId, ct);
        if (!quote.Allowed)
        {
            throw new WorkflowException(409, quote.ReasonMessage ?? "Plan change is not allowed at this time.", quote.ReasonCode);
        }

        var reference = $"{DateTime.UtcNow:yyMMddHHmmss}{Guid.NewGuid():N}"[..32];
        PaymentOrder order;
        try
        {
            order = await repository.CreateOrder(userId, planId, reference, quote.AmountDue, quote.ChangeType, quote.StartsAt, quote.EndsAt, ct);
        }
        catch (KeyNotFoundException ex) { throw new WorkflowException(404, ex.Message); }
        catch (InvalidOperationException ex) { throw new WorkflowException(400, ex.Message); }

        // 1. XỬ LÝ DÀNH CHO GÓI MIỄN PHÍ (0đ) - Không cần ValidateConfiguration()
        if (order.Amount == 0m)
        {
            // Trực tiếp kích hoạt gói cho User trong DB
            await repository.ApplyGatewayResult(reference, true, ct);

            // Trả về kết quả thành công ngay
            return new(reference, 0m, "");
        }

        // 2. XỬ LÝ CHO GÓI CÓ PHÍ (> 0đ) - Chỉ Validate khi gọi VNPay
        ValidateConfiguration();

        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Version"] = options.Version,
            ["vnp_Command"] = options.Command,
            ["vnp_TmnCode"] = options.TmnCode,
            ["vnp_Amount"] = checked((long)(order.Amount * 100m)).ToString(CultureInfo.InvariantCulture),
            ["vnp_CurrCode"] = options.CurrencyCode,
            ["vnp_TxnRef"] = reference,
            ["vnp_OrderInfo"] = OrderInfo(order.PlanName),
            ["vnp_OrderType"] = options.OrderType,
            ["vnp_Locale"] = options.Locale,
            ["vnp_ReturnUrl"] = options.ReturnUrl,
            ["vnp_IpAddr"] = NormalizeIp(ipAddress),
            ["vnp_CreateDate"] = now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
            ["vnp_ExpireDate"] = now.AddMinutes(15).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
        };
        var query = BuildQuery(values);
        var signature = Sign(query);
        return new(reference, order.Amount, $"{options.PaymentUrl}?{query}&vnp_SecureHash={signature}");
    }

    public async Task<CurrentSubscriptionDto> SelectFreePlan(int userId, int planId, CancellationToken ct)
    {
        // 1. Kiểm tra tồn tại của Plan
        var plan = await repository.Plan(planId, ct)
            ?? throw new WorkflowException(404, "Subscription plan not found.");

        // 2. Validate giá phải bằng 0
        if (plan.Price > 0m)
        {
            throw new WorkflowException(400, "Gói này yêu cầu thanh toán. Vui lòng chuyển sang cổng thanh toán.");
        }

        // 3. Không cho phép hủy gói trả phí đang hiệu lực
        var currentPaid = await repository.CurrentPaidSubscription(userId, DateTime.UtcNow, ct);
        if (currentPaid != null)
        {
            throw new WorkflowException(409, "Không thể chuyển sang gói miễn phí khi đang có gói trả phí hiệu lực.", "CANNOT_CANCEL_PAID_SUBSCRIPTION");
        }

        // 4. Sinh mã Reference ngắn gọn (< 32 ký tự)
        var reference = $"FREE{DateTime.UtcNow:yyMMddHHmmss}{Random.Shared.Next(100, 999)}";

        // 5. Tạo Order
        await repository.CreateOrder(userId, planId, reference, 0m, "Free", DateTime.UtcNow, DateTime.UtcNow.AddYears(100), ct);

        // 6. Kích hoạt trực tiếp kết quả giao dịch thành công cho gói Free
        await repository.ApplyGatewayResult(reference, true, ct);

        // 7. Trả về thông tin gói đăng ký hiện tại
        return await CurrentSubscription(userId, ct);
    }
    public async Task<PaymentStatusDto> HandleReturn(IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        var callback = VerifyCallback(query);
        var payment = await GetMatchingPayment(callback, ct);
        // VNPay quy định Return URL chỉ xác minh và hiển thị kết quả. IPN chịu trách nhiệm cập nhật trạng thái chính.
        return new(callback.Reference, payment.Status, payment.Amount, callback.ResponseCode);
    }

    public async Task<VnPayIpnResult> HandleIpn(IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        var callback = VerifyCallback(query);
        _ = await GetMatchingPayment(callback, ct);
        bool alreadyProcessed;
        try { alreadyProcessed = await repository.ApplyGatewayResult(callback.Reference, callback.Success, ct); }
        catch (InvalidOperationException ex) { throw new WorkflowException(409, ex.Message); }
        var payment = await repository.Transaction(callback.Reference, ct)
            ?? throw new WorkflowException(404, "Payment transaction not found.");
        return new(new(callback.Reference, payment.Status, payment.Amount, callback.ResponseCode), alreadyProcessed);
    }

    private VerifiedCallback VerifyCallback(IReadOnlyDictionary<string, string> query)
    {
        ValidateConfiguration();
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in query)
            if (item.Key.StartsWith("vnp_", StringComparison.Ordinal) && item.Key is not "vnp_SecureHash" and not "vnp_SecureHashType")
                values[item.Key] = item.Value;

        if (!query.TryGetValue("vnp_SecureHash", out var providedHash) || !FixedEquals(Sign(BuildQuery(values)), providedHash))
            throw new WorkflowException(400, "Invalid VNPay signature.");
        if (!values.TryGetValue("vnp_TmnCode", out var tmnCode) || tmnCode != options.TmnCode ||
            !values.TryGetValue("vnp_TxnRef", out var reference) || string.IsNullOrWhiteSpace(reference) ||
            !values.TryGetValue("vnp_Amount", out var amountText) ||
            !long.TryParse(amountText, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount <= 0 ||
            !values.TryGetValue("vnp_ResponseCode", out var responseCode) ||
            responseCode.Length != 2 || responseCode.Any(c => !char.IsAsciiDigit(c)) ||
            !values.TryGetValue("vnp_TransactionStatus", out var transactionStatus) ||
            transactionStatus.Length != 2 || transactionStatus.Any(c => !char.IsAsciiDigit(c)) ||
            !values.TryGetValue("vnp_TransactionNo", out var gatewayTransaction) || gatewayTransaction.Length is < 1 or > 15 || gatewayTransaction.Any(c => !char.IsAsciiDigit(c)))
            throw new WorkflowException(400, "Incomplete or mismatched VNPay callback.");

        return new(reference, amount, responseCode, transactionStatus, responseCode == "00" && transactionStatus == "00");
    }

    private async Task<PaymentTransaction> GetMatchingPayment(VerifiedCallback callback, CancellationToken ct)
    {
        var payment = await repository.Transaction(callback.Reference, ct)
            ?? throw new WorkflowException(404, "Payment transaction not found.");
        if (payment.PaymentMethod != "VNPay" || callback.Amount != checked((long)(payment.Amount * 100m)))
            throw new WorkflowException(400, "VNPay amount does not match the payment order.");
        return payment;
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(options.TmnCode) || options.TmnCode.Length != 8 || options.TmnCode.Any(c => !char.IsAsciiLetterOrDigit(c)) || string.IsNullOrWhiteSpace(options.HashSecret) ||
            !Uri.TryCreate(options.ReturnUrl, UriKind.Absolute, out var returnUri) || returnUri.Scheme != Uri.UriSchemeHttps ||
            options.ReturnUrl.Length > 255 || !Uri.TryCreate(options.PaymentUrl, UriKind.Absolute, out var paymentUri) || paymentUri.Scheme != Uri.UriSchemeHttps)
            throw new WorkflowException(503, "VNPay is not configured. Set VnPay:TmnCode, HashSecret, PaymentUrl and ReturnUrl.");
    }

    private static void ValidatePlan(SaveSubscriptionPlanInput input)
    {
        if (string.IsNullOrWhiteSpace(input.PlanName)) throw new WorkflowException(400, "Plan name is required.");
        if (!Validator.TryValidateObject(input, new ValidationContext(input), new List<ValidationResult>(), true))
            throw new WorkflowException(400, "Invalid subscription plan fields.");

        // Đảm bảo giá trị là số nguyên VND và không âm (cho phép = 0)
        if (input.Price < 0m || input.Price != decimal.Truncate(input.Price))
            throw new WorkflowException(400, "Plan price must be a non-negative whole number of VND.");
    }

    private static void ValidatePaging(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100) throw new WorkflowException(400, "Page must be positive and pageSize must be between 1 and 100.");
    }

    private static void ValidateStatus(string? status)
    {
        if (status is not null && status is not ("Pending" or "Success" or "Failed"))
            throw new WorkflowException(400, "Status must be Pending, Success or Failed.");
    }

    private static SubscriptionPlanDto ToDto(SubscriptionPlan plan) =>
        new(plan.PlanId, plan.PlanName, plan.MaxVaults, plan.MaxStorageBytes, plan.Price, plan.DurationDays);

    private static UserSubscriptionDto ToUserSubDto(UserSubscription sub) =>
        new(sub.SubscriptionId, sub.Status, sub.StartDate, sub.EndDate, ToDto(sub.Plan));

    private static AdminSubscriptionPlanDto ToAdminDto(SubscriptionPlan plan, int activeUsersCount = 0, int totalSubscribersCount = 0) =>
        new(plan.PlanId, plan.PlanName, plan.MaxVaults, plan.MaxStorageBytes, plan.Price, plan.DurationDays, plan.IsActive, plan.CreatedAt, activeUsersCount, totalSubscribersCount);

    private static PaymentTransactionDto ToDto(PaymentTransaction transaction) =>
        new(transaction.TransactionId, transaction.TransactionCode, transaction.Amount, transaction.PaymentMethod, transaction.Status,
            transaction.CreatedAt, transaction.SubscriptionId, transaction.Subscription?.Plan?.PlanName);

    private string Sign(string data) => Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(options.HashSecret), Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    private static bool FixedEquals(string expected, string actual)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(actual)); }
        catch (FormatException) { return false; }
    }
    private static string BuildQuery(IEnumerable<KeyValuePair<string, string>> values) =>
        string.Join("&", values.Select(x => $"{WebUtility.UrlEncode(x.Key)}={WebUtility.UrlEncode(x.Value)}"));
    private static string OrderInfo(string planName)
    {
        var normalized = planName.Normalize(NormalizationForm.FormD);
        var text = new string(normalized.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && (char.IsAsciiLetterOrDigit(c) || char.IsWhiteSpace(c))).ToArray());
        var words = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var orderInfo = string.IsNullOrWhiteSpace(words) ? "Thanh toan goi dich vu" : $"Thanh toan goi {words}";
        return orderInfo.Length <= 255 ? orderInfo : orderInfo[..255];
    }
    private static string NormalizeIp(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address)) return "127.0.0.1";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return "127.0.0.1";
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return "127.0.0.1";
        var value = address.ToString();
        return value.Length <= 45 ? value : "127.0.0.1";
    }

    private sealed record VerifiedCallback(string Reference, long Amount, string ResponseCode, string TransactionStatus, bool Success);
}
