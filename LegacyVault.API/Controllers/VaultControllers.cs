using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Security;
using LegacyVault.BLL.Services;
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
        if (file.Length > 10 * 1024 * 1024) throw new WorkflowException(413, "File exceeds 10 MB.");
        if (file.Length <= 0) throw new WorkflowException(400, "File must not be empty.");
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
public sealed class HandoverUploadForm
{
    [Required] public IFormFile File { get; set; } = null!;
    public IFormFile? Signature { get; set; }
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
    [HttpPost("{requestId:int}/start")]
    public async Task<IActionResult> Start(int requestId, CancellationToken ct) { await service.StartHandover(UserId, requestId, ct); return NoContent(); }
    [HttpPost("{requestId:int}/documents"), RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<DocumentDto> Upload(int requestId, [FromForm] HandoverUploadForm form, CancellationToken ct) =>
        await service.UploadHandoverDocument(UserId, requestId, await ReadUpload(form.File, ct), await ReadSignature(form.Signature, ct), ct);
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
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record LoginInput([Required, EmailAddress, StringLength(150)] string Email, [Required, StringLength(1024)] string Password);

[ApiController, Route("api/auth")]
public sealed class AuthController(
    IdentityService identity,
    OtpService otp,
    PasswordAuthenticationService authentication,
    IAccessTokenIssuer issuer) : VaultController
{
    [HttpPost("register"), EnableRateLimiting("login")]
    [ProducesResponseType(typeof(RegistrationDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<RegistrationDto>> Register(RegisterInput input, CancellationToken ct)
    {
        var result = await identity.Register(input, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("login"), EnableRateLimiting("login")]
    [ProducesResponseType(typeof(TokenDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(LoginChallengeDto), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Login(LoginInput input, CancellationToken ct)
    {
        var result = await authentication.Login(input.Email, input.Password, ct);
        return result is LoginChallengeDto ? Accepted(result) : Ok(result);
    }

    [HttpPost("refresh-token")]
    [ProducesResponseType(typeof(TokenDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<TokenDto>> RefreshToken([FromBody] RefreshTokenInput input, CancellationToken ct)
    {
        var result = await identity.RefreshAccessTokenAsync(input.RefreshToken, issuer, ct);
        return Ok(result);
    }

    [HttpPost("2fa/verify"), EnableRateLimiting("otp")]
    [ProducesResponseType(typeof(TokenDto), StatusCodes.Status200OK)]
    public async Task<TokenDto> VerifyFactor(FactorInput input, CancellationToken ct) =>
        await authentication.Verify(input, ct);

    [HttpPost("2fa/resend"), EnableRateLimiting("otp")]
    public Task<LoginChallengeDto> ResendFactor(FactorResendInput input, CancellationToken ct) =>
        authentication.Resend(input, ct);

    [Authorize, HttpGet("me")]
    public Task<MeDto> Me(CancellationToken ct) =>
        identity.Me(UserId, ct);

    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenInput? input, CancellationToken ct)
    {
        otp.Revoke(SessionId);
        if (input is not null && !string.IsNullOrWhiteSpace(input.RefreshToken))
        {
            await identity.RevokeRefreshTokenAsync(input.RefreshToken, ct);
        }
        return NoContent();
    }
}

