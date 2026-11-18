using System.Diagnostics;
using System.IO;

namespace OpenIsland;

/// <summary>Одна команда карточки «Быстрый запуск»: подпись, цель и аргументы.</summary>
sealed record QuickAction(string Name, string Target, string Arguments = "")
{
    /// <summary>Подпись строки: имя, а если его нет — имя файла без расширения.</summary>
    public string DisplayName => Name.Length > 0 ? Name : Path.GetFileNameWithoutExtension(Target);
}

/// <summary>
/// Чистая логика карточки «Быстрый запуск»: список своих программ хранится
/// в реестре одной строкой <c>Имя|Путь|Аргументы</c> (по строке на команду),
/// поэтому разбор и сборка вынесены сюда и покрыты юнит-тестами.
/// Запуск — через оболочку Windows, чтобы работали и <c>exe</c>, и ярлыки,
/// и системные URI вроде <c>ms-settings:</c>.
/// </summary>
static class QuickActions
{
    const char Separator = '|';

    static readonly string[] Supported = [".exe", ".lnk", ".bat", ".cmd"];

    /// <summary>Системные утилиты карточки. Терминал выбирается по наличию Windows Terminal.</summary>
    public static IReadOnlyList<QuickAction> SystemActions() =>
    [
        new("Проводник", "explorer.exe"),
        new("Терминал", TerminalTarget()),
        new("Калькулятор", "calc.exe"),
        new("Блокнот", "notepad.exe"),
        new("Диспетчер задач", "taskmgr.exe"),
        new("Параметры", "ms-settings:"),
        new("Панель управления", "control.exe"),
        new("Ножницы", "ms-screenclip:"),
    ];

    static string TerminalTarget()
    {
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string folder in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (File.Exists(Path.Combine(folder.Trim(), "wt.exe"))) return "wt.exe";
            }
            catch (ArgumentException)
            {
                // Битая запись PATH: пропускаем её, остальные всё равно проверим.
            }
        }
        return "powershell.exe";
    }

    /// <summary>Разбирает строку из реестра, молча пропуская пустые и битые записи.</summary>
    public static IReadOnlyList<QuickAction> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var actions = new List<QuickAction>();
        foreach (string line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split(Separator, 3);
            if (parts.Length < 2) continue;

            string name = parts[0].Trim();
            string target = parts[1].Trim();
            if (target.Length == 0) continue;

            actions.Add(new QuickAction(name, target, parts.Length == 3 ? parts[2].Trim() : ""));
        }
        return actions;
    }

    /// <summary>Собирает список обратно в строку реестра (по строке на команду).</summary>
    public static string Format(IEnumerable<QuickAction> actions) =>
        string.Join('\n', actions.Select(action =>
            $"{CleanName(action.Name)}{Separator}{Clean(action.Target)}{Separator}{Clean(action.Arguments)}"));

    /// <summary>Своя программа из выбранного файла: имя — без расширения, путь — полный.</summary>
    public static QuickAction FromFile(string path)
    {
        string full = Path.GetFullPath(path.Trim());
        return new QuickAction(Path.GetFileNameWithoutExtension(full), full);
    }

    /// <summary>Можно ли добавлять файл такого типа в список своих программ.</summary>
    public static bool IsSupported(string path)
    {
        string extension = Path.GetExtension(path);
        return Supported.Any(item => extension.Equals(item, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Есть ли шанс запустить команду: системные имена и URI разрешает оболочка,
    /// а для полного пути проверяем, что файл ещё существует.
    /// </summary>
    public static bool CanStart(QuickAction action) =>
        action.Target.Length > 0 && (!Path.IsPathRooted(action.Target) || File.Exists(action.Target));

    /// <summary>Параметры запуска через оболочку Windows; для файла задаётся его папка.</summary>
    public static ProcessStartInfo StartInfo(QuickAction action)
    {
        var info = new ProcessStartInfo
        {
            FileName = action.Target,
            Arguments = action.Arguments,
            UseShellExecute = true,
        };

        if (Path.IsPathRooted(action.Target) && Path.GetDirectoryName(action.Target) is { Length: > 0 } folder)
            info.WorkingDirectory = folder;
        return info;
    }

    static string CleanName(string? name) =>
        (name ?? "").Replace(Separator, ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

    static string Clean(string? value) =>
        (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
}
