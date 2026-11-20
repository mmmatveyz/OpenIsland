using System;
using System.Globalization;

namespace OpenIsland;

/// <summary>
/// Форматирование и преобразование цветов для экранной пипетки.
/// </summary>
public static class ColorFormat
{
    /// <summary>Форматирует байты RGB в стандартную шестнадцатеричную строку вида #RRGGBB.</summary>
    public static string FormatHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    /// <summary>Форматирует байты RGB в строку вида rgb(r, g, b).</summary>
    public static string FormatRgb(byte r, byte g, byte b) => $"rgb({r}, {g}, {b})";

    /// <summary>Преобразует Win32 COLORREF (0x00BBGGRR) в байты R, G, B.</summary>
    public static (byte r, byte g, byte b) FromColorRef(uint colorRef) =>
        ((byte)(colorRef & 0xFF),
         (byte)((colorRef >> 8) & 0xFF),
         (byte)((colorRef >> 16) & 0xFF));

    /// <summary>Разбирает строку #RRGGBB или RRGGBB в байты R, G, B.</summary>
    public static bool TryParseHex(string? hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        string s = hex.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length != 6) return false;

        if (byte.TryParse(s[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) &&
            byte.TryParse(s[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) &&
            byte.TryParse(s[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
        {
            return true;
        }

        r = g = b = 0;
        return false;
    }
}
