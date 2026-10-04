using System;
using System.Collections.Generic;

namespace LegacyVault.DAL.Entities;

public partial class BeneficiaryReceipt
{
    public int ReceiptId { get; set; }

    public int HandoverId { get; set; }

    public int BeneficiaryId { get; set; }

    public string Status { get; set; } = null!;

    public string? ConfirmationNote { get; set; }

    public DateTime ConfirmedAt { get; set; }

    public virtual User Beneficiary { get; set; } = null!;

    public virtual HandoverCase Handover { get; set; } = null!;
}
