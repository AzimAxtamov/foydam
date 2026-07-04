namespace SellerTools.Core.Entities;

/// <summary>
/// Магазин Uzum. Id = shopId из Uzum (ключ создаётся на конкретный магазин).
/// </summary>
public class Shop
{
    /// <summary>Uzum shopId.</summary>
    public long Id { get; set; }

    /// <summary>Владелец — Telegram user id.</summary>
    public long SellerId { get; set; }
    public Seller? Seller { get; set; }

    /// <summary>Отображаемое имя магазина (если известно).</summary>
    public string? Title { get; set; }

    /// <summary>
    /// API-ключ селлера в зашифрованном виде (AES). Шифрование появится в В3.
    /// В логи и plaintext НИКОГДА не попадает.
    /// </summary>
    public string? ApiKeyEncrypted { get; set; }

    /// <summary>Срок действия ключа (из ЛК: от 1 месяца до бессрочного/null).</summary>
    public DateTimeOffset? KeyExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
