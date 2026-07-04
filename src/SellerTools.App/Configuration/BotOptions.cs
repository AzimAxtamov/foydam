namespace SellerTools.App.Configuration;

/// <summary>Настройки Telegram-бота (секция "Bot").</summary>
public class BotOptions
{
    public const string SectionName = "Bot";

    /// <summary>Токен бота. Только через appsettings.Secrets.json или env Bot__Token.</summary>
    public string Token { get; set; } = "";
}
