using System.Collections.Concurrent;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using LegacyVault.BLL.DTOs;
namespace LegacyVault.BLL.Security;

public sealed class MailOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
}
public interface IOtpSender { Task Send(string email, string code, CancellationToken ct); }
public sealed class SmtpOtpSender(MailOptions options) : IOtpSender
{
    public async Task Send(string email, string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Host) || string.IsNullOrWhiteSpace(options.From))
            throw new WorkflowException(503, "OTP email delivery is not configured.");
        using var client = new SmtpClient(options.Host, options.Port) { EnableSsl = true, Credentials = new NetworkCredential(options.Username, options.Password) };
        using var message = new MailMessage(options.From, email, "LegacyVault identity verification", $"Your code is {code}. It expires in 5 minutes. Do not share this code.");
        //await client.SendMailAsync(message, ct);
    }
}
public sealed record OtpChallenge(string ChallengeId, DateTime ExpiresAt);
public sealed class OtpService(IOtpSender sender)
{
    private sealed class State(int user, string session, byte[] hash, DateTime expires)
    {
        public int User { get; } = user;
        public string Session { get; } = session;
        public byte[] Hash { get; } = hash;
        public DateTime Expires { get; } = expires;
        public int Attempts { get; set; }
    }
    private readonly ConcurrentDictionary<string, State> challenges = new();
    private readonly ConcurrentDictionary<int, DateTime> sent = new();
    private readonly ConcurrentDictionary<string, DateTime> verified = new();
    private readonly SemaphoreSlim sendLock = new(1, 1);
    public async Task<OtpChallenge> Request(int user, string session, string email, CancellationToken ct)
    {
        await sendLock.WaitAsync(ct);
        try
        {
            var now = DateTime.UtcNow;
            foreach (var entry in challenges.Where(x => x.Value.Expires <= now).ToArray()) challenges.TryRemove(entry.Key, out _);
            foreach (var entry in verified.Where(x => x.Value <= now).ToArray()) verified.TryRemove(entry.Key, out _);
            foreach (var entry in sent.Where(x => x.Value <= now.AddMinutes(-1)).ToArray()) sent.TryRemove(entry.Key, out _);
            if (sent.ContainsKey(user)) throw new WorkflowException(429, "Wait 60 seconds before requesting another OTP.");
            var code = RandomNumberGenerator.GetInt32(1000000).ToString("D6");
            var id = Guid.NewGuid().ToString("N"); var expires = now.AddMinutes(5);
            await sender.Send(email, code, ct);
            foreach (var entry in challenges.Where(x => x.Value.User == user).ToArray()) challenges.TryRemove(entry.Key, out _);
            challenges[id] = new State(user, session, SHA256.HashData(Encoding.UTF8.GetBytes(id + code)), expires);
            sent[user] = now; verified.TryRemove(session, out _);
            return new OtpChallenge(id, expires);
        }
        finally { sendLock.Release(); }
    }
    public void Verify(int user, string session, string id, string code)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(code) || code.Length != 6 || !challenges.TryGetValue(id, out var state))
            throw new WorkflowException(400, "Invalid or expired OTP.");
        lock (state)
        {
            if (state.User != user || state.Session != session || state.Expires <= DateTime.UtcNow || state.Attempts >= 5)
                throw new WorkflowException(400, "Invalid or expired OTP.");
            state.Attempts++;
            if (!CryptographicOperations.FixedTimeEquals(state.Hash, SHA256.HashData(Encoding.UTF8.GetBytes(id + code))))
                throw new WorkflowException(400, "Invalid or expired OTP.");
            if (!challenges.TryRemove(id, out _)) throw new WorkflowException(400, "OTP already consumed.");
            verified[session] = DateTime.UtcNow.AddMinutes(15);
        }
    }
    public void RequireVerified(string session)
    {
        if (!verified.TryGetValue(session, out var expires) || expires <= DateTime.UtcNow)
            throw new WorkflowException(403, "Verify beneficiary identity using OTP first.");
    }
    public void Revoke(string session)
    {
        verified.TryRemove(session, out _);
        foreach (var entry in challenges.Where(x => x.Value.Session == session).ToArray()) challenges.TryRemove(entry.Key, out _);
    }
}
