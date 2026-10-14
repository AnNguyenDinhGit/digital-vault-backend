using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json.Serialization;
using LegacyVault.API.Authentication;
using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Security;
using LegacyVault.BLL.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LegacyVault.API.Controllers;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GoogleOtpInput(
    [Required, StringLength(32, MinimumLength = 32)] string ChallengeId,
    [Required, RegularExpression("^[0-9]{6}$")] string Code);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GoogleResendInput([Required, StringLength(32, MinimumLength = 32)] string ChallengeId);

[ApiController, Route("api/auth/google")]
public sealed class GoogleAuthController(GoogleOptions options, GoogleLoginOtp otp, IdentityService identity, PasswordAuthenticationService authentication) : ControllerBase
{
    private string Browser => Request.Cookies[GoogleAuthentication.BrowserCookie] ?? "";

    [HttpGet("start"), EnableRateLimiting("login")]
    public IActionResult Start()
    {
        if (!options.Configured) throw new WorkflowException(503, "Configure Google:ClientId and Google:ClientSecret first.");
        var browser = Guid.NewGuid().ToString("N");
        Response.Cookies.Append(GoogleAuthentication.BrowserCookie, browser, GoogleAuthentication.PendingCookie);
        Response.Cookies.Delete(GoogleAuthentication.ChallengeCookie, GoogleAuthentication.PendingCookie);
        var properties = new AuthenticationProperties { RedirectUri = "/api/auth/google/callback" };
        properties.Items["browser"] = browser;
        return Challenge(properties, GoogleAuthentication.Scheme);
    }

    [HttpGet("callback")]
    public async Task<ContentResult> Callback(CancellationToken ct)
    {
        var challenge = await otp.Current(Browser, Request.Cookies[GoogleAuthentication.ChallengeCookie] ?? "", ct);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return Content($$"""
            <!doctype html><html lang="vi"><meta charset="utf-8"><title>Google OTP</title>
            <h1>Kiểm tra email để lấy OTP</h1>
            <p>Trong cùng trình duyệt, mở Swagger và gọi POST /api/auth/google/otp/verify với body:</p>
            <pre>{"challengeId":"{{challenge.ChallengeId}}","code":"MA_OTP_6_SO"}</pre>
            <p>OTP có hạn 5 phút. Kết quả 200 trả accessToken để Authorize Bearer; nếu 202, hoàn tất /api/auth/2fa/verify trước. API nghiệp vụ không dùng cookie đăng nhập.</p>
            <a href="/swagger">Mở Swagger</a></html>
            """, "text/html; charset=utf-8");
    }
    [HttpGet("challenge"), EnableRateLimiting("otp")]
    public Task<OtpChallenge> ChallengeState(CancellationToken ct) =>
        otp.Current(Browser, Request.Cookies[GoogleAuthentication.ChallengeCookie] ?? "", ct);

    [HttpGet("error")]
    public IActionResult Error() => BadRequest(new { detail = "Google authentication failed or was cancelled. Start again." });

    [HttpPost("otp/verify"), EnableRateLimiting("otp")]
    public async Task<IActionResult> Verify(GoogleOtpInput input, CancellationToken ct)
    {
        var google = await otp.Verify(Browser, input.ChallengeId, input.Code, ct);
        var result = await identity.GoogleLogin(google.Email, google.FullName, ct);
        var response = await authentication.AfterPrimary(result, ct);
        Response.Cookies.Delete(GoogleAuthentication.BrowserCookie, GoogleAuthentication.PendingCookie);
        Response.Cookies.Delete(GoogleAuthentication.ChallengeCookie, GoogleAuthentication.PendingCookie);
        return response is LoginChallengeDto ? Accepted(response) : Ok(response);
    }

    [HttpPost("otp/resend"), EnableRateLimiting("otp")]
    public async Task<OtpChallenge> Resend(GoogleResendInput input, CancellationToken ct)
    {
        var challenge = await otp.Resend(Browser, input.ChallengeId, ct);
        Response.Cookies.Append(GoogleAuthentication.ChallengeCookie, challenge.ChallengeId, GoogleAuthentication.PendingCookie);
        return challenge;
    }
}
