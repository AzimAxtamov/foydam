namespace SellerTools.Core.Entities;

/// <summary>
/// Селлер = пользователь Telegram. Id — это telegram user id.
/// </summary>
public class Seller
{
    /// <summary>Telegram user id.</summary>
    public long Id { get; set; }

    /// <summary>Язык интерфейса: "ru" | "uz".</summary>
    public string Language { get; set; } = "ru";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Магазины селлера.</summary>
    public List<Shop> Shops { get; set; } = new();
}
