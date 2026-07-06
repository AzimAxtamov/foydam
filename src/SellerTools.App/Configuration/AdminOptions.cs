namespace SellerTools.App.Configuration;

/// <summary>Настройки админ-алертов (секция "Admin").</summary>
public class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// Telegram chat_id администратора для алертов. 0/пусто — алерты выключены.
    /// Задавать через env Admin__ChatId.
    /// </summary>
    public long ChatId { get; set; }

    /// <summary>Минимальный интервал между алертами одного типа (минуты). Анти-спам.</summary>
    public int ThrottleMinutes { get; set; } = 10;
}
