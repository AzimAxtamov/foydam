namespace SellerTools.App.Localization;

/// <summary>
/// i18n: словари ru/uz по ключам. Покрытие обоих языков — полное (100%).
/// UZ — черновой перевод (латиница), вычитать перед публичным запуском (см. CLAUDE.md).
/// Недостающий ключ фолбэком берётся из ru, затем возвращается сам ключ.
/// </summary>
public static class Loc
{
    public static readonly IReadOnlyList<string> SupportedLanguages = new[] { "ru", "uz" };
    public const string DefaultLanguage = "ru";

    private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        // ==================== RU ====================
        ["ru"] = new()
        {
            // --- Отчёт (общее) ---
            ["report.title"] = "Сколько Uzum вам должен",
            ["report.shop"] = "Магазин",
            ["report.date"] = "Дата",
            ["report.col.sku"] = "SKU / товар",
            ["report.col.lost"] = "Потеряно",
            ["report.col.price"] = "Цена",
            ["report.col.bySale"] = "По цене продажи",
            ["report.col.byCogs"] = "По себестоимости",
            ["report.total"] = "ИТОГО",
            ["report.totalBySale"] = "Итого по цене продажи",
            ["report.totalByCogs"] = "Итого по себестоимости",
            ["report.noLosses"] = "Потерянных товаров не найдено — баланс сходится.",
            ["report.cogsNote"] =
                "* Себестоимость не указана — оценка потерь только по цене продажи. " +
                "Ввод себестоимости появится позже.",
            ["report.compensationNote"] =
                "Если по позиции Uzum уже компенсировал потерю — вычтите эту сумму вручную.",
            ["report.scanned"] = "Просмотрено SKU",

            // --- Карточный отчёт в боте ---
            ["report.card.owed"] = "💰 Uzum вам должен: до {0} сум",
            ["report.card.header"] = "🏪 {0} · {1} · проверено {2} товаров",
            ["report.card.lost"] = "❌ потеряно {0}",
            ["report.card.sum"] = "≈ {0} сум",
            ["report.card.lineCogs"] = "   по себестоимости: ≈ {0} сум",
            ["report.card.more"] = "…и ещё {0} позиций — полный список в Excel",
            ["report.card.clean"] = "✅ Потерь не найдено — баланс по {0} товарам сходится",

            // --- Excel: лист потеряшек ---
            ["xlsx.loss.sheet"] = "Потеряшки",
            ["xlsx.loss.title"] = "Сколько Uzum вам должен — {0} · на {1}",
            ["xlsx.col.sku"] = "SKU",
            ["xlsx.col.product"] = "Товар",
            ["xlsx.col.article"] = "Артикул",
            ["xlsx.col.barcode"] = "Штрихкод",
            ["xlsx.col.accepted"] = "Принято",
            ["xlsx.col.removed"] = "Вывезено",
            ["xlsx.col.sold"] = "Продано",
            ["xlsx.col.defected"] = "Брак",
            ["xlsx.col.stock"] = "Остаток",
            ["xlsx.col.lost"] = "Потеряно",
            ["xlsx.col.price"] = "Цена продажи",
            ["xlsx.col.sumSale"] = "Сумма по продаже",
            ["xlsx.loss.total"] = "ИТОГО",
            ["xlsx.loss.note"] =
                "Потеряно = принято − вывезено − продано − брак − остаток. " +
                "Если Uzum уже компенсировал потерю по позиции — вычтите её вручную.",

            // --- Excel: лист товары + ИКПУ ---
            ["xlsx.ikpu.sheet"] = "Товары и ИКПУ",
            ["xlsx.ikpu.title"] = "Товары и ИКПУ-коды — {0} · на {1}",
            ["xlsx.col.ikpu"] = "ИКПУ",

            // --- Бот: приветствие и язык ---
            ["bot.chooseLanguage"] = "Выберите язык / Tilni tanlang:",
            ["bot.langSet"] = "Язык установлен: русский.",
            ["bot.welcome"] =
                "Привет! Я помогу посчитать, сколько Uzum вам должен за потерянные и " +
                "бракованные товары — по официальному API, ключом «Только чтение».\n\n" +
                "Я работаю только на чтение и физически не могу ничего изменить в вашем магазине.",

            // --- Бот: ввод ключа ---
            ["bot.askKey"] =
                "Пришлите API-ключ из личного кабинета Uzum (права «Только чтение»).\n" +
                "Магазин я определю сам по ключу — вводить ID не нужно.\n\n" +
                "🔒 Сообщение с ключом я удалю сразу же. Ключ хранится только в " +
                "зашифрованном виде и никогда не попадает в логи. Присылайте ключ " +
                "только сюда, в бота — не пересылайте его людям.",
            ["bot.keyReceived"] = "Ключ получил, сообщение удалил. Проверяю доступ к API…",
            ["bot.keyEmpty"] = "Пустое сообщение. Пришлите сам ключ текстом.",

            // --- Бот: выбор магазина ---
            ["bot.chooseShop"] = "У ключа несколько магазинов. Какой подключить?",
            ["bot.chooseShopHint"] = "Выберите магазин кнопкой выше.",

            // --- Бот: результат привязки ---
            ["bot.bindOk"] =
                "✅ Готово! Магазин {0} привязан, ключ проверен и сохранён (зашифрован).\n" +
                "Кнопки меню внизу: отчёт, выгрузки в Excel и обновление данных.",
            ["bot.bindOkNoReport"] =
                "✅ Магазин {0} привязан, ключ сохранён (зашифрован).",

            // --- Бот: меню ---
            ["bot.menu.report"] = "📊 Отчёт",
            ["bot.menu.lossExcel"] = "📉 Потеряшки в Excel",
            ["bot.menu.ikpuExcel"] = "📦 Товары + ИКПУ",
            ["bot.menu.refresh"] = "🔄 Обновить данные",

            // --- Бот: отчёт ---
            ["bot.report.building"] = "Собираю свежие данные из Uzum, это займёт несколько секунд…",
            ["bot.report.fromSnapshot"] = "Отчёт по данным на {0}:",
            ["bot.report.syncFailed"] =
                "😕 Не получилось получить данные из Uzum: {0}. Попробуйте позже.",
            ["bot.report.noShopYet"] = "У вас пока нет привязанного магазина. Нажмите /start.",

            // --- Бот: Excel-выгрузки ---
            ["bot.excel.building"] = "Готовлю файл Excel…",
            ["bot.excel.lossCaption"] =
                "📉 Потеряшки: {0} позиций с потерями/браком. Итоги — в последней строке листа.",
            ["bot.excel.lossEmptyCaption"] =
                "📉 Потерь и брака не найдено — файл содержит только шапку.",
            ["bot.excel.ikpuCaption"] =
                "📦 Все товары магазина с ИКПУ-кодами ({0} SKU). Пригодится для ЭТТН.",
            ["bot.excel.failed"] =
                "😕 Не получилось собрать выгрузку: {0}. Попробуйте позже.",

            // --- Бот: обновление данных (троттлинг) ---
            ["bot.refresh.running"] = "Обновляю данные магазина из Uzum…",
            ["bot.refresh.done"] = "✅ Данные обновлены: {0} SKU на {1}.",
            ["bot.refresh.throttled"] =
                "⏳ Данные от {0}. Обновить можно в {1} — чаще нельзя, чтобы не упереться в лимиты Uzum.",

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

            // --- Бот: прочее ---
            ["bot.unknown"] = "Не понял команду. Начать — /start.",
        },

        // ==================== UZ (черновой перевод) ====================
        ["uz"] = new()
        {
            // --- Hisobot (umumiy) ---
            ["report.title"] = "Uzum sizga qancha qarzdor",
            ["report.shop"] = "Do'kon",
            ["report.date"] = "Sana",
            ["report.col.sku"] = "SKU / tovar",
            ["report.col.lost"] = "Yo'qolgan",
            ["report.col.price"] = "Narx",
            ["report.col.bySale"] = "Sotuv narxi bo'yicha",
            ["report.col.byCogs"] = "Tannarx bo'yicha",
            ["report.total"] = "JAMI",
            ["report.totalBySale"] = "Sotuv narxi bo'yicha jami",
            ["report.totalByCogs"] = "Tannarx bo'yicha jami",
            ["report.noLosses"] = "Yo'qolgan tovarlar topilmadi — balans to'g'ri keladi.",
            ["report.cogsNote"] =
                "* Tannarx ko'rsatilmagan — yo'qotish faqat sotuv narxi bo'yicha baholanadi. " +
                "Tannarx kiritish keyinroq qo'shiladi.",
            ["report.compensationNote"] =
                "Agar Uzum pozitsiya bo'yicha yo'qotishni allaqachon qoplagan bo'lsa — bu summani qo'lda ayiring.",
            ["report.scanned"] = "Tekshirilgan SKU",

            // --- Kartochka hisoboti ---
            ["report.card.owed"] = "💰 Uzum sizga qarzdor: {0} so'mgacha",
            ["report.card.header"] = "🏪 {0} · {1} · {2} ta tovar tekshirildi",
            ["report.card.lost"] = "❌ yo'qolgan {0}",
            ["report.card.sum"] = "≈ {0} so'm",
            ["report.card.lineCogs"] = "   tannarx bo'yicha: ≈ {0} so'm",
            ["report.card.more"] = "…va yana {0} ta pozitsiya — to'liq ro'yxat Excelda",
            ["report.card.clean"] = "✅ Yo'qotish topilmadi — {0} ta tovar bo'yicha balans to'g'ri keladi",

            // --- Excel: yo'qotishlar varag'i ---
            ["xlsx.loss.sheet"] = "Yo'qotishlar",
            ["xlsx.loss.title"] = "Uzum sizga qancha qarzdor — {0} · {1} holatiga",
            ["xlsx.col.sku"] = "SKU",
            ["xlsx.col.product"] = "Tovar",
            ["xlsx.col.article"] = "Artikul",
            ["xlsx.col.barcode"] = "Shtrix-kod",
            ["xlsx.col.accepted"] = "Qabul qilingan",
            ["xlsx.col.removed"] = "Chiqarilgan",
            ["xlsx.col.sold"] = "Sotilgan",
            ["xlsx.col.defected"] = "Nuqson",
            ["xlsx.col.stock"] = "Qoldiq",
            ["xlsx.col.lost"] = "Yo'qolgan",
            ["xlsx.col.price"] = "Sotuv narxi",
            ["xlsx.col.sumSale"] = "Sotuv bo'yicha summa",
            ["xlsx.loss.total"] = "JAMI",
            ["xlsx.loss.note"] =
                "Yo'qolgan = qabul qilingan − chiqarilgan − sotilgan − nuqson − qoldiq. " +
                "Agar Uzum pozitsiya bo'yicha yo'qotishni qoplagan bo'lsa — uni qo'lda ayiring.",

            // --- Excel: tovarlar + IKPU ---
            ["xlsx.ikpu.sheet"] = "Tovarlar va IKPU",
            ["xlsx.ikpu.title"] = "Tovarlar va IKPU kodlari — {0} · {1} holatiga",
            ["xlsx.col.ikpu"] = "IKPU",

            // --- Bot: salomlashuv va til ---
            ["bot.chooseLanguage"] = "Tilni tanlang / Выберите язык:",
            ["bot.langSet"] = "Til o'rnatildi: o'zbekcha.",
            ["bot.welcome"] =
                "Salom! Men Uzum sizga yo'qolgan va nuqsonli tovarlar uchun qancha " +
                "qarzdorligini hisoblab beraman — rasmiy API, «Faqat o'qish» kaliti orqali.\n\n" +
                "Men faqat o'qiyman va do'koningizda hech narsani o'zgartira olmayman.",

            // --- Bot: kalit kiritish ---
            ["bot.askKey"] =
                "Uzum shaxsiy kabinetidan API kalitini yuboring («Faqat o'qish»). " +
                "Do'konni kalit orqali o'zim aniqlayman — ID kiritish shart emas.\n\n" +
                "🔒 Kalitli xabarni darhol o'chiraman. Kalit faqat shifrlangan holda " +
                "saqlanadi va hech qachon loglarga tushmaydi. Kalitni faqat shu botga " +
                "yuboring — odamlarga uzatmang.",
            ["bot.keyReceived"] = "Kalit qabul qilindi, xabar o'chirildi. API tekshirilmoqda…",
            ["bot.keyEmpty"] = "Bo'sh xabar. Kalitni matn ko'rinishida yuboring.",

            // --- Bot: do'kon tanlash ---
            ["bot.chooseShop"] = "Kalitda bir nechta do'kon bor. Qaysi birini ulaymiz?",
            ["bot.chooseShopHint"] = "Yuqoridagi tugma orqali do'konni tanlang.",

            // --- Bot: ulanish natijasi ---
            ["bot.bindOk"] =
                "✅ Tayyor! Do'kon {0} ulandi, kalit tekshirildi va saqlandi (shifrlangan).\n" +
                "Quyidagi menyu tugmalari: hisobot, Excel yuklamalar va ma'lumotni yangilash.",
            ["bot.bindOkNoReport"] =
                "✅ Do'kon {0} ulandi, kalit saqlandi (shifrlangan).",

            // --- Bot: menyu ---
            ["bot.menu.report"] = "📊 Hisobot",
            ["bot.menu.lossExcel"] = "📉 Yo'qotishlar Excelda",
            ["bot.menu.ikpuExcel"] = "📦 Tovarlar + IKPU",
            ["bot.menu.refresh"] = "🔄 Ma'lumotni yangilash",

            // --- Bot: hisobot ---
            ["bot.report.building"] = "Uzumdan yangi ma'lumot yig'yapman, bir necha soniya…",
            ["bot.report.fromSnapshot"] = "{0} holatidagi ma'lumot bo'yicha hisobot:",
            ["bot.report.syncFailed"] =
                "😕 Uzumdan ma'lumot olib bo'lmadi: {0}. Keyinroq urinib ko'ring.",
            ["bot.report.noShopYet"] = "Sizda hali ulangan do'kon yo'q. /start ni bosing.",

            // --- Bot: Excel yuklamalar ---
            ["bot.excel.building"] = "Excel faylini tayyorlayapman…",
            ["bot.excel.lossCaption"] =
                "📉 Yo'qotishlar: {0} ta yo'qotish/nuqsonli pozitsiya. Jami — varaqning oxirgi qatorida.",
            ["bot.excel.lossEmptyCaption"] =
                "📉 Yo'qotish va nuqson topilmadi — faylda faqat sarlavha bor.",
            ["bot.excel.ikpuCaption"] =
                "📦 Do'konning barcha tovarlari IKPU kodlari bilan ({0} ta SKU). ETTN uchun kerak bo'ladi.",
            ["bot.excel.failed"] =
                "😕 Yuklamani tayyorlab bo'lmadi: {0}. Keyinroq urinib ko'ring.",

            // --- Bot: ma'lumotni yangilash (cheklov) ---
            ["bot.refresh.running"] = "Do'kon ma'lumotini Uzumdan yangilayapman…",
            ["bot.refresh.done"] = "✅ Ma'lumot yangilandi: {0} ta SKU, {1} holatiga.",
            ["bot.refresh.throttled"] =
                "⏳ Ma'lumot {0} holatiga. Yangilash {1} da mumkin — Uzum limitiga urilmaslik uchun tez-tez mumkin emas.",

            // --- Bot: xatoliklar ---
            ["bot.err.unauthorized"] =
                "🔑 Kalit to'g'ri kelmadi (ruxsat yo'q). Kalit to'liq nusxalanganini va " +
                "aynan shu do'kon uchun «Faqat o'qish» huquqi bilan yaratilganini tekshiring, " +
                "so'ng kalitni yana yuboring.",
            ["bot.err.notFound"] =
                "🔍 API bu kalit bo'yicha ma'lumot topmadi. Kalitni tekshirib, qaytadan urinib ko'ring.",
            ["bot.err.noShopsForKey"] =
                "🤔 Bu kalit bo'yicha birorta do'kon topilmadi. Kalit kerakli do'kon kabinetida " +
                "yaratilganini tekshiring va uni yana yuboring.",
            ["bot.err.sessionExpired"] =
                "⌛ Sessiya tugadi. Qaytadan boshlang — /start.",
            ["bot.err.rateLimited"] =
                "⏳ Uzum so'rovlar tezligini vaqtincha cheklab qo'ydi. Bir daqiqa kuting va " +
                "kalitni yana yuboring.",
            ["bot.err.network"] =
                "🌐 Uzum API bilan bog'lanib bo'lmadi (tarmoq yoki xizmat mavjud emas). " +
                "Bu ulanish tomonidagi muammo, kalitingizda emas. Keyinroq urinib ko'ring " +
                "yoki kalitni yana yuboring.",
            ["bot.err.server"] =
                "⚠️ Uzum API server xatosi bilan javob berdi. Odatda bu vaqtinchalik — " +
                "bir necha daqiqadan so'ng qayta urinib ko'ring.",
            ["bot.err.generic"] =
                "😕 Kalitni tekshirishda nimadir noto'g'ri ketdi. Qayta urinib ko'ring yoki " +
                "qaytadan boshlang — /start.",
            ["bot.err.noShop"] =
                "Avval do'konni ulang: /start.",
            ["bot.err.notConfigured"] =
                "Bot hali administrator tomonidan sozlanmagan (token yo'q). Keyinroq kiring.",

            // --- Bot: boshqa ---
            ["bot.unknown"] = "Buyruqni tushunmadim. Boshlash — /start.",
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

    /// <summary>Ключи, которых не хватает в языке относительно ru (для самопроверки покрытия).</summary>
    public static IReadOnlyList<string> MissingKeys(string lang)
    {
        if (!Strings.TryGetValue(lang, out var dict)) return Array.Empty<string>();
        return Strings[DefaultLanguage].Keys.Where(k => !dict.ContainsKey(k)).ToList();
    }
}
