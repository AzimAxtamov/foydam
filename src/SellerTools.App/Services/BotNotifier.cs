using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SellerTools.App.Configuration;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace SellerTools.App.Services;

/// <summary>
/// Отправка сообщений/документов пользователю из фоновых джоб (Hangfire). Свой
/// TelegramBotClient — джоба не зависит от жизненного цикла TelegramBotService.
/// Ошибки отправки логируются, наружу не бросаются.
/// </summary>
public interface IBotNotifier
{
    Task SendTextAsync(long chatId, string text, bool markdown = false, CancellationToken ct = default);
    /// <summary>Текст с инлайн-клавиатурой (напр. кнопка «Поделиться» под отчётом из фона).</summary>
    Task SendTextAsync(
        long chatId, string text, InlineKeyboardMarkup? markup, bool markdown = false, CancellationToken ct = default);
    Task SendDocumentAsync(
        long chatId, string fileName, byte[] bytes, string? caption, CancellationToken ct = default);
    bool Enabled { get; }
}

public class BotNotifier : IBotNotifier
{
    private readonly ITelegramBotClient? _bot;
    private readonly ILogger<BotNotifier> _log;

    public BotNotifier(IOptions<BotOptions> bot, ILogger<BotNotifier> log)
    {
        _log = log;
        var token = bot.Value.Token;
        if (!string.IsNullOrWhiteSpace(token))
            _bot = new TelegramBotClient(token);
    }

    public bool Enabled => _bot is not null;

    public Task SendTextAsync(
        long chatId, string text, bool markdown = false, CancellationToken ct = default)
        => SendTextAsync(chatId, text, markup: null, markdown, ct);

    public async Task SendTextAsync(
        long chatId, string text, InlineKeyboardMarkup? markup, bool markdown = false, CancellationToken ct = default)
    {
        if (_bot is null) return;
        try
        {
            await _bot.SendMessage(chatId, text,
                parseMode: markdown ? ParseMode.Markdown : ParseMode.None,
                replyMarkup: markup, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning("BotNotifier: не удалось отправить текст в чат {ChatId}: {Msg}", chatId, ex.Message);
        }
    }

    public async Task SendDocumentAsync(
        long chatId, string fileName, byte[] bytes, string? caption, CancellationToken ct = default)
    {
        if (_bot is null) return;
        try
        {
            using var ms = new MemoryStream(bytes);
            await _bot.SendDocument(chatId, InputFile.FromStream(ms, fileName),
                caption: caption, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning("BotNotifier: не удалось отправить документ в чат {ChatId}: {Msg}", chatId, ex.Message);
        }
    }
}
