using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace LegacyVault.BLL.DTOs;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AssignBeneficiaryInput
{
    [Required, EmailAddress, StringLength(150)] public string Email { get; init; } = null!;
    [Range(typeof(decimal), "0.01", "100")] public decimal Allocation { get; init; } = 100m;
}
