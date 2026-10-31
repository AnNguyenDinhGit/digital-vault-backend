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
    public string Version { get; set; } = "2.1.0";
    public string Command { get; set; } = "pay";
    public string CurrencyCode { get; set; } = "VND";
    public string Locale { get; set; } = "vn";
    public string OrderType { get; set; } = "other";
}

public sealed class VnPayService(IPaymentRepository repository, VnPayOptions options)
{
    public async Task<IReadOnlyList<SubscriptionPlanDto>> Plans(CancellationToken ct) =>
        (await repository.ActivePlans(ct)).Select(x => new SubscriptionPlanDto(x.PlanId, x.PlanName, x.MaxVaults, x.MaxStorageBytes, x.Price, x.DurationDays)).ToArray();

    public async Task<CurrentSubscriptionDto> CurrentSubscription(int userId, CancellationToken ct)
    {
        var current = await repository.CurrentSubscription(userId, DateTime.UtcNow, ct);
        return current is null
            ? new(false, null)
            : new(true, new(current.SubscriptionId, current.Status, current.StartDate, current.EndDate, ToDto(current.Plan)));
    }

    public async Task<PaymentTransactionPageDto> UserHistory(int userId, int page, int pageSize, string? status, CancellationToken ct)
    {
        ValidatePaging(page, pageSize);
        ValidateStatus(status);
        var total = await repository.CountUserTransactions(userId, status, ct);
        var rows = await repository.UserTransactions(userId, status, (page - 1) * pageSize, pageSize, ct);
        return new(page, pageSize, total, rows.Select(ToDto).ToArray());
    }

    public async Task<IReadOnlyList<AdminSubscriptionPlanDto>> AdminPlans(CancellationToken ct) =>
        (await repository.AllPlans(ct)).Select(ToAdminDto).ToArray();

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
        ValidateConfiguration();
        var reference = $"{DateTime.UtcNow:yyMMddHHmmss}{Guid.NewGuid():N}"[..32];
        PaymentOrder order;
        try { order = await repository.CreateOrder(userId, planId, reference, ct); }
        catch (KeyNotFoundException ex) { throw new WorkflowException(404, ex.Message); }
        catch (InvalidOperationException ex) { throw new WorkflowException(400, ex.Message); }

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
            ["vnp_ExpireDate"] = now.AddMinutes(15).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
        };
        var query = BuildQuery(values);
        var signature = Sign(query);
        return new(reference, order.Amount, $"{options.PaymentUrl}?{query}&vnp_SecureHash={signature}");
    }

    public async Task<PaymentStatusDto> HandleReturn(IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        var callback = VerifyCallback(query);
        var payment = await GetMatchingPayment(callback, ct);
        // VNPay specifies that Return URL only verifies and displays the result. IPN owns settlement.
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
        if (input.Price != decimal.Truncate(input.Price))
            throw new WorkflowException(400, "Plan price must be a whole number of VND.");
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

    private static AdminSubscriptionPlanDto ToAdminDto(SubscriptionPlan plan) =>
        new(plan.PlanId, plan.PlanName, plan.MaxVaults, plan.MaxStorageBytes, plan.Price, plan.DurationDays, plan.IsActive, plan.CreatedAt);

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
