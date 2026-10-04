using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Security;
using LegacyVault.BLL.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LegacyVault.API.Controllers;

public abstract class VaultController : ControllerBase
{
    protected int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new WorkflowException(401, "Authentication required.");
    protected string SessionId => User.FindFirstValue("vault_session") ?? throw new WorkflowException(401, "Login required.");
    protected static async Task<UploadFile> ReadUpload(IFormFile file, CancellationToken ct)
    {
        if (file.Length is <= 0 or > 10 * 1024 * 1024) throw new WorkflowException(400, "File must be between 1 byte and 10 MB.");
        using var buffer = new MemoryStream(); await file.CopyToAsync(buffer, ct);
        return new UploadFile(Path.GetFileName(file.FileName), file.ContentType, buffer.ToArray());
    }
    protected static async Task<byte[]?> ReadSignature(IFormFile? file, CancellationToken ct)
    {
        if (file is null) return null;
        if (file.Length is <= 0 or > 16384) throw new WorkflowException(400, "Invalid detached signature size.");
        using var buffer = new MemoryStream(); await file.CopyToAsync(buffer, ct); return buffer.ToArray();
    }
    protected IActionResult ContentFile(DocumentContent doc)
    {
        Response.Headers["X-Signature-Valid"] = doc.SignatureValid ? "true" : "false";
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(doc.Content, doc.FileType, doc.FileName, enableRangeProcessing: false);
    }
}

public sealed class OwnerUploadForm
{
    [Required] public IFormFile File { get; set; } = null!;
    public IFormFile? Signature { get; set; }
    public IFormFile? DeathCertificate { get; set; }
    [Range(1, int.MaxValue)] public int BeneficiaryId { get; set; }
    [Required] public bool? OwnerAlive { get; set; }
}
public sealed class CompletionForm
{
    [Required] public IFormFile File { get; set; } = null!;
    [Required] public IFormFile Signature { get; set; } = null!;
}
public sealed class LegalForm
{
    [Range(1, int.MaxValue)] public int VerifierId { get; set; }
    [StringLength(2000)] public string? Comment { get; set; }
    [Required] public IFormFile Evidence { get; set; } = null!;
}

[ApiController, Authorize, Route("api/owner")]
public sealed class OwnerController(VaultService service) : VaultController
{
    [HttpGet("vaults")] public Task<IReadOnlyList<VaultDto>> Vaults(CancellationToken ct) => service.OwnerVaults(UserId, ct);
    [HttpGet("vaults/{vaultId:int}")] public Task<VaultDto> Vault(int vaultId, CancellationToken ct) => service.OwnerVault(UserId, vaultId, ct);
    [HttpPost("vaults")]
    [ProducesResponseType(typeof(VaultDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<VaultDto>> CreateVault(CreateVaultInput input, CancellationToken ct)
    {
        var vault = await service.CreateVault(UserId, input, ct);
        return CreatedAtAction(nameof(Vault), new { vaultId = vault.VaultId }, vault);
    }
    [HttpPost("assets")]
    [ProducesResponseType(typeof(AssetDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<AssetDto>> CreateAsset(CreateAssetInput input, CancellationToken ct)
    {
        var asset = await service.CreateAsset(UserId, input, ct);
        return CreatedAtAction(nameof(Asset), new { assetId = asset.AssetId }, asset);
    }
    [HttpGet("assets")] public Task<IReadOnlyList<AssetDto>> Assets(CancellationToken ct) => service.OwnerAssets(UserId, ct);
    [HttpGet("assets/{assetId:int}")] public Task<AssetDto> Asset(int assetId, CancellationToken ct) => service.OwnerAsset(UserId, assetId, ct);
    [HttpGet("beneficiaries")] public Task<IReadOnlyList<BeneficiaryDto>> Beneficiaries([FromQuery] int? assetId, CancellationToken ct) => service.Beneficiaries(UserId, assetId, ct);
    [HttpPost("assets/{assetId:int}/beneficiaries")]
    [ProducesResponseType(typeof(BeneficiaryDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<BeneficiaryDto>> AssignBeneficiary(int assetId, AssignBeneficiaryInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await service.AssignBeneficiary(UserId, assetId, input, ct));
    [HttpPost("assets/{assetId:int}/documents"), RequestSizeLimit(22 * 1024 * 1024)]
    public async Task<ActionResult<DocumentDto>> Upload(int assetId, [FromForm] OwnerUploadForm form, CancellationToken ct) =>
        Ok(await service.UploadOwnerDocument(UserId, assetId, form.BeneficiaryId, form.OwnerAlive!.Value,
            await ReadUpload(form.File, ct), await ReadSignature(form.Signature, ct),
            form.DeathCertificate is null ? null : await ReadUpload(form.DeathCertificate, ct), ct));
}

[ApiController, Authorize, Route("api/executor/handovers")]
public sealed class ExecutorController(VaultService service) : VaultController
{
    [HttpGet] public Task<IReadOnlyList<HandoverDto>> Requests(CancellationToken ct) => service.Requests(UserId, ct);
    [HttpGet("{requestId:int}")] public Task<HandoverDto> Detail(int requestId, CancellationToken ct) => service.Request(UserId, requestId, ct);
    [HttpGet("{requestId:int}/documents/{documentId:int}/content")]
    public async Task<IActionResult> Document(int requestId, int documentId, CancellationToken ct) => ContentFile(await service.RequestDocument(UserId, requestId, documentId, ct));
    [HttpPost("{requestId:int}/complete"), RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> Complete(int requestId, [FromForm] CompletionForm form, CancellationToken ct)
    {
        await service.Complete(UserId, requestId, await ReadUpload(form.File, ct), (await ReadSignature(form.Signature, ct))!, ct); return NoContent();
    }
    [HttpPost("{requestId:int}/legal-verifications"), RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> Legal(int requestId, [FromForm] LegalForm form, CancellationToken ct) =>
        Ok(new { VerificationId = await service.RequestLegal(UserId, requestId, form.VerifierId, form.Comment, await ReadUpload(form.Evidence, ct), ct) });
}

[ApiController, Authorize, Route("api/beneficiary")]
public sealed class BeneficiaryController(VaultService service, IdentityService identity, OtpService otp) : VaultController
{
    [HttpPost("otp/request"), EnableRateLimiting("otp")]
    public async Task<OtpChallenge> RequestOtp(CancellationToken ct) => await otp.Request(UserId, SessionId, await identity.BeneficiaryEmail(UserId, ct), ct);
    [HttpPost("otp/verify"), EnableRateLimiting("otp")]
    public async Task<IActionResult> VerifyOtp(OtpInput input, CancellationToken ct)
    {
        await identity.BeneficiaryEmail(UserId, ct); otp.Verify(UserId, SessionId, input.ChallengeId, input.Code); return Ok(new { Verified = true, ValidForSeconds = 900 });
    }
    [HttpGet("assets")] public Task<IReadOnlyList<InheritanceDto>> Assets(CancellationToken ct) { otp.RequireVerified(SessionId); return service.Inherited(UserId, ct); }
    [HttpGet("assets/{assetId:int}")] public Task<InheritanceDto> Asset(int assetId, CancellationToken ct) { otp.RequireVerified(SessionId); return service.InheritedAsset(UserId, assetId, ct); }
    [HttpGet("assets/{assetId:int}/documents/{documentId:int}/content")]
    public async Task<IActionResult> Document(int assetId, int documentId, CancellationToken ct)
    { otp.RequireVerified(SessionId); return ContentFile(await service.InheritedDocument(UserId, assetId, documentId, ct)); }
}
public sealed record OtpInput([Required, StringLength(32, MinimumLength = 32)] string ChallengeId, [Required, RegularExpression("^[0-9]{6}$")] string Code);
public sealed record LoginInput([Required, EmailAddress] string Email, [Required, StringLength(1024)] string Password);

[ApiController, Route("api/auth")]
public sealed class AuthController(IdentityService identity, OtpService otp) : VaultController
{
    [HttpPost("register"), EnableRateLimiting("login")]
    [ProducesResponseType(typeof(RegistrationDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<RegistrationDto>> Register(RegisterInput input, CancellationToken ct)
    {
        var result = await identity.Register(input, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }
    [HttpPost("login"), EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginInput input, CancellationToken ct)
    {
        var result = await identity.Login(input.Email, input.Password, ct);
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, result.UserId.ToString()), new Claim("vault_session", Guid.NewGuid().ToString("N")) };
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));
        return Ok(result);
    }
    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout() { otp.Revoke(SessionId); await HttpContext.SignOutAsync(); return NoContent(); }
}
