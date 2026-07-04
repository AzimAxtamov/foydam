using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SellerTools.App.Configuration;

namespace SellerTools.App.Services;

/// <summary>
/// AES-256-CBC. Формат выхода: base64( IV(16) ‖ ciphertext ). Для каждого
/// шифрования — случайный IV. Ключ (32 байта) берётся из конфига Crypto:KeyBase64
/// (задаётся только через secrets/env).
/// </summary>
public class AesCryptoService : ICryptoService
{
    private readonly byte[] _key;

    public AesCryptoService(IOptions<CryptoOptions> options)
    {
        var b64 = options.Value.KeyBase64;
        if (string.IsNullOrWhiteSpace(b64))
            throw new InvalidOperationException(
                "Crypto:KeyBase64 не задан. Укажите AES-256 ключ (base64, 32 байта) " +
                "в appsettings.Secrets.json или env Crypto__KeyBase64.");

        try
        {
            _key = Convert.FromBase64String(b64);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Crypto:KeyBase64 — некорректный base64.");
        }

        if (_key.Length != 32)
            throw new InvalidOperationException(
                $"Crypto:KeyBase64 должен быть 32 байта (AES-256), сейчас {_key.Length}.");
    }

    public string Encrypt(string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        var plain = Encoding.UTF8.GetBytes(plaintext);
        using var enc = aes.CreateEncryptor();
        var cipher = enc.TransformFinalBlock(plain, 0, plain.Length);

        // IV ‖ cipher
        var result = new byte[aes.IV.Length + cipher.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipher, 0, result, aes.IV.Length, cipher.Length);
        return Convert.ToBase64String(result);
    }

    public string Decrypt(string ciphertextBase64)
    {
        var all = Convert.FromBase64String(ciphertextBase64);
        using var aes = Aes.Create();
        aes.Key = _key;

        var iv = new byte[16];
        if (all.Length < iv.Length)
            throw new InvalidOperationException("Шифртекст короче IV — данные повреждены.");
        Buffer.BlockCopy(all, 0, iv, 0, iv.Length);
        aes.IV = iv;

        using var dec = aes.CreateDecryptor();
        var plain = dec.TransformFinalBlock(all, iv.Length, all.Length - iv.Length);
        return Encoding.UTF8.GetString(plain);
    }
}
