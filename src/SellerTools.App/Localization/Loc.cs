namespace SellerTools.App.Localization;

/// <summary>
/// Минимальный i18n-задел: словари ru/uz по ключам. Тексты UZ можно дополнять
/// позже; структура — с первого дня. В В3+ подключим к боту через язык селлера.
/// </summary>
public static class Loc
{
    public static readonly IReadOnlyList<string> SupportedLanguages = new[] { "ru", "uz" };
    public const string DefaultLanguage = "ru";

    private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
    {
        ["ru"] = new()
        {
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
        },
        // UZ — задел; тексты дополним позже.
        ["uz"] = new()
        {
            ["report.title"] = "Uzum sizga qancha qarzdor",
            ["report.shop"] = "Do'kon",
            ["report.date"] = "Sana",
            ["report.total"] = "JAMI",
        },
    };

    public static string T(string key, string lang = DefaultLanguage)
    {
        if (Strings.TryGetValue(lang, out var dict) && dict.TryGetValue(key, out var value))
            return value;
        // Фолбэк на русский, затем на сам ключ.
        if (Strings[DefaultLanguage].TryGetValue(key, out var ru))
            return ru;
        return key;
    }
}
