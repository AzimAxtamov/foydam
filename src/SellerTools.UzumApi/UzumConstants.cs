namespace SellerTools.UzumApi;

/// <summary>
/// Константы протокола Uzum Seller API: значения статусов/типов и лимиты пагинации.
/// Собраны в одном месте, чтобы «магические» строки/числа не расползались по коду.
/// Значения — как их отдаёт/принимает API (менять нельзя без сверки со спекой).
/// </summary>
public static class UzumConstants
{
    /// <summary>Значения invoiceStatus.value накладной поставки (/v1/invoice).</summary>
    public static class InvoiceStatus
    {
        /// <summary>Принято складом — терминальное состояние (участвует в accepted).</summary>
        public const string Accepted = "ACCEPTED";

        /// <summary>Черновик — в accepted НЕ участвует.</summary>
        public const string Created = "CREATED";
    }

    /// <summary>Значения type возврата (/v1/return): SellerReturnDto.type.</summary>
    public static class ReturnType
    {
        public const string Defected = "DEFECTED";
        public const string Return = "RETURN";
        public const string Fbs = "FBS";
    }

    /// <summary>
    /// Максимальный размер страницы для /v1/invoice и /v1/return (хард-лимит спеки).
    /// Не тюнится конфигом — это ограничение API, а не наша настройка.
    /// </summary>
    public const int InvoiceReturnPageSize = 50;
}
