using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SellerTools.App.Configuration;
using Telegram.Bot;

namespace SellerTools.App.Services;

/// <summary>Тип админ-алерта — по нему троттлится (не чаще раза в N минут на тип).</summary>
public enum AlertKind
{
    SyncFailed,        // синк магазина не удался
    RateLimitLow,      // остаток дневного лимита < порога
    UnhandledError,    // необработанное исключение
}

public interface IAdminAlertService
{
    /// <summary>Шлёт алерт админу в Telegram. Троттлится по типу. Никогда не бросает.</summary>
    Task AlertAsync(AlertKind kind, string message, CancellationToken ct = default);

    /// <summary>Сырое сообщение админу без троттлинга и без префикса. Никогда не бросает.</summary>
    Task SendRawToAdminAsync(string message, CancellationToken ct = default);
}

/// <summary>
/// Отправляет админ-алерты в Telegram (chat_id из конфига Admin). Троттлинг по типу
/// ошибки — не чаще раза в N минут (по умолчанию 10), чтобы не спамить. Свой
/// TelegramBotClient, чтобы не зависеть от жизненного цикла бота. Ошибки отправки
/// глушатся (алерт не должен ронять то, что его вызвало).
/// </summary>
public class AdminAlertService : IAdminAlertService
{
    private readonly AdminOptions _admin;
    private readonly ILogger<AdminAlertService> _log;
    private readonly ITelegramBotClient? _bot;
    private readonly TimeSpan _throttle;

    // Последняя отправка по каждому типу — для анти-спама.
    private readonly ConcurrentDictionary<AlertKind, DateTimeOffset> _lastSent = new();

    public AdminAlertService(
        IOptions<AdminOptions> admin,
        IOptions<BotOptions> bot,
        ILogger<AdminAlertService> log)
    {
        _admin = admin.Value;
        _log = log;
        _throttle = TimeSpan.FromMinutes(Math.Max(1, _admin.ThrottleMinutes));

        var token = bot.Value.Token;
        if (!string.IsNullOrWhiteSpace(token) && _admin.ChatId != 0)
            _bot = new TelegramBotClient(token);
    }

    public async Task AlertAsync(AlertKind kind, string message, CancellationToken ct = default)
    {
        if (_bot is null)
            return; // алерты выключены (нет токена или chat_id)

        // Троттлинг по типу.
        var now = DateTimeOffset.UtcNow;
        if (_lastSent.TryGetValue(kind, out var last) && now - last < _throttle)
            return;
        _lastSent[kind] = now;

        var text = $"🚨 SellerTools · {Title(kind)}\n\n{message}";
        try
        {
            await _bot.SendMessage(_admin.ChatId, text, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            // Не смогли отправить алерт — только в лог, наружу не бросаем.
            _log.LogWarning("Не удалось отправить админ-алерт ({Kind}): {Msg}", kind, ex.Message);
        }
    }

    public async Task SendRawToAdminAsync(string message, CancellationToken ct = default)
    {
        if (_bot is null)
            return;
        try
        {
            await _bot.SendMessage(_admin.ChatId, message, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning("Не удалось отправить сырое сообщение админу: {Msg}", ex.Message);
        }
    }

    private static string Title(AlertKind kind) => kind switch
    {
        AlertKind.SyncFailed => "Сбой синка магазина",
        AlertKind.RateLimitLow => "Низкий остаток дневного лимита",
        AlertKind.UnhandledError => "Необработанное исключение",
        _ => "Ошибка",
    };
}
