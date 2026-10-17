using LegacyVault.BLL.DTOs;

namespace LegacyVault.BLL.Security;

public sealed record GoogleSignInState(string ChallengeId, DateTime ExpiresAt, bool RequiresOtp);
public sealed record GoogleExistingIdentity(int UserId, string Email);

// Short-lived verified OIDC identity, never a JWT and never accepted by business APIs.
public sealed class GoogleAuthorizationStore(TimeProvider clock)
{
    private sealed record Pending(string Browser, GoogleExistingIdentity Identity, DateTime Expires);
    private readonly Dictionary<string, Pending> pending = new();
    private readonly object gate = new();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public GoogleSignInState Start(string browser, GoogleExistingIdentity identity)
    {
        lock (gate)
        {
            foreach (var key in pending.Where(x => x.Value.Expires <= Now || x.Value.Browser == browser).Select(x => x.Key).ToArray()) pending.Remove(key);
            if (pending.Count >= 10000) throw new WorkflowException(503, "Too many pending Google sign-ins. Retry later.");
            var id = Guid.NewGuid().ToString("N");
            var expires = Now.AddMinutes(5);
            pending[id] = new(browser, identity, expires);
            return new(id, expires, false);
        }
    }
    public GoogleSignInState? Current(string browser, string id)
    {
        lock (gate)
            return Find(browser, id) is { } state ? new(id, state.Expires, false) : null;
    }
    public GoogleExistingIdentity Consume(string browser, string id)
    {
        lock (gate)
        {
            var state = Find(browser, id) ?? throw new WorkflowException(400, "Google authorization expired or belongs to another browser. Start again.");
            pending.Remove(id);
            return state.Identity;
        }
    }
    private Pending? Find(string browser, string id) => !string.IsNullOrEmpty(browser) && !string.IsNullOrEmpty(id) &&
        pending.TryGetValue(id, out var state) && state.Browser == browser && state.Expires > Now ? state : null;
}
