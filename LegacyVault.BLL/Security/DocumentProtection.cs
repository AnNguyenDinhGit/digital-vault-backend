using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using LegacyVault.BLL.DTOs;
namespace LegacyVault.BLL.Security;
public sealed class SecurityOptions
{
    public string EncryptionKey { get; set; } = "";
    public Dictionary<int, string> SignerCertificates { get; set; } = new();
    public List<string> TrustedRootCertificates { get; set; } = new();
}
public sealed record ProtectedDocument(UploadFile File, int SignerId, byte[]? Signature);
public sealed class DocumentProtection(SecurityOptions options)
{
    public bool Verify(byte[] content, byte[]? signature, int signer)
    {
        if (signature is null || !options.SignerCertificates.TryGetValue(signer, out var pem)) return false;
        try
        {
            using var cert = X509Certificate2.CreateFromPem(pem);
            if (DateTime.UtcNow < cert.NotBefore.ToUniversalTime() || DateTime.UtcNow > cert.NotAfter.ToUniversalTime()) return false;
            using var rsa = cert.GetRSAPublicKey();
            var usage = cert.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
            if (rsa is null || rsa.KeySize < 2048 || (usage is not null && !usage.KeyUsages.HasFlag(X509KeyUsageFlags.DigitalSignature))) return false;
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(5);
            var roots = new List<X509Certificate2>();
            try
            {
                if (options.TrustedRootCertificates.Count > 0)
                {
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    foreach (var rootPem in options.TrustedRootCertificates)
                    {
                        var root = X509Certificate2.CreateFromPem(rootPem); roots.Add(root);
                        chain.ChainPolicy.CustomTrustStore.Add(root);
                    }
                }
                return rsa is not null && chain.Build(cert) && rsa.VerifyData(content, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            finally { foreach (var root in roots) root.Dispose(); }
        }
        catch (CryptographicException) { return false; }
    }
    private byte[] Key()
    {
        try { var key = Convert.FromBase64String(options.EncryptionKey); if (key.Length == 32) return key; } catch (FormatException) { }
        throw new WorkflowException(503, "Configure Security:EncryptionKey as a base64 32-byte secret.");
    }
    public byte[] Encrypt(ProtectedDocument document)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(document);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16]; var cipher = new byte[plain.Length];
        using var aes = new AesGcm(Key(), 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        return nonce.Concat(tag).Concat(cipher).ToArray();
    }
    public ProtectedDocument Decrypt(byte[] bytes)
    {
        if (bytes.Length < 28) throw new InvalidDataException("Invalid encrypted document.");
        var plain = new byte[bytes.Length - 28];
        using var aes = new AesGcm(Key(), 16);
        aes.Decrypt(bytes.AsSpan(0, 12), bytes.AsSpan(28), bytes.AsSpan(12, 16), plain);
        return JsonSerializer.Deserialize<ProtectedDocument>(plain) ?? throw new InvalidDataException();
    }
}
