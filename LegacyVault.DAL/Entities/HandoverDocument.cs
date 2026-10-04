using System;
using System.Collections.Generic;

namespace LegacyVault.DAL.Entities;

public partial class HandoverDocument
{
    public int HandoverDocumentId { get; set; }

    public int RequestId { get; set; }

    public string FileName { get; set; } = null!;

    public string FileType { get; set; } = null!;

    public string StoragePath { get; set; } = null!;

    public bool IsEncrypted { get; set; }

    public int UploadedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual HandoverRequest Request { get; set; } = null!;

    public virtual User UploadedByNavigation { get; set; } = null!;
}
