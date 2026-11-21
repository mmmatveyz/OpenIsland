using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace OpenIsland;

/// <summary>
/// Один элемент полки файлов.
/// </summary>
public sealed record FileShelfItem(string Path, string Name, long SizeBytes, DateTime AddedAtUtc)
{
    public string FormattedSize => FileShelf.FormatSize(SizeBytes);
    public bool Exists => File.Exists(Path) || Directory.Exists(Path);
    public bool IsDirectory => Directory.Exists(Path);
}

/// <summary>
/// Чистая логика «Полки файлов»: временное хранение путей перетащенных файлов,
/// форматирование размеров, проверка существования и управление списком.
/// </summary>
public static class FileShelf
{
    public const int MaxCapacity = 25;

    static readonly object Gate = new();
    static readonly List<FileShelfItem> Items = new();

    public static event Action? Changed;

    public static IReadOnlyList<FileShelfItem> All
    {
        get
        {
            lock (Gate) return Items.ToList();
        }
    }

    public static int Count
    {
        get
        {
            lock (Gate) return Items.Count;
        }
    }

    /// <summary>
    /// Добавляет пути файлов на полку. Новые файлы встают в начало.
    /// Дубликаты перемещаются наверх. Лимит — <see cref="MaxCapacity"/>.
    /// </summary>
    public static int AddFiles(IEnumerable<string>? paths)
    {
        if (paths == null) return 0;
        int added = 0;

        lock (Gate)
        {
            foreach (string raw in paths)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string clean = raw.Trim().Trim('"', '\'');
                if (clean.Length == 0) continue;

                long size = 0;
                string name = System.IO.Path.GetFileName(clean);

                if (File.Exists(clean))
                {
                    try
                    {
                        var info = new FileInfo(clean);
                        size = info.Length;
                        if (string.IsNullOrEmpty(name)) name = info.Name;
                    }
                    catch
                    {
                        // Не удалось прочитать размер — останется 0
                    }
                }
                else if (Directory.Exists(clean))
                {
                    if (string.IsNullOrEmpty(name)) name = clean;
                }
                else
                {
                    // Файл не существует на диске
                    continue;
                }

                if (string.IsNullOrEmpty(name)) name = clean;

                // Удаляем существующий дубликат, чтобы поднять его наверх
                Items.RemoveAll(x => string.Equals(x.Path, clean, StringComparison.OrdinalIgnoreCase));

                Items.Insert(0, new FileShelfItem(clean, name, size, DateTime.UtcNow));
                added++;

                if (Items.Count > MaxCapacity)
                {
                    Items.RemoveAt(Items.Count - 1);
                }
            }
        }

        if (added > 0) Changed?.Invoke();
        return added;
    }

    public static bool Remove(string path)
    {
        bool removed;
        lock (Gate)
        {
            removed = Items.RemoveAll(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)) > 0;
        }
        if (removed) Changed?.Invoke();
        return removed;
    }

    public static void Clear()
    {
        bool hadItems;
        lock (Gate)
        {
            hadItems = Items.Count > 0;
            Items.Clear();
        }
        if (hadItems) Changed?.Invoke();
    }

    /// <summary>
    /// Форматирует байты в читаемый вид (Б, КБ, МБ, ГБ).
    /// </summary>
    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 Б";
        if (bytes < 1024) return $"{bytes} Б";
        if (bytes < 1024 * 1024) return string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:F1} КБ");
        if (bytes < 1024 * 1024 * 1024) return string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):F1} МБ");
        return string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} ГБ");
    }
}
