using System;
using System.Collections.Generic;

namespace LegacyVault.DAL.Entities;

public partial class DigitalSignature
{
    public int SignatureId { get; set; }

    public int VerificationId { get; set; }

    public int SignerId { get; set; }

    public string SignatureData { get; set; } = null!;

    public string? CertificateInfo { get; set; }

    public string Status { get; set; } = null!;

    public DateTime SignedAt { get; set; }

    public virtual User Signer { get; set; } = null!;

    public virtual LegalVerification Verification { get; set; } = null!;
}
