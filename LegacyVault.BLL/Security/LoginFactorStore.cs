using System.Security.Cryptography;
using System.Text;
using LegacyVault.BLL.DTOs;
using LegacyVault.DAL.Entities;

namespace LegacyVault.BLL.Security;

// Temporary authentication challenges are not access tokens. No secrets leave this store.
public sealed class LoginFactorStore(IOtpSender sender, TimeProvider time)
{
    private sealed record Pending(int UserId, int FactorId, string Method, string SecretFingerprint, byte[]? CodeHash, DateTime Expires)
    { public int Attempts { get; set; } }
    private readonly Dictionary<string, Pending> pending = new();
    private readonly Dictionary<int, DateTime> sent = new();
    private readonly Dictionary<int, long> usedTotp = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTime Now => time.GetUtcNow().UtcDateTime;
    public async Task<LoginChallengeDto> Start(User user, CancellationToken ct)
    {
        var enabled = user.TwoFactorAuths.Where(x => x.IsEnabled).ToArray();
        if (enabled.Length != 1 || enabled[0].Method is not ("Email" or "TOTP"))
            throw new WorkflowException(503, "Enabled 2FA configuration is unsupported. Supported methods: Email and TOTP; exactly one enabled factor.");
        var factor = enabled[0];
        if (factor.Method == "TOTP") _ = DecodeSecret(factor.Secret);
        await gate.WaitAsync(ct);
        try
        {
            foreach (var key in pending.Where(x => x.Value.Expires <= Now).Select(x => x.Key).ToArray()) pending.Remove(key);
            foreach (var key in sent.Where(x => x.Value <= Now.AddMinutes(-1)).Select(x => x.Key).ToArray()) sent.Remove(key);
            if (sent.ContainsKey(user.UserId)) throw new WorkflowException(429, "Wait 60 seconds before starting another 2FA challenge.");
            if (pending.Count >= 10000) throw new WorkflowException(503, "Too many pending authentication challenges.");
            var id = Guid.NewGuid().ToString("N");
            var code = RandomNumberGenerator.GetInt32(1000000).ToString("D6");
            if (factor.Method == "Email") await sender.Send(user.Email, code, ct);
            foreach (var key in pending.Where(x => x.Value.UserId == user.UserId).Select(x => x.Key).ToArray()) pending.Remove(key);
            var expires = Now.AddMinutes(5);
            pending[id] = new(user.UserId, factor.TwoFactorId, factor.Method, Fingerprint(factor.Secret), factor.Method == "Email" ? Hash(id, code) : null, expires);
            sent[user.UserId] = Now;
            return new(id, factor.Method, expires);
        }
        finally { gate.Release(); }
    }
    public async Task<int> UserId(string id, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { return Find(id).UserId; }
        finally { gate.Release(); }
    }
    public async Task Verify(User user, string id, string code, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var state = Find(id);
            if (state.UserId != user.UserId || state.Attempts >= 5) throw new WorkflowException(400, "Invalid or expired challenge.");
            state.Attempts++;
            var enabled = user.TwoFactorAuths.Where(x => x.IsEnabled).ToArray();
            var factor = enabled.SingleOrDefault(x => x.TwoFactorId == state.FactorId);
            if (enabled.Length != 1 || factor is null || factor.Method != state.Method || Fingerprint(factor.Secret) != state.SecretFingerprint)
                throw new WorkflowException(409, "2FA configuration changed. Log in again.");
            var valid = false;
            if (state.Method == "Email") valid = CryptographicOperations.FixedTimeEquals(state.CodeHash!, Hash(id, code));
            else
            {
                var step = time.GetUtcNow().ToUnixTimeSeconds() / 30;
                for (var offset = -1; offset <= 1; offset++)
                {
                    var candidate = step + offset;
                    if (usedTotp.GetValueOrDefault(user.UserId, -1) >= candidate) continue;
                    if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Totp(factor.Secret, candidate)), Encoding.ASCII.GetBytes(code)))
                    { usedTotp[user.UserId] = candidate; valid = true; break; }
                }
            }
            if (!valid) throw new WorkflowException(400, "Invalid or expired 2FA code.");
            pending.Remove(id);
        }
        finally { gate.Release(); }
    }
    private Pending Find(string id) => !string.IsNullOrEmpty(id) && pending.TryGetValue(id, out var state) && state.Expires > Now
        ? state : throw new WorkflowException(400, "Invalid or expired challenge.");
    private static byte[] Hash(string id, string code) => SHA256.HashData(Encoding.UTF8.GetBytes(id + code));
    private static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string Totp(string secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(DecodeSecret(secret), counter);
        var offset = hash[^1] & 15;
        var number = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff;
        return (number % 1000000).ToString("D6");
    }
    private static byte[] DecodeSecret(string secret)
    {
        var bytes = new List<byte>(); int value = 0, bits = 0;
        foreach (var c in secret.TrimEnd('=').ToUpperInvariant())
        {
            var index = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(c);
            if (index < 0) throw new WorkflowException(503, "TOTP secret must be Base32.");
            value = (value << 5) | index; bits += 5;
            if (bits >= 8) { bits -= 8; bytes.Add((byte)(value >> bits)); }
        }
        if (bytes.Count < 16) throw new WorkflowException(503, "TOTP secret must contain at least 128 bits.");
        return bytes.ToArray();
    }
}
