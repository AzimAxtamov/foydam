namespace SellerTools.App.Configuration;

/// <summary>Настройки Telegram-бота (секция "Bot").</summary>
public class BotOptions
{
    public const string SectionName = "Bot";

    /// <summary>Токен бота. Только через appsettings.Secrets.json или env Bot__Token.</summary>
    public string Token { get; set; } = "";

    /// <summary>
    /// Username бота без @ (для дип-линков share-карточки <c>https://t.me/{Username}?start=ref_*</c>).
    /// Нужен и в фоновой доставке отчёта, где живого GetMe нет. Дефолт — прод-бот.
    /// </summary>
    public string Username { get; set; } = "foydam_bot";
}
