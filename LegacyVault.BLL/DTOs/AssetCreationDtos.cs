using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace LegacyVault.BLL.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateVaultInput
{
    [Required, StringLength(100)] public string Name { get; init; } = null!;
    [StringLength(4000)] public string? Description { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateAssetInput
{
    [Range(1, int.MaxValue)] public int VaultId { get; init; }
    [Required, StringLength(100)] public string Name { get; init; } = null!;
    // AssetType is varchar(50) in the scaffold; avoid lossy conversion of Unicode type names.
    [Required, StringLength(50), RegularExpression(@"^[\x20-\x7E]+$")]
    public string Type { get; init; } = null!;
    [StringLength(4000)] public string? Description { get; init; }
}

public sealed record VaultDto(int VaultId, string Name, string? Description, string Status);
