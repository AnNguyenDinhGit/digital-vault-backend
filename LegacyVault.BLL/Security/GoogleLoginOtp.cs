using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using LegacyVault.BLL.DTOs;

namespace LegacyVault.BLL.Security;

public sealed record GoogleEmailIdentity(string Email, string FullName);

// Separate from beneficiary OTP: Google verification never grants inheritance access.
public sealed class GoogleLoginOtp(IOtpSender sender, TimeProvider clock)
{
    private sealed record Pending(string Browser, GoogleEmailIdentity Identity, byte[] Hash, DateTime Expires)
    {
        public int Attempts { get; set; }
    }
    private readonly Dictionary<string, Pending> pending = new();
    private readonly Dictionary<string, DateTime> sent = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<OtpChallenge> Request(string browser, GoogleEmailIdentity identity, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            return await RequestLocked(browser, identity, ct);
        }
        finally { gate.Release(); }
    }

    private async Task<OtpChallenge> RequestLocked(string browser, GoogleEmailIdentity identity, CancellationToken ct)
    {
        foreach (var key in pending.Where(x => x.Value.Expires <= Now).Select(x => x.Key).ToArray()) pending.Remove(key);
        foreach (var key in sent.Where(x => x.Value <= Now.AddMinutes(-1)).Select(x => x.Key).ToArray()) sent.Remove(key);
        var email = identity.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(browser) || email.Length > 150 || !new EmailAddressAttribute().IsValid(email))
            throw new WorkflowException(400, "Invalid Google identity.");
        if (sent.ContainsKey(email)) throw new WorkflowException(429, "Wait 60 seconds before requesting another OTP.");
        if (pending.Count >= 10000) throw new WorkflowException(503, "Too many pending sign-ins. Retry later.");
        var id = Guid.NewGuid().ToString("N");
        var code = RandomNumberGenerator.GetInt32(1000000).ToString("D6");
        await sender.Send(email, code, ct);
        foreach (var key in pending.Where(x => x.Value.Browser == browser).Select(x => x.Key).ToArray()) pending.Remove(key);
        var expires = Now.AddMinutes(5);
        pending[id] = new Pending(browser, identity with { Email = email }, Hash(id, code), expires);
        sent[email] = Now;
        return new OtpChallenge(id, expires);
    }

    public async Task<OtpChallenge> Current(string browser, string id, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { var value = Find(browser, id); return new OtpChallenge(id, value.Expires); }
        finally { gate.Release(); }
    }

    public async Task<OtpChallenge> Resend(string browser, string id, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { return await RequestLocked(browser, Find(browser, id).Identity, ct); }
        finally { gate.Release(); }
    }

    public async Task<GoogleEmailIdentity> Verify(string browser, string id, string code, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var value = Find(browser, id);
            if (value.Attempts >= 5) throw new WorkflowException(400, "Invalid or expired OTP.");
            value.Attempts++;
            if (code is null || code.Length != 6 || !CryptographicOperations.FixedTimeEquals(value.Hash, Hash(id, code)))
                throw new WorkflowException(400, "Invalid or expired OTP.");
            pending.Remove(id); // Consume before database work; a failed save requires a fresh Google flow.
            return value.Identity;
        }
        finally { gate.Release(); }
    }

    private Pending Find(string browser, string id)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(browser) || !pending.TryGetValue(id, out var value) ||
            value.Browser != browser || value.Expires <= Now)
            throw new WorkflowException(400, "Google sign-in expired or belongs to another browser. Start again.");
        return value;
    }
    private static byte[] Hash(string id, string code) => SHA256.HashData(Encoding.UTF8.GetBytes(id + code));
}
