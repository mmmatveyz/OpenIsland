using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OpenIsland;

/// <summary>
/// Чистая логика списка сессий OpenCode: порядок, поиск, имя папки и
/// относительное время последней активности. Без UI — покрывается тестами.
/// </summary>
static class SessionList
{
    /// <summary>Поле поиска показываем, только когда сессий достаточно много.</summary>
    public const int SearchThreshold = 8;

    /// <summary>Свежие сессии сверху; порядок OpenCode не гарантирован.</summary>
    public static List<OpenCodeSessionInfo> Order(IEnumerable<OpenCodeSessionInfo> sessions)
        => sessions.OrderByDescending(session => session.UpdatedAt).ToList();

    /// <summary>Фильтр по названию или папке (без учёта регистра). Пустой запрос — все сессии.</summary>
    public static List<OpenCodeSessionInfo> Filter(IEnumerable<OpenCodeSessionInfo> sessions, string? query)
    {
        List<OpenCodeSessionInfo> ordered = Order(sessions);
        string needle = (query ?? "").Trim();
        if (needle.Length == 0) return ordered;

        return ordered
            .Where(session =>
                Contains(session.Title, needle) || Contains(session.Directory, needle))
            .ToList();
    }

    static bool Contains(string value, string needle)
        => value.Length > 0 && value.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>Последний сегмент пути; для корня диска возвращает сам путь.</summary>
    public static string FolderName(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return "";
        string path = directory.TrimEnd('\\', '/');
        if (path.Length == 0) return directory!;

        string name = Path.GetFileName(path);
        return name.Length > 0 ? name : path;
    }

    /// <summary>«сейчас», «5 мин», «2 ч», «3 д», дальше — «12.05».</summary>
    public static string RelativeTime(long unixMilliseconds, DateTime utcNow)
    {
        if (unixMilliseconds <= 0) return "";

        DateTime time;
        try
        {
            time = DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).UtcDateTime;
        }
        catch (ArgumentOutOfRangeException)
        {
            return "";
        }

        TimeSpan age = utcNow - time;
        if (age < TimeSpan.FromMinutes(1)) return "сейчас";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} мин";
        if (age < TimeSpan.FromDays(1)) return $"{(int)age.TotalHours} ч";
        if (age < TimeSpan.FromDays(7)) return $"{(int)age.TotalDays} д";

        return time.ToLocalTime().ToString("dd.MM", CultureInfo.InvariantCulture);
    }
}
