using System;
using System.Globalization;

namespace OpenIsland;

/// <summary>
/// Чистая логика разбора метеоданных: WMO-коды в глифы и текст, форматирование температур и ветра.
/// </summary>
public static class WeatherMath
{
    /// <summary>
    /// Преобразует WMO-код погоды (0..99) в соответствующий Glyph и русскоязычное описание.
    /// </summary>
    public static (Glyph glyph, string description) DescribeWmoCode(int code, bool isDay = true) => code switch
    {
        0 => (isDay ? Glyph.Sun : Glyph.Moon, "Ясно"),
        1 => (isDay ? Glyph.CloudSun : Glyph.CloudMoon, "Преимущественно ясно"),
        2 => (isDay ? Glyph.CloudSun : Glyph.CloudMoon, "Переменная облачность"),
        3 => (Glyph.Cloud, "Пасмурно"),
        45 or 48 => (Glyph.Fog, "Туман"),
        51 or 53 or 55 => (Glyph.Rain, "Морось"),
        56 or 57 => (Glyph.Rain, "Ледяная морось"),
        61 or 63 or 65 => (Glyph.Rain, "Дождь"),
        66 or 67 => (Glyph.Rain, "Ледяной дождь"),
        71 or 73 or 75 or 77 => (Glyph.Snow, "Снег"),
        80 or 81 or 82 => (Glyph.Rain, "Ливень"),
        85 or 86 => (Glyph.Snow, "Снегопад"),
        95 or 96 or 99 => (Glyph.Storm, "Гроза"),
        _ => (Glyph.CloudSun, "Облачно"),
    };

    /// <summary>
    /// Форматирует температуру целым числом со знаком: +18°, 0°, -5°.
    /// </summary>
    public static string FormatTemp(double temp)
    {
        int rounded = (int)Math.Round(temp);
        return rounded > 0 ? $"+{rounded}\u00B0" : $"{rounded}\u00B0";
    }

    /// <summary>
    /// Переводит скорость ветра из км/ч в м/с и форматирует.
    /// </summary>
    public static string FormatWind(double speedKmh)
    {
        double ms = speedKmh / 3.6;
        return string.Create(CultureInfo.InvariantCulture, $"{ms:F1} м/с");
    }

    /// <summary>
    /// Форматирует количество осадков в миллиметрах.
    /// </summary>
    public static string FormatPrecipitation(double mm)
    {
        if (mm <= 0.05) return "0 мм";
        return string.Create(CultureInfo.InvariantCulture, $"{mm:F1} мм");
    }

    /// <summary>
    /// Форматирует влажность в процентах.
    /// </summary>
    public static string FormatHumidity(double percent)
    {
        int clamped = Math.Clamp((int)Math.Round(percent), 0, 100);
        return $"{clamped}%";
    }
}
