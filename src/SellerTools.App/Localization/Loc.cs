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

            // --- Карточный отчёт в боте ---
            // {0} — сумма по цене продажи (вся строка выводится жирной)
            ["report.card.owed"] = "💰 Uzum вам должен: до {0} сум",
            // {0} — сумма по закупу
            ["report.card.minByPurchase"] = "Из них подтверждено закупом: {0} сум",
            // {0} — магазин, {1} — дата дд.мм.гггг, {2} — кол-во проверенных товаров
            ["report.card.header"] = "🏪 {0} · {1} · проверено {2} товаров",
            // Сегменты строки карточки (нулевые компоненты скрываются в коде).
            ["report.card.missing"] = "❌ потеряно {0}",   // {0} — кол-во потеряно
            ["report.card.defected"] = "⚠️ брак {0}",      // {0} — кол-во брак
            ["report.card.sum"] = "≈ {0} сум",             // {0} — сумма по цене продажи
            // {0} — сумма по закупу для этой позиции
            ["report.card.linePurchase"] = "   закуп: ≈ {0} сум",
            // {0} — сколько ещё позиций скрыто
            ["report.card.more"] = "…и ещё {0} позиций — полный список в Excel",
            // {0} — кол-во проверенных товаров
            ["report.card.clean"] = "✅ Потерь и брака не найдено — проверено {0} товаров, всё чисто",

            // --- Бот: приветствие и язык ---
            ["bot.chooseLanguage"] = "Выберите язык / Tilni tanlang:",
            ["bot.langSet"] = "Язык установлен: русский.",
            ["bot.welcome"] =
                "Привет! Я помогу посчитать, сколько Uzum вам должен за потерянные и " +
                "бракованные товары — по официальному API, ключом «Только чтение».\n\n" +
                "Я работаю только на чтение и физически не могу ничего изменить в вашем магазине.",

            // --- Бот: ввод ключа (shopId узнаём сами) ---
            ["bot.askKey"] =
                "Пришлите API-ключ из личного кабинета Uzum (права «Только чтение»).\n" +
                "Магазин я определю сам по ключу — вводить ID не нужно.\n\n" +
                "🔒 Сообщение с ключом я удалю сразу же. Ключ хранится только в " +
                "зашифрованном виде и никогда не попадает в логи. Присылайте ключ " +
                "только сюда, в бота — не пересылайте его людям.",
            ["bot.keyReceived"] = "Ключ получил, сообщение удалил. Проверяю доступ к API…",
            ["bot.keyEmpty"] = "Пустое сообщение. Пришлите сам ключ текстом.",

            // --- Бот: выбор магазина (если у ключа их несколько) ---
            ["bot.chooseShop"] = "У ключа несколько магазинов. Какой подключить?",
            ["bot.chooseShopHint"] = "Выберите магазин кнопкой выше.",

            // --- Бот: результат привязки ---
            ["bot.bindOk"] =
                "✅ Готово! Магазин {0} привязан, ключ проверен и сохранён (зашифрован).\n" +
                "Нажмите кнопку «📊 Отчёт» внизу — построю отчёт «Сколько Uzum вам должен».",
            ["bot.bindOkNoReport"] =
                "✅ Магазин {0} привязан, ключ сохранён (зашифрован).",

            // --- Бот: меню и отчёт ---
            ["bot.menu.report"] = "📊 Отчёт",
            ["bot.report.building"] = "Собираю свежие данные из Uzum, это займёт несколько секунд…",
            ["bot.report.fromSnapshot"] = "Отчёт по данным на {0}:",
            ["bot.report.syncFailed"] =
                "😕 Не получилось получить данные из Uzum: {0}. Попробуйте позже.",

            // --- Бот: дружелюбные ошибки ---
            ["bot.err.unauthorized"] =
                "🔑 Ключ не подошёл (доступ запрещён). Проверьте, что ключ скопирован " +
                "полностью и создан именно для этого магазина с правом «Только чтение», " +
                "и пришлите ключ ещё раз.",
            ["bot.err.notFound"] =
                "🔍 API не нашло данных по этому ключу. Проверьте ключ и попробуйте снова.",
            ["bot.err.noShopsForKey"] =
                "🤔 По этому ключу не найдено ни одного магазина. Проверьте, что ключ " +
                "создан в личном кабинете нужного магазина, и пришлите его ещё раз.",
            ["bot.err.sessionExpired"] =
                "⌛ Сессия истекла. Начните заново — /start.",
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

            // Карточный отчёт.
            ["report.card.owed"] = "💰 Uzum sizga qarzdor: {0} so'mgacha",
            ["report.card.minByPurchase"] = "Shundan xarid bilan tasdiqlangan: {0} so'm",
            ["report.card.header"] = "🏪 {0} · {1} · {2} ta tovar tekshirildi",
            ["report.card.missing"] = "❌ yo'qolgan {0}",
            ["report.card.defected"] = "⚠️ nuqson {0}",
            ["report.card.sum"] = "≈ {0} so'm",
            ["report.card.linePurchase"] = "   xarid: ≈ {0} so'm",
            ["report.card.more"] = "…va yana {0} ta pozitsiya — to'liq ro'yxat Excelda",
            ["report.card.clean"] = "✅ Yo'qotish va nuqson topilmadi — {0} ta tovar tekshirildi, hammasi toza",

            ["bot.chooseLanguage"] = "Tilni tanlang / Выберите язык:",
            ["bot.langSet"] = "Til o'rnatildi: o'zbekcha.",
            ["bot.welcome"] =
                "Salom! Men Uzum sizga yo'qolgan va nuqsonli tovarlar uchun qancha " +
                "qarzdorligini hisoblab beraman — rasmiy API, «Faqat o'qish» kaliti orqali.\n\n" +
                "Men faqat o'qiyman va do'koningizda hech narsani o'zgartira olmayman.",
            ["bot.askKey"] =
                "API kalitini yuboring («Faqat o'qish»). Do'konni kalit orqali o'zim " +
                "aniqlayman — ID kiritish shart emas.\n\n" +
                "🔒 Kalitli xabarni darhol o'chiraman. Kalit faqat shifrlangan holda " +
                "saqlanadi va hech qachon loglarga tushmaydi.",
            ["bot.keyReceived"] = "Kalit qabul qilindi, xabar o'chirildi. API tekshirilmoqda…",
            ["bot.chooseShop"] = "Kalitda bir nechta do'kon bor. Qaysi birini ulaymiz?",
            ["bot.chooseShopHint"] = "Yuqoridagi tugma orqali do'konni tanlang.",
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
