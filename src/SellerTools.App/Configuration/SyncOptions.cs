namespace SellerTools.App.Configuration;

/// <summary>
/// Настройки синхронизации «синк → Postgres → отчёт из БД» (секция "Sync").
/// </summary>
public class SyncOptions
{
    public const string SectionName = "Sync";

    /// <summary>
    /// Порог остатка ДНЕВНОГО лимита (абсолютное число, x-ratelimit-remaining-per-day),
    /// ниже которого несрочные синки паузятся + админ-алерт. По умолчанию 5000.
    /// </summary>
    public long RateLimitDailyFloor { get; set; } = 5000;

    /// <summary>Длительность паузы (мс) при низком остатке дневного лимита.</summary>
    public int RateLimitPauseMs { get; set; } = 60_000;

    /// <summary>
    /// Порог свежести карточек SKU (часы): если последний синк карточек старше — перед
    /// отчётом делаем быстрый дельта-синк карточек. По умолчанию 6 ч.
    /// </summary>
    public int CardsFreshnessHours { get; set; } = 6;

    /// <summary>
    /// Порог свежести накладных/возвратов (часы): старше — быстрый дельта-синк перед
    /// отчётом. По умолчанию 6 ч (совпадает с карточками — один порог «данные свежие»).
    /// </summary>
    public int DeltaFreshnessHours { get; set; } = 6;

    /// <summary>
    /// На сколько страниц глубже точки останова дельты пагинировать МИНИМУМ (страховка,
    /// даже если pending нет). По умолчанию 1 — одна лишняя страница «на всякий».
    /// </summary>
    public int DeltaOverscanPages { get; set; } = 1;

    /// <summary>Максимум страниц за один синк (страховка от бесконечной пагинации). 0 = без лимита.</summary>
    public int MaxPagesPerSync { get; set; } = 1000;

    /// <summary>
    /// Минимальный интервал между ручными обновлениями по кнопке «Обновить» (минуты).
    /// Защита от спама лимитов Uzum. Раньше был хардкод 30.
    /// </summary>
    public int RefreshThrottleMinutes { get; set; } = 30;

    /// <summary>
    /// Интервал между первичными синками разных магазинов при приёмке (минуты) —
    /// чтобы не завалить Uzum всеми магазинами сразу. Раньше хардкод 2.
    /// </summary>
    public int ProvisionSpacingMinutes { get; set; } = 2;

    /// <summary>
    /// Максимальный случайный сдвиг (секунды) перед синком каждого магазина в ночной
    /// джобе — размазывает нагрузку. Раньше хардкод 30. 0 = без сдвига.
    /// </summary>
    public int NightlyJitterSeconds { get; set; } = 30;
}
