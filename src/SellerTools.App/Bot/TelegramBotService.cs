using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SellerTools.App.Configuration;
using SellerTools.App.Localization;
using SellerTools.App.Services;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace SellerTools.App.Bot;

/// <summary>
/// Telegram-бот на long polling (IHostedService + IUpdateHandler).
/// Диалог: /start → выбор языка → shopId → API-ключ (сообщение сразу удаляется,
/// ключ шифруется). Все сбои связи с API — дружелюбными сообщениями.
/// </summary>
public class TelegramBotService : IHostedService, IUpdateHandler
{
    private readonly BotOptions _options;
    private readonly IServiceScopeFactory _scopes;
    private readonly ConversationStore _store;
    private readonly ILogger<TelegramBotService> _log;

    private ITelegramBotClient? _bot;
    private CancellationTokenSource? _cts;

    public TelegramBotService(
        IOptions<BotOptions> options,
        IServiceScopeFactory scopes,
        ConversationStore store,
        ILogger<TelegramBotService> log)
    {
        _options = options.Value;
        _scopes = scopes;
        _store = store;
        _log = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.Token))
        {
            _log.LogError(
                "Bot:Token не задан — бот не запущен. Укажите токен в appsettings.Secrets.json " +
                "или env Bot__Token.");
            return;
        }

        _bot = new TelegramBotClient(_options.Token);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var me = await _bot.GetMe(_cts.Token);
        _log.LogInformation("Бот запущен: @{Username} (id {Id})", me.Username, me.Id);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message, UpdateType.CallbackQuery },
            DropPendingUpdates = true,
        };

        // Long polling в фоне; не блокируем StartAsync.
        _bot.StartReceiving(this, receiverOptions, _cts.Token);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        _log.LogInformation("Бот остановлен.");
        return Task.CompletedTask;
    }

    // --- IUpdateHandler ---

    public async Task HandleUpdateAsync(
        ITelegramBotClient bot, Update update, CancellationToken ct)
    {
        try
        {
            if (update.CallbackQuery is { } cq)
            {
                await OnCallbackAsync(bot, cq, ct);
                return;
            }

            if (update.Message is { Text: { } text } msg)
            {
                await OnMessageAsync(bot, msg, text.Trim(), ct);
            }
        }
        catch (Exception ex)
        {
            // Никогда не роняем поллинг из-за одного апдейта.
            _log.LogError(ex, "Ошибка обработки апдейта {UpdateId}", update.Id);
        }
    }

    public Task HandleErrorAsync(
        ITelegramBotClient bot, Exception exception, HandleErrorSource source, CancellationToken ct)
    {
        // Ошибки самого поллинга (сеть до Telegram) — логируем, поллинг продолжается.
        var msg = exception is ApiRequestException api
            ? $"Telegram API {api.ErrorCode}: {api.Message}"
            : exception.Message;
        _log.LogWarning("Ошибка поллинга ({Source}): {Msg}", source, msg);
        return Task.CompletedTask;
    }

    // --- Логика диалога ---

    private async Task OnMessageAsync(
        ITelegramBotClient bot, Message msg, string text, CancellationToken ct)
    {
        var userId = msg.From!.Id;
        var chatId = msg.Chat.Id;
        var state = _store.Get(userId);

        // /start в любой момент сбрасывает диалог.
        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            await StartDialogAsync(bot, chatId, userId, ct);
            return;
        }

        if (text.StartsWith("/report", StringComparison.OrdinalIgnoreCase))
        {
            await OnReportAsync(bot, chatId, userId, state.Language, ct);
            return;
        }

        switch (state.Step)
        {
            case DialogStep.AwaitingShopId:
                await OnShopIdAsync(bot, chatId, userId, text, state, ct);
                break;

            case DialogStep.AwaitingKey:
                await OnKeyAsync(bot, msg, userId, text, state, ct);
                break;

            default:
                await bot.SendMessage(chatId, Loc.T("bot.unknown", state.Language),
                    cancellationToken: ct);
                break;
        }
    }

    private async Task StartDialogAsync(
        ITelegramBotClient bot, long chatId, long userId, CancellationToken ct)
    {
        var state = _store.Get(userId);
        state.Step = DialogStep.ChoosingLanguage;

        // Инлайн-кнопки выбора языка.
        var kb = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("Русский", "lang:ru"),
                InlineKeyboardButton.WithCallbackData("O'zbekcha", "lang:uz"),
            },
        });

        await bot.SendMessage(chatId, Loc.T("bot.chooseLanguage", state.Language),
            replyMarkup: kb, cancellationToken: ct);
    }

    private async Task OnCallbackAsync(
        ITelegramBotClient bot, CallbackQuery cq, CancellationToken ct)
    {
        var userId = cq.From.Id;
        var chatId = cq.Message?.Chat.Id ?? userId;
        var state = _store.Get(userId);

        if (cq.Data is { } data && data.StartsWith("lang:"))
        {
            var lang = data["lang:".Length..];
            if (!Loc.SupportedLanguages.Contains(lang)) lang = Loc.DefaultLanguage;
            state.Language = lang;

            using (var scope = _scopes.CreateScope())
            {
                var sellers = scope.ServiceProvider.GetRequiredService<SellerService>();
                await sellers.SetLanguageAsync(userId, lang, ct);
            }

            await bot.AnswerCallbackQuery(cq.Id, cancellationToken: ct);
            await bot.SendMessage(chatId, Loc.T("bot.langSet", lang), cancellationToken: ct);
            await bot.SendMessage(chatId, Loc.T("bot.welcome", lang), cancellationToken: ct);

            state.Step = DialogStep.AwaitingShopId;
            await bot.SendMessage(chatId, Loc.T("bot.askShopId", lang), cancellationToken: ct);
        }
    }

    private async Task OnShopIdAsync(
        ITelegramBotClient bot, long chatId, long userId, string text,
        ConversationState state, CancellationToken ct)
    {
        if (!long.TryParse(text, out var shopId) || shopId <= 0)
        {
            await bot.SendMessage(chatId, Loc.T("bot.badShopId", state.Language),
                cancellationToken: ct);
            return;
        }

        state.PendingShopId = shopId;
        state.Step = DialogStep.AwaitingKey;
        await bot.SendMessage(chatId, Loc.T("bot.askKey", state.Language), cancellationToken: ct);
    }

    private async Task OnKeyAsync(
        ITelegramBotClient bot, Message msg, long userId, string text,
        ConversationState state, CancellationToken ct)
    {
        var chatId = msg.Chat.Id;
        var lang = state.Language;

        if (string.IsNullOrWhiteSpace(text))
        {
            await bot.SendMessage(chatId, Loc.T("bot.keyEmpty", lang), cancellationToken: ct);
            return;
        }

        // КРИТИЧНО: немедленно удаляем сообщение с ключом.
        try
        {
            await bot.DeleteMessage(chatId, msg.MessageId, ct);
        }
        catch (Exception ex)
        {
            // Не смогли удалить (нет прав/старое сообщение) — не показываем ключ, логируем факт.
            _log.LogWarning("Не удалось удалить сообщение с ключом: {Msg}", ex.Message);
        }

        await bot.SendMessage(chatId, Loc.T("bot.keyReceived", lang), cancellationToken: ct);

        var apiKey = text;
        var shopId = state.PendingShopId ?? 0;
        if (shopId <= 0)
        {
            state.Step = DialogStep.AwaitingShopId;
            await bot.SendMessage(chatId, Loc.T("bot.askShopId", lang), cancellationToken: ct);
            return;
        }

        // Проверяем ключ и, если ок, — сохраняем зашифрованным.
        using var scope = _scopes.CreateScope();
        var verifier = scope.ServiceProvider.GetRequiredService<KeyVerification>();
        var sellers = scope.ServiceProvider.GetRequiredService<SellerService>();

        var check = await verifier.CheckAsync(shopId, apiKey, ct);

        if (check.Outcome != KeyCheckOutcome.Ok)
        {
            await bot.SendMessage(chatId, FriendlyError(check.Outcome, lang), cancellationToken: ct);
            _log.LogInformation(
                "Проверка ключа для shop {ShopId}: {Outcome} (HTTP {Code})",
                shopId, check.Outcome, check.StatusCode);
            // Остаёмся на шаге ключа — пользователь может прислать снова.
            return;
        }

        await sellers.BindShopAsync(userId, shopId, apiKey, ct: ct);
        _store.Reset(userId);

        await bot.SendMessage(chatId,
            string.Format(Loc.T("bot.bindOk", lang), shopId), cancellationToken: ct);
    }

    private async Task OnReportAsync(
        ITelegramBotClient bot, long chatId, long userId, string lang, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var sellers = scope.ServiceProvider.GetRequiredService<SellerService>();
        var shops = await sellers.GetShopsAsync(userId, ct);

        if (shops.Count == 0)
        {
            await bot.SendMessage(chatId, Loc.T("bot.report.noShopYet", lang), cancellationToken: ct);
            return;
        }

        // Полноценный отчёт красиво в боте — В5. Здесь подтверждаем, что магазин на связи.
        await bot.SendMessage(chatId, Loc.T("bot.report.building", lang), cancellationToken: ct);
        await bot.SendMessage(chatId,
            $"🛠 Магазин {shops[0].Id} подключён. Формирование отчёта в боте появится в следующем обновлении.",
            cancellationToken: ct);
    }

    /// <summary>Маппинг исхода проверки ключа → дружелюбный текст.</summary>
    private static string FriendlyError(KeyCheckOutcome outcome, string lang) => outcome switch
    {
        KeyCheckOutcome.Unauthorized => Loc.T("bot.err.unauthorized", lang),
        KeyCheckOutcome.NotFound => Loc.T("bot.err.notFound", lang),
        KeyCheckOutcome.RateLimited => Loc.T("bot.err.rateLimited", lang),
        KeyCheckOutcome.Network => Loc.T("bot.err.network", lang),
        KeyCheckOutcome.ServerError => Loc.T("bot.err.server", lang),
        _ => Loc.T("bot.err.generic", lang),
    };
}
