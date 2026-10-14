using LegacyVault.BLL.DTOs;
using LegacyVault.BLL.Security;
using LegacyVault.DAL.Repositories;

namespace LegacyVault.BLL.Services;

public sealed class PasswordAuthenticationService(IdentityService identity, IVaultRepository repository, LoginFactorStore factors, IAccessTokenIssuer issuer)
{
    public async Task<object> Login(string email, string password, CancellationToken ct)
    {
        var result = await identity.CheckPassword(email, password, ct);
        return await AfterPrimary(result, ct);
    }
    public async Task<object> AfterPrimary(LoginDto result, CancellationToken ct, bool google = false)
    {
        _ = await identity.Me(result.UserId, ct);
        var user = await repository.User(result.UserId, ct) ?? throw new WorkflowException(401, "Account unavailable.");
        if (google) IdentityService.RequireGoogleAccount(user);
        if (user.TwoFactorAuths.Any(x => x.IsEnabled)) return await factors.Start(user, ct, google);
        return issuer.Issue(await identity.CompleteLogin(result.UserId, google, ct));
    }
    public async Task<TokenDto> Verify(FactorInput input, CancellationToken ct)
    {
        var challenge = await factors.Identity(input.ChallengeId, ct);
        _ = await identity.Me(challenge.UserId, ct);
        var user = await repository.User(challenge.UserId, ct) ?? throw new WorkflowException(401, "Account unavailable.");
        if (challenge.Google) IdentityService.RequireGoogleAccount(user);
        await factors.Verify(user, input.ChallengeId, input.Code, ct);
        return issuer.Issue(await identity.CompleteLogin(user.UserId, challenge.Google, ct));
    }
    public async Task<LoginChallengeDto> Resend(FactorResendInput input, CancellationToken ct)
    {
        var challenge = await factors.Identity(input.ChallengeId, ct);
        _ = await identity.Me(challenge.UserId, ct);
        var user = await repository.User(challenge.UserId, ct) ?? throw new WorkflowException(401, "Account unavailable.");
        if (challenge.Google) IdentityService.RequireGoogleAccount(user);
        return await factors.Resend(user, input.ChallengeId, ct);
    }
}
