namespace LegacyVault.BLL.DTOs;

public sealed record CreatePaymentInput(int PlanId);
public sealed record SubscriptionPlanDto(int PlanId, string PlanName, int MaxVaults, long MaxStorageBytes, decimal Price, int DurationDays);
public sealed record PaymentCheckoutDto(string TransactionCode, decimal Amount, string PaymentUrl);
public sealed record PaymentStatusDto(string TransactionCode, string Status, decimal Amount);
