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
public sealed class GoogleAuthController(
    GoogleOptions options,
    GoogleLoginOtp otp,
    GoogleAuthorizationStore authorizations,
    IdentityService identity,
    PasswordAuthenticationService authentication) : ControllerBase
{
    private string Browser => Request.Cookies[GoogleAuthentication.BrowserCookie] ?? "";
    private string ChallengeId => Request.Cookies[GoogleAuthentication.ChallengeCookie] ?? "";

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
    public async Task<IActionResult> Callback(CancellationToken ct)
    {
        var challenge = await ChallengeState(ct);
        if (!string.IsNullOrEmpty(options.FrontendCallbackUrl)) return Redirect(options.FrontendCallbackUrl);
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!challenge.RequiresOtp)
            return Content($$"""
                <!doctype html><html lang="vi"><meta charset="utf-8"><title>Google authorized</title>
                <h1>Google đã xác thực tài khoản</h1>
                <p>Không cần OTP email đăng ký. Trong cùng trình duyệt, POST /api/auth/google/complete:</p>
                <pre>{"challengeId":"{{challenge.ChallengeId}}"}</pre>
                <p>200 trả token; nếu tài khoản đã bật 2FA, hoàn tất challenge 202 trước.</p><a href="/swagger">Mở Swagger</a></html>
                """, "text/html; charset=utf-8");
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
    public async Task<GoogleSignInState> ChallengeState(CancellationToken ct)
    {
        if (authorizations.Current(Browser, ChallengeId) is { } ready) return ready;
        var registration = await otp.Current(Browser, ChallengeId, ct);
        return new(registration.ChallengeId, registration.ExpiresAt, true);
    }

    [HttpPost("complete"), EnableRateLimiting("login")]
    [ProducesResponseType(typeof(TokenDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(LoginChallengeDto), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Complete(GoogleResendInput input, CancellationToken ct)
    {
        var verified = authorizations.Consume(Browser, input.ChallengeId);
        var account = await identity.ExistingGoogleLogin(verified.Email, ct);
        if (account is null || account.UserId != verified.UserId) throw new WorkflowException(409, "Google account changed. Start sign-in again.");
        var response = await authentication.AfterPrimary(account, ct, google: true);
        ClearCookies();
        return response is LoginChallengeDto ? Accepted(response) : Ok(response);
    }

    private void ClearCookies()
    {
        Response.Cookies.Delete(GoogleAuthentication.BrowserCookie, GoogleAuthentication.PendingCookie);
        Response.Cookies.Delete(GoogleAuthentication.ChallengeCookie, GoogleAuthentication.PendingCookie);
    }

    [HttpGet("error")]
    public IActionResult Error() => BadRequest(new { detail = "Google authentication failed or was cancelled. Start again." });

    [HttpPost("otp/verify"), EnableRateLimiting("otp")]
    [ProducesResponseType(typeof(TokenDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(LoginChallengeDto), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Verify(GoogleOtpInput input, CancellationToken ct)
    {
        var google = await otp.Verify(Browser, input.ChallengeId, input.Code, ct);
        var result = await identity.GoogleLogin(google.Email, google.FullName, ct);
        var response = await authentication.AfterPrimary(result, ct, google: true);
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
