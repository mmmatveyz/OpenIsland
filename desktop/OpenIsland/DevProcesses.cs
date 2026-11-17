using System.Globalization;

namespace OpenIsland;

/// <summary>Один процесс в карточке «Процессы и порты».</summary>
sealed record DevProcessInfo(int Pid, string Name, long WorkingSetBytes, IReadOnlyList<int> Ports)
{
    /// <summary>Первый занятый порт (для сортировки: процессы с портами идут первыми).</summary>
    public int FirstPort => Ports.Count == 0 ? int.MaxValue : Ports[0];
}

/// <summary>
/// Чистая логика карточки «Процессы и порты»: какие процессы интересны,
/// как их упорядочить и как подписать память и порты. Без Win32 — всё это
/// проверяется юнит-тестами; перечисление процессов и портов лежит рядом
/// (<see cref="DevProcessService"/>, <see cref="PortScanner"/>).
/// </summary>
static class DevProcesses
{
    const int MaxShownPorts = 4;

    /// <summary>Инструменты, которые пользователь ожидает увидеть в списке даже без слушающего порта.</summary>
    static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        "node", "nodejs", "npm", "npx", "pnpm", "yarn", "bun", "deno", "tsx", "nodemon",
        "vite", "next-server", "esbuild", "webpack", "turbo",
        "python", "py", "pythonw", "uv", "uvicorn", "gunicorn", "celery", "pip",
        "dotnet", "msbuild", "vsdbg", "vstest",
        "docker", "dockerd", "docker-compose", "com.docker.backend", "com.docker.build",
        "go", "gofmt", "gopls", "air",
        "java", "javac", "gradle", "mvn", "kotlin", "scala",
        "ruby", "rails", "puma", "bundle", "irb",
        "php", "php-fpm", "composer", "artisan",
        "cargo", "rustc", "rust-analyzer", "clippy-driver",
        "postgres", "postmaster", "mysqld", "mariadbd", "mongod", "redis-server", "redis",
        "nginx", "apache2", "httpd", "caddy", "traefik",
        "ollama", "cloudflared", "ngrok", "firebase",
        "wsl", "wslhost", "vmmem", "vmmemwsl", "wslrelay",
        "code", "code-insiders", "cursor", "idea64", "rider64", "studio64",
    };

    /// <summary>
    /// Системные процессы, которые почти всегда держат служебные порты.
    /// Их не показываем, чтобы список не превращался в netstat целиком.
    /// </summary>
    static readonly HashSet<string> System = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "memory compression", "smss", "csrss", "wininit", "winlogon",
        "services", "lsass", "svchost", "spoolsv", "dwm", "fontdrvhost", "ctfmon", "sihost",
        "taskhostw", "conhost", "dllhost", "runtimebroker", "searchindexer", "searchapp",
        "searchhost", "startmenuexperiencehost", "shellexperiencehost", "textinputhost",
        "applicationframehost", "lockapp", "logonui", "backgroundtaskhost", "smartscreen",
        "securityhealthservice", "securityhealthsystray", "msmpeng", "nissrv", "mpdefendercoreservice",
        "audiodg", "wudfhost", "explorer", "systemsettings", "widgetservice", "widgetboard",
        "msedgewebview2", "aggregatorhost", "useroobebroker", "sppsvc", "trustedinstaller",
    };

    /// <summary>Нормализованное имя процесса: без пробелов по краям и без расширения <c>.exe</c>.</summary>
    public static string Normalize(string? name)
    {
        string value = (name ?? "").Trim();
        return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? value[..^4] : value;
    }

    /// <summary>Относится ли процесс к известным инструментам разработки (node, python, dotnet, docker, go…).</summary>
    public static bool IsKnown(string? name)
    {
        string value = Normalize(name);
        if (value.Length == 0) return false;
        if (Known.Contains(value)) return true;

        // python3.11, php8, ruby3.2 и подобные версии.
        return IsVersioned(value, "python") || IsVersioned(value, "php") || IsVersioned(value, "ruby");
    }

    /// <summary>Служебный процесс Windows: его порты в карточке не нужны.</summary>
    public static bool IsSystem(string? name) => System.Contains(Normalize(name));

    /// <summary>
    /// Показывать ли процесс: известный dev-инструмент — всегда, остальные — только
    /// если они занимают порт. Так «что заняло 3000» находится, даже когда запущен
    /// незнакомый сервер, а фоновые службы без портов не мешают.
    /// </summary>
    public static bool IsInteresting(string? name, bool holdsPort)
    {
        if (IsKnown(name)) return true;
        return holdsPort && !IsSystem(name);
    }

    /// <summary>Порты процесса без повторов и в порядке возрастания.</summary>
    public static IReadOnlyList<int> SortPorts(IEnumerable<int>? ports)
    {
        if (ports == null) return [];
        return ports.Where(port => port is > 0 and <= 65535).Distinct().OrderBy(port => port).ToList();
    }

    /// <summary>Сначала процессы с портами (по возрастанию первого порта), затем остальные по памяти.</summary>
    public static IReadOnlyList<DevProcessInfo> Sort(IEnumerable<DevProcessInfo> items) =>
        items
            .OrderBy(item => item.Ports.Count == 0 ? 1 : 0)
            .ThenBy(item => item.FirstPort)
            .ThenByDescending(item => item.WorkingSetBytes)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Компактная подпись портов: «3000, 5173», для длинного списка — «… +2».</summary>
    public static string FormatPorts(IReadOnlyList<int>? ports, int max = MaxShownPorts)
    {
        if (ports == null || ports.Count == 0) return "—";
        if (max <= 0) max = MaxShownPorts;
        string shown = string.Join(", ", ports.Take(max));
        return ports.Count > max ? $"{shown} +{ports.Count - max}" : shown;
    }

    /// <summary>Полная подпись портов для подсказки (без сокращения).</summary>
    public static string PortsTooltip(IReadOnlyList<int>? ports) =>
        ports == null || ports.Count == 0 ? "Слушающих портов нет" : "Порты: " + string.Join(", ", ports);

    /// <summary>Рабочий набор процесса: «120 МБ», «1.2 ГБ».</summary>
    public static string FormatMemory(long bytes)
    {
        if (bytes <= 0) return "—";
        double megabytes = bytes / 1048576.0;
        if (megabytes < 1) return "<1 МБ";
        if (megabytes < 1024) return string.Create(CultureInfo.InvariantCulture, $"{megabytes:0} МБ");
        double gigabytes = megabytes / 1024.0;
        return string.Create(CultureInfo.InvariantCulture, $"{gigabytes:0.0} ГБ");
    }

    /// <summary>«3 порта» для шапки меню: русское склонение без библиотек.</summary>
    public static string PortsCount(int count)
    {
        if (count <= 0) return "";
        int lastTwo = count % 100, last = count % 10;
        if (lastTwo is >= 11 and <= 14) return $"{count} портов";
        return last switch
        {
            1 => $"{count} порт",
            2 or 3 or 4 => $"{count} порта",
            _ => $"{count} портов",
        };
    }

    static bool IsVersioned(string name, string prefix) =>
        name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && name.Length > prefix.Length
        && name[prefix.Length..].All(symbol => char.IsDigit(symbol) || symbol == '.');
}
