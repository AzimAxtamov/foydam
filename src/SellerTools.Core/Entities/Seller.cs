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

    /// <summary>
    /// Реф-код селлера (base36, 8 символов). Генерируется ЛЕНИВО при первом тапе
    /// «Поделиться» (см. <c>ReferralService</c>). Кладётся в дип-линк share-карточки
    /// <c>?start=ref_{RefCode}</c>. null — код ещё не создавался. Unique.
    /// </summary>
    public string? RefCode { get; set; }

    /// <summary>
    /// Кто привёл этого селлера (first touch). Пишется РОВНО ОДИН РАЗ при первом
    /// приходе по дип-линку <c>ref_*</c>; повторные /start с другим ref НЕ перезаписывают.
    /// Self-ref (свой же код) игнорируется. null — пришёл не по рефералке.
    /// </summary>
    public long? ReferredBySellerId { get; set; }

    /// <summary>Магазины селлера.</summary>
    public List<Shop> Shops { get; set; } = new();
}
