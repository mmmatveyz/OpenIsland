namespace OpenIsland;

/// <summary>Состояние агента, которое уместно показать прямо в свёрнутой пилюле.</summary>
public enum AgentPillState
{
    /// <summary>Показывать нечего: остров свернут в обычные часы.</summary>
    Hidden,

    /// <summary>Агент выполняет задачу.</summary>
    Busy,

    /// <summary>Агент ждёт подтверждения разрешения — самый важный сигнал.</summary>
    Asks,

    /// <summary>Задача только что завершилась (короткая зелёная отметка вместо тоста).</summary>
    Done,
}

/// <summary>
/// Чистая логика индикатора агента в свёрнутой пилюле. Вынесена из окна, чтобы
/// приоритет состояний (разрешение → работа → «готово» → тишина) проверялся тестами
/// без UI и таймеров.
/// </summary>
public static class AgentPill
{
    /// <summary>Сколько «готово» остаётся на виду после завершения задачи.</summary>
    public static readonly TimeSpan DoneLinger = TimeSpan.FromSeconds(12);

    /// <summary>
    /// Приоритет: разрешение важнее работы, а «готово» живёт ограниченное время.
    /// Отрицательный <paramref name="sinceDone"/> (часы «прыгнули» назад) не считается готовностью.
    /// </summary>
    public static AgentPillState StateOf(bool connected, bool busy, bool asks, TimeSpan sinceDone)
    {
        if (!connected) return AgentPillState.Hidden;
        if (asks) return AgentPillState.Asks;
        if (busy) return AgentPillState.Busy;
        return sinceDone >= TimeSpan.Zero && sinceDone < DoneLinger ? AgentPillState.Done : AgentPillState.Hidden;
    }
}