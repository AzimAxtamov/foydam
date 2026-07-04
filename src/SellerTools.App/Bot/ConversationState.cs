using System.Collections.Concurrent;

namespace SellerTools.App.Bot;

/// <summary>Шаг диалога подключения магазина.</summary>
public enum DialogStep
{
    None = 0,
    ChoosingLanguage,
    AwaitingKey,
    ChoosingShop, // ключ прислан, у него несколько магазинов — выбираем какой
}

/// <summary>Состояние диалога одного пользователя (в памяти).</summary>
public class ConversationState
{
    public DialogStep Step { get; set; } = DialogStep.None;
    public string Language { get; set; } = "ru";

    /// <summary>
    /// Ключ, ожидающий привязки (на шаге ChoosingShop, когда у ключа несколько
    /// магазинов). Держим в памяти только до выбора магазина, затем шифруем и чистим.
    /// </summary>
    public string? PendingApiKey { get; set; }

    /// <summary>Магазины, доступные ключу (id → имя), для шага выбора.</summary>
    public Dictionary<long, string?> DiscoveredShops { get; set; } = new();
}

/// <summary>
/// Хранилище состояний диалогов в памяти. Для В3 достаточно; переживать
/// рестарты не обязано (после /start диалог начинается заново).
/// </summary>
public class ConversationStore
{
    private readonly ConcurrentDictionary<long, ConversationState> _states = new();

    public ConversationState Get(long userId) =>
        _states.GetOrAdd(userId, _ => new ConversationState());

    public void Reset(long userId) => _states.TryRemove(userId, out _);
}
