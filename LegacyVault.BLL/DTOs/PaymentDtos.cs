namespace LegacyVault.BLL.DTOs;

public sealed record CreatePaymentInput(int PlanId);
public sealed record SubscriptionPlanDto(int PlanId, string PlanName, int MaxVaults, long MaxStorageBytes, decimal Price, int DurationDays);
public sealed record CurrentSubscriptionDto(bool HasActiveSubscription, UserSubscriptionDto? Subscription);
public sealed record UserSubscriptionDto(int SubscriptionId, string Status, DateTime StartDate, DateTime EndDate, SubscriptionPlanDto Plan);
public sealed record PaymentTransactionDto(long TransactionId, string? TransactionCode, decimal Amount, string PaymentMethod, string Status, DateTime CreatedAt, int? SubscriptionId, string? PlanName);
public sealed record PaymentTransactionPageDto(int Page, int PageSize, int Total, IReadOnlyList<PaymentTransactionDto> Items);
public sealed record AdminSubscriptionPlanDto(int PlanId, string PlanName, int MaxVaults, long MaxStorageBytes, decimal Price, int DurationDays, bool IsActive, DateTime CreatedAt);
public sealed record AdminPaymentTransactionDto(long TransactionId, int UserId, string? UserEmail, string? TransactionCode, decimal Amount, string PaymentMethod, string Status, DateTime CreatedAt, int? SubscriptionId, int? PlanId, string? PlanName);

public sealed class SaveSubscriptionPlanInput
{
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(100, MinimumLength = 1)]
    public string PlanName { get; init; } = "";
    [System.ComponentModel.DataAnnotations.Range(1, 100000)] public int MaxVaults { get; init; }
    [System.ComponentModel.DataAnnotations.Range(1, long.MaxValue)] public long MaxStorageBytes { get; init; }
    [System.ComponentModel.DataAnnotations.Range(typeof(decimal), "0", "1000000000")] public decimal Price { get; init; }
    [System.ComponentModel.DataAnnotations.Range(1, 3650)] public int DurationDays { get; init; }
    public bool IsActive { get; init; } = true;
}
public sealed record PaymentCheckoutDto(string TransactionCode, decimal Amount, string PaymentUrl);
public sealed record PaymentStatusDto(string TransactionCode, string Status, decimal Amount, string GatewayResponseCode);
public sealed record VnPayIpnResult(PaymentStatusDto Payment, bool AlreadyProcessed);
