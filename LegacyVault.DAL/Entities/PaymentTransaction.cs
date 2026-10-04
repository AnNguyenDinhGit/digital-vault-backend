using System;
using System.Collections.Generic;

namespace LegacyVault.DAL.Entities;

public partial class PaymentTransaction
{
    public long TransactionId { get; set; }

    public int UserId { get; set; }

    public int? SubscriptionId { get; set; }

    public decimal Amount { get; set; }

    public string PaymentMethod { get; set; } = null!;

    public string? TransactionCode { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual UserSubscription? Subscription { get; set; }

    public virtual User User { get; set; } = null!;
}
