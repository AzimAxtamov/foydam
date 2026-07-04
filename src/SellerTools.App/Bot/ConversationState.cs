using System.Collections.Concurrent;

namespace SellerTools.App.Bot;

/// <summary>Шаг диалога подключения магазина.</summary>
public enum DialogStep
{
    None = 0,
    ChoosingLanguage,
    AwaitingShopId,
    AwaitingKey,
}

/// <summary>Состояние диалога одного пользователя (в памяти).</summary>
public class ConversationState
{
    public DialogStep Step { get; set; } = DialogStep.None;
    public string Language { get; set; } = "ru";

    /// <summary>Введённый shopId (на шаге AwaitingKey).</summary>
    public long? PendingShopId { get; set; }
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
