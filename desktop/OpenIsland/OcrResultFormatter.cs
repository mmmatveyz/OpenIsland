using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenIsland;

/// <summary>
/// Форматирование и очистка текста, распознанного с экрана через OCR.
/// </summary>
public static class OcrResultFormatter
{
    /// <summary>
    /// Объединяет непустые строки текста, убирая лишние пробелы по краям.
    /// </summary>
    public static string FormatText(IEnumerable<string>? lines)
    {
        if (lines == null) return string.Empty;
        var cleaned = lines
            .Select(line => line?.Trim() ?? string.Empty)
            .Where(line => line.Length > 0);
        return string.Join(Environment.NewLine, cleaned);
    }

    /// <summary>
    /// Строит краткое однострочное превью для карточки-уведомления.
    /// </summary>
    public static string FormatPreview(string? text, int maxLength = 50)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        string singleLine = string.Join(" ", text.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (singleLine.Length <= maxLength) return singleLine;
        return singleLine[..(maxLength - 1)] + "\u2026";
    }
}
