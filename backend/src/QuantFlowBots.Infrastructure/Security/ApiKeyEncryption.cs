using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace QuantFlowBots.Infrastructure.Security;

public interface IApiKeyEncryption
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}

public sealed class AesApiKeyEncryption : IApiKeyEncryption
{
    private readonly byte[] _key;

    public AesApiKeyEncryption(IConfiguration configuration)
    {
        var raw = configuration["Security:EncryptionKey"]
            ?? throw new InvalidOperationException("Security:EncryptionKey is not configured.");
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
    }

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        var nonce = RandomNumberGenerator.GetBytes(12);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using var gcm = new AesGcm(_key, tag.Length);
        gcm.Encrypt(nonce, data, cipher, tag);

        var result = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, result, nonce.Length + tag.Length, cipher.Length);
        return "v2:" + Convert.ToBase64String(result);
    }

    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return string.Empty;
        if (ciphertext.StartsWith("v2:", StringComparison.Ordinal))
        {
            var bytes = Convert.FromBase64String(ciphertext[3..]);
            var nonce = bytes[..12];
            var tag = bytes[12..28];
            var cipher = bytes[28..];
            var plain = new byte[cipher.Length];
            using var gcm = new AesGcm(_key, tag.Length);
            gcm.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }

        return DecryptLegacyCbc(ciphertext);
    }

    private string DecryptLegacyCbc(string ciphertext)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        var bytes = Convert.FromBase64String(ciphertext);
        var iv = new byte[16];
        Buffer.BlockCopy(bytes, 0, iv, 0, 16);
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var plain = decryptor.TransformFinalBlock(bytes, 16, bytes.Length - 16);
        return Encoding.UTF8.GetString(plain);
    }
}
