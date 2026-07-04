namespace SellerTools.App.Localization;

/// <summary>
/// Минимальный i18n-задел: словари ru/uz по ключам. Тексты UZ пока частичные,
/// недостающие фолбэком берутся из ru. Структура — с первого дня.
/// </summary>
public static class Loc
{
    public static readonly IReadOnlyList<string> SupportedLanguages = new[] { "ru", "uz" };
    public const string DefaultLanguage = "ru";

    private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        ["ru"] = new()
        {
            // --- Отчёт ---
            ["report.title"] = "Сколько Uzum вам должен",
            ["report.shop"] = "Магазин",
            ["report.date"] = "Дата",
            ["report.col.sku"] = "SKU / товар",
            ["report.col.missing"] = "Потеряно",
            ["report.col.defected"] = "Брак",
            ["report.col.price"] = "Цена",
            ["report.col.bySale"] = "По цене продажи",
            ["report.col.byPurchase"] = "По закупу",
            ["report.total"] = "ИТОГО",
            ["report.totalBySale"] = "Итого по цене продажи",
            ["report.totalByPurchase"] = "Итого по закупу",
            ["report.noLosses"] = "Потерянных и бракованных товаров не найдено — всё в порядке.",
            ["report.purchaseNote"] =
                "* Закуп берётся из ЛК Uzum и у части товаров не заполнен — оценка по закупу неполная.",
            ["report.scanned"] = "Просмотрено SKU",

            // --- Бот: приветствие и язык ---
            ["bot.chooseLanguage"] = "Выберите язык / Tilni tanlang:",
            ["bot.langSet"] = "Язык установлен: русский.",
            ["bot.welcome"] =
                "Привет! Я помогу посчитать, сколько Uzum вам должен за потерянные и " +
                "бракованные товары — по официальному API, ключом «Только чтение».\n\n" +
                "Я работаю только на чтение и физически не могу ничего изменить в вашем магазине.",

            // --- Бот: ввод shopId ---
            ["bot.askShopId"] =
                "Введите ID вашего магазина (shopId) — число из личного кабинета Uzum.",
            ["bot.badShopId"] = "Не похоже на ID магазина. Пришлите число, например: 123456.",

            // --- Бот: ввод ключа ---
            ["bot.askKey"] =
                "Теперь пришлите API-ключ этого магазина (права «Только чтение»).\n\n" +
                "🔒 Сообщение с ключом я удалю сразу же. Ключ хранится только в " +
                "зашифрованном виде и никогда не попадает в логи. Присылайте ключ " +
                "только сюда, в бота — не пересылайте его людям.",
            ["bot.keyReceived"] = "Ключ получил, сообщение удалил. Проверяю доступ к API…",
            ["bot.keyEmpty"] = "Пустое сообщение. Пришлите сам ключ текстом.",

            // --- Бот: результат привязки ---
            ["bot.bindOk"] =
                "✅ Готово! Магазин {0} привязан, ключ проверен и сохранён (зашифрован).\n" +
                "Команда /report — построить отчёт «Сколько Uzum вам должен».",
            ["bot.bindOkNoReport"] =
                "✅ Магазин {0} привязан, ключ сохранён (зашифрован).",

            // --- Бот: дружелюбные ошибки ---
            ["bot.err.unauthorized"] =
                "🔑 Ключ не подошёл (доступ запрещён). Проверьте, что ключ скопирован " +
                "полностью и создан именно для этого магазина с правом «Только чтение», " +
                "и пришлите ключ ещё раз.",
            ["bot.err.notFound"] =
                "🔍 Магазин с таким ID не найден по этому ключу. Проверьте shopId и ключ. " +
                "Начать заново — /start.",
            ["bot.err.rateLimited"] =
                "⏳ Uzum временно ограничил частоту запросов. Подождите минуту и " +
                "пришлите ключ ещё раз.",
            ["bot.err.network"] =
                "🌐 Не удалось связаться с API Uzum (сеть или сервис недоступны). " +
                "Это на нашей стороне соединения, а не с вашим ключом. Попробуйте позже " +
                "или пришлите ключ ещё раз.",
            ["bot.err.server"] =
                "⚠️ API Uzum ответило ошибкой сервера. Обычно это временно — " +
                "попробуйте ещё раз через несколько минут.",
            ["bot.err.generic"] =
                "😕 Что-то пошло не так при проверке ключа. Попробуйте ещё раз или " +
                "начните заново — /start.",
            ["bot.err.noShop"] =
                "Сначала привяжите магазин: /start.",
            ["bot.err.notConfigured"] =
                "Бот ещё не настроен администратором (нет токена). Загляните позже.",

            // --- Бот: команды/подсказки ---
            ["bot.report.building"] = "Строю отчёт, это займёт несколько секунд…",
            ["bot.report.noShopYet"] = "У вас пока нет привязанного магазина. Нажмите /start.",
            ["bot.unknown"] = "Не понял команду. Начать — /start.",
        },

        // UZ — частичный перевод; недостающее фолбэком из ru.
        ["uz"] = new()
        {
            ["report.title"] = "Uzum sizga qancha qarzdor",
            ["report.shop"] = "Do'kon",
            ["report.date"] = "Sana",
            ["report.total"] = "JAMI",

            ["bot.chooseLanguage"] = "Tilni tanlang / Выберите язык:",
            ["bot.langSet"] = "Til o'rnatildi: o'zbekcha.",
            ["bot.welcome"] =
                "Salom! Men Uzum sizga yo'qolgan va nuqsonli tovarlar uchun qancha " +
                "qarzdorligini hisoblab beraman — rasmiy API, «Faqat o'qish» kaliti orqali.\n\n" +
                "Men faqat o'qiyman va do'koningizda hech narsani o'zgartira olmayman.",
            ["bot.askShopId"] = "Do'koningiz ID raqamini (shopId) yuboring.",
            ["bot.askKey"] =
                "Endi shu do'konning API kalitini yuboring («Faqat o'qish»).\n\n" +
                "🔒 Kalitli xabarni darhol o'chiraman. Kalit faqat shifrlangan holda " +
                "saqlanadi va hech qachon loglarga tushmaydi.",
            ["bot.keyReceived"] = "Kalit qabul qilindi, xabar o'chirildi. API tekshirilmoqda…",
        },
    };

    public static string T(string key, string lang = DefaultLanguage)
    {
        if (Strings.TryGetValue(lang, out var dict) && dict.TryGetValue(key, out var value))
            return value;
        if (Strings[DefaultLanguage].TryGetValue(key, out var ru))
            return ru;
        return key;
    }
}
