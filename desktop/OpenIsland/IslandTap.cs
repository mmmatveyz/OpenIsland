namespace OpenIsland;

/// <summary>Какая карточка открывается по левому клику на свёрнутом островке.</summary>
public enum TapTarget
{
    /// <summary>Медиаплеер — если сейчас играет музыка.</summary>
    Player,

    /// <summary>Карточка агента OpenCode.</summary>
    OpenCode,

    /// <summary>Таймер — пока он идёт.</summary>
    Timer,

    /// <summary>Меню со списком виджетов.</summary>
    Menu,
}

/// <summary>
/// Чистая логика выбора карточки по клику на свёрнутый островок. Вынесена из окна,
/// чтобы порядок приоритетов и правило «выключенный AI Hub не открывается»
/// проверялись тестами без UI.
/// </summary>
public static class IslandTap
{
    /// <summary>
    /// Плеер важнее фоновой задачи агента, таймер успевает между ними, а карточка
    /// агента доступна только при включённом виджете OpenCode — иначе клик ведёт в меню.
    /// </summary>
    public static TapTarget Target(bool showOpenCode, bool mediaActive, bool agentBusy, bool timerActive, bool agentConnected)
    {
        if (mediaActive) return TapTarget.Player;
        if (showOpenCode && agentBusy) return TapTarget.OpenCode;
        if (timerActive) return TapTarget.Timer;
        if (showOpenCode && agentConnected) return TapTarget.OpenCode;
        return TapTarget.Menu;
    }
}
