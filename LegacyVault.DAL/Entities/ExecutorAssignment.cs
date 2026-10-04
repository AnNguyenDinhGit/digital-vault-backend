using System;
using System.Collections.Generic;

namespace LegacyVault.DAL.Entities;

public partial class ExecutorAssignment
{
    public int ExecutorAssignmentId { get; set; }

    public int VaultId { get; set; }

    public int ExecutorId { get; set; }

    public string Status { get; set; } = null!;

    public DateTime AssignedAt { get; set; }

    public virtual User Executor { get; set; } = null!;

    public virtual DigitalVault Vault { get; set; } = null!;
}
