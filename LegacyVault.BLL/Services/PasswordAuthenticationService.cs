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
    public async Task<object> AfterPrimary(LoginDto result, CancellationToken ct)
    {
        _ = await identity.Me(result.UserId, ct);
        var user = await repository.User(result.UserId, ct) ?? throw new WorkflowException(401, "Account unavailable.");
        if (user.TwoFactorAuths.Any(x => x.IsEnabled)) return await factors.Start(user, ct);
        return issuer.Issue(result);
    }
    public async Task<TokenDto> Verify(FactorInput input, CancellationToken ct)
    {
        var id = await factors.UserId(input.ChallengeId, ct);
        var me = await identity.Me(id, ct);
        var user = await repository.User(id, ct) ?? throw new WorkflowException(401, "Account unavailable.");
        await factors.Verify(user, input.ChallengeId, input.Code, ct);
        return issuer.Issue(new(id, me.Roles));
    }
}
