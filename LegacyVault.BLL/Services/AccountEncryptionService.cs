using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using LegacyVault.BLL.Interfaces;

namespace LegacyVault.BLL.Services
{
    public class AccountEncryptionService : IAccountEncryptionService
    {
        private readonly byte[] _key;

        public AccountEncryptionService(IConfiguration configuration)
        {
            var keyBase64 = configuration["Security:EncryptionKey"];

            if (string.IsNullOrEmpty(keyBase64))
            {
                throw new InvalidOperationException("Chưa cấu hình Security:EncryptionKey.");
            }

            try
            {
                _key = Convert.FromBase64String(keyBase64);
            }
            catch (FormatException)
            {
                // Nếu chuỗi cấu hình bị sai định dạng Base64, tự tạo key 32-byte tạm thời bằng SHA256 từ chuỗi đó để app không bị crash
                using var sha256 = SHA256.Create();
                _key = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyBase64));
            }
        }

        public string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;

            byte[] nonce = new byte[12];
            RandomNumberGenerator.Fill(nonce);

            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] cipherBytes = new byte[plainBytes.Length];
            byte[] tag = new byte[16];

            using var aesGcm = new AesGcm(_key, 16);
            aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

            byte[] result = new byte[nonce.Length + tag.Length + cipherBytes.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);
            Buffer.BlockCopy(cipherBytes, 0, result, nonce.Length + tag.Length, cipherBytes.Length);

            return Convert.ToBase64String(result);
        }

        public string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;

            byte[] fullBytes = Convert.FromBase64String(cipherText);
            if (fullBytes.Length < 28) throw new ArgumentException("Dữ liệu mã hóa không hợp lệ.");

            byte[] nonce = fullBytes[..12];
            byte[] tag = fullBytes[12..28];
            byte[] cipherBytes = fullBytes[28..];

            byte[] plainBytes = new byte[cipherBytes.Length];

            using var aesGcm = new AesGcm(_key, 16);
            aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);

            return Encoding.UTF8.GetString(plainBytes);
        }
    }
}