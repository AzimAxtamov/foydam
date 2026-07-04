namespace SellerTools.App.Configuration;

/// <summary>Настройки шифрования (секция "Crypto").</summary>
public class CryptoOptions
{
    public const string SectionName = "Crypto";

    /// <summary>
    /// AES-256 ключ в base64 (32 байта). Только через appsettings.Secrets.json
    /// или env Crypto__KeyBase64.
    /// </summary>
    public string KeyBase64 { get; set; } = "";
}
