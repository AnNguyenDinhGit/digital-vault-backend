using System;
using System.Collections.Generic;

namespace LegacyVault.DAL.Entities;

public partial class LegalVerification
{
    public int VerificationId { get; set; }

    public int RequestId { get; set; }

    public int VerifierId { get; set; }

    public string Status { get; set; } = null!;

    public string? Comment { get; set; }

    public DateTime SubmittedAt { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public virtual ICollection<DigitalSignature> DigitalSignatures { get; set; } = new List<DigitalSignature>();

    public virtual HandoverRequest Request { get; set; } = null!;

    public virtual User Verifier { get; set; } = null!;
}
