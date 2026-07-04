namespace SellerTools.App.Services;

/// <summary>
/// Симметричное шифрование секретов (API-ключей селлеров).
/// Открытый ключ (plaintext) в БД и логи НИКОГДА не попадает.
/// </summary>
public interface ICryptoService
{
    /// <summary>Зашифровать строку → base64 (IV внутри).</summary>
    string Encrypt(string plaintext);

    /// <summary>Расшифровать base64 → исходная строка.</summary>
    string Decrypt(string ciphertextBase64);
}
