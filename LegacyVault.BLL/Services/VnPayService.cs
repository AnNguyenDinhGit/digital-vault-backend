using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Security;
using LegacyVault.DAL.Repositories;
using Microsoft.AspNetCore.WebUtilities;

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

    public async Task<PaymentCheckoutDto> CreateCheckout(int userId, int planId, string ipAddress, CancellationToken ct)
    {
        ValidateConfiguration();
        var reference = $"{DateTime.UtcNow:yyMMddHHmmss}{Guid.NewGuid():N}"[..32];
        PaymentOrder order;
        try { order = await repository.CreateOrder(userId, planId, reference, ct); }
        catch (KeyNotFoundException ex) { throw new WorkflowException(404, ex.Message); }
        catch (InvalidOperationException ex) { throw new WorkflowException(400, ex.Message); }

        var now = DateTime.UtcNow;
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["vnp_Version"] = options.Version,
            ["vnp_Command"] = options.Command,
            ["vnp_TmnCode"] = options.TmnCode,
            ["vnp_Amount"] = checked((long)(order.Amount * 100m)).ToString(CultureInfo.InvariantCulture),
            ["vnp_CurrCode"] = options.CurrencyCode,
            ["vnp_TxnRef"] = reference,
            ["vnp_OrderInfo"] = $"Payment for {order.PlanName}",
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

    public async Task<PaymentStatusDto> HandleCallback(IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        ValidateConfiguration();
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in query)
            if (item.Key.StartsWith("vnp_", StringComparison.Ordinal) && item.Key is not "vnp_SecureHash" and not "vnp_SecureHashType")
                values[item.Key] = item.Value;

        if (!query.TryGetValue("vnp_SecureHash", out var providedHash) || !FixedEquals(Sign(BuildQuery(values)), providedHash))
            throw new WorkflowException(400, "Invalid VNPay signature.");
        if (!values.TryGetValue("vnp_TxnRef", out var reference) || !values.TryGetValue("vnp_Amount", out var amountText) ||
            !long.TryParse(amountText, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) ||
            !values.TryGetValue("vnp_ResponseCode", out var responseCode) || !values.TryGetValue("vnp_TransactionStatus", out var transactionStatus))
            throw new WorkflowException(400, "Incomplete VNPay callback.");

        var payment = await repository.Transaction(reference, ct);
        if (payment is null) throw new WorkflowException(404, "Payment transaction not found.");
        if (payment.PaymentMethod != "VNPay" || amount != checked((long)(payment.Amount * 100m)))
            throw new WorkflowException(400, "VNPay callback does not match the payment order.");
        var success = responseCode == "00" && transactionStatus == "00";
        try { await repository.ApplyGatewayResult(reference, success, ct); }
        catch (InvalidOperationException ex) { throw new WorkflowException(409, ex.Message); }
        payment = await repository.Transaction(reference, ct) ?? payment;
        return new(reference, payment.Status, payment.Amount);
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(options.TmnCode) || string.IsNullOrWhiteSpace(options.HashSecret) ||
            string.IsNullOrWhiteSpace(options.ReturnUrl) || !Uri.TryCreate(options.PaymentUrl, UriKind.Absolute, out _))
            throw new WorkflowException(503, "VNPay is not configured. Set VnPay:TmnCode, HashSecret, PaymentUrl and ReturnUrl.");
    }

    private string Sign(string data) => Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(options.HashSecret), Encoding.UTF8.GetBytes(data))).ToLowerInvariant();
    private static bool FixedEquals(string expected, string actual)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(actual)); }
        catch (FormatException) { return false; }
    }
    private static string BuildQuery(IEnumerable<KeyValuePair<string, string>> values) =>
        string.Join("&", values.Select(x => $"{WebUtility.UrlEncode(x.Key)}={WebUtility.UrlEncode(x.Value)}"));
    private static string NormalizeIp(string ip) => string.IsNullOrWhiteSpace(ip) ? "127.0.0.1" : ip.Length > 45 ? ip[..45] : ip;
}
