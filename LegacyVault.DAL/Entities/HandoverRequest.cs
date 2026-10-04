using System;
using System.Collections.Generic;

namespace LegacyVault.DAL.Entities;

public partial class HandoverRequest
{
    public int RequestId { get; set; }

    public int VaultId { get; set; }

    public int ExecutorId { get; set; }

    public string RequestType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime InitiatedAt { get; set; }

    public DateTime? SubmittedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public virtual User Executor { get; set; } = null!;

    public virtual ICollection<HandoverCase> HandoverCases { get; set; } = new List<HandoverCase>();

    public virtual ICollection<HandoverDocument> HandoverDocuments { get; set; } = new List<HandoverDocument>();

    public virtual ICollection<LegalVerification> LegalVerifications { get; set; } = new List<LegalVerification>();

    public virtual DigitalVault Vault { get; set; } = null!;
}
