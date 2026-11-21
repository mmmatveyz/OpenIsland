using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenIsland;

public sealed record HourlyForecast(DateTime Time, double Temp, int WmoCode, bool IsDay)
{
    public string FormattedTemp => WeatherMath.FormatTemp(Temp);
    public string HourText => Time.ToString("HH:mm");
    public (Glyph Glyph, string Description) Condition => WeatherMath.DescribeWmoCode(WmoCode, IsDay);
}

public sealed record CurrentWeather(
    string CityName,
    double Temperature,
    double ApparentTemperature,
    int RelativeHumidity,
    double Precipitation,
    double WindSpeedKmh,
    int WmoCode,
    bool IsDay,
    DateTime UpdatedAt,
    IReadOnlyList<HourlyForecast> Hourly)
{
    public string FormattedTemp => WeatherMath.FormatTemp(Temperature);
    public string FormattedApparent => WeatherMath.FormatTemp(ApparentTemperature);
    public string FormattedWind => WeatherMath.FormatWind(WindSpeedKmh);
    public string FormattedPrecipitation => WeatherMath.FormatPrecipitation(Precipitation);
    public string FormattedHumidity => WeatherMath.FormatHumidity(RelativeHumidity);
    public (Glyph Glyph, string Description) Condition => WeatherMath.DescribeWmoCode(WmoCode, IsDay);
}

/// <summary>
/// Асинхронный сервис погоды через бесплатный открытый API Open-Meteo (без API-ключей),
/// с автоопределением города по IP, кэшированием на 25 минут и безопасной обработкой офлайна.
/// </summary>
public sealed class WeatherService : IDisposable
{
    static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(25);
    static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    readonly HttpClient _http;
    CurrentWeather? _cached;
    DateTime _cachedAtUtc;
    bool _isLoading;
    string? _lastError;
    double _cachedLat = 55.75, _cachedLon = 37.62;
    string _cachedCity = "Москва";
    bool _geoResolved;

    public event Action? Changed;

    public CurrentWeather? Current => _cached;
    public bool IsLoading => _isLoading;
    public string? LastError => _lastError;

    public WeatherService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = RequestTimeout };
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    /// <summary>
    /// Получает текущую погоду. Если в кэше есть свежие данные (до 25 мин), возвращает их сразу.
    /// </summary>
    public async Task<CurrentWeather?> GetWeatherAsync(bool force = false)
    {
        if (!force && _cached != null && (DateTime.UtcNow - _cachedAtUtc) < CacheDuration)
        {
            return _cached;
        }

        if (_isLoading) return _cached;
        _isLoading = true;
        _lastError = null;
        Changed?.Invoke();

        try
        {
            if (!_geoResolved)
            {
                await ResolveLocationAsync();
            }

            string url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={_cachedLat:F4}&longitude={_cachedLon:F4}&current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,precipitation,weather_code,wind_speed_10m&hourly=temperature_2m,weather_code,is_day&forecast_days=2&timezone=auto");

            using var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            CurrentWeather parsed = ParseOpenMeteoJson(json, _cachedCity);

            _cached = parsed;
            _cachedAtUtc = DateTime.UtcNow;
            _lastError = null;
            return parsed;
        }
        catch (Exception ex)
        {
            App.LogDebug($"WeatherService: fetch error: {ex.Message}");
            _lastError = "Не удалось обновить погоду";
            return _cached;
        }
        finally
        {
            _isLoading = false;
            Changed?.Invoke();
        }
    }

    async Task ResolveLocationAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var response = await _http.GetAsync("http://ip-api.com/json/?fields=status,city,lat,lon", cts.Token);
            if (response.IsSuccessStatusCode)
            {
                string geoJson = await response.Content.ReadAsStringAsync(cts.Token);
                using var doc = JsonDocument.Parse(geoJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("status", out var st) && st.GetString() == "success")
                {
                    if (root.TryGetProperty("city", out var c)) _cachedCity = c.GetString() ?? _cachedCity;
                    if (root.TryGetProperty("lat", out var lt)) _cachedLat = lt.GetDouble();
                    if (root.TryGetProperty("lon", out var ln)) _cachedLon = ln.GetDouble();
                    _geoResolved = true;
                }
            }
        }
        catch
        {
            // Ошибка гео-поиска: используем дефолтные координаты
            _geoResolved = true;
        }
    }

    public static CurrentWeather ParseOpenMeteoJson(string json, string cityName)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var current = root.GetProperty("current");
        double temp = current.GetProperty("temperature_2m").GetDouble();
        double appTemp = current.GetProperty("apparent_temperature").GetDouble();
        int humidity = current.GetProperty("relative_humidity_2m").GetInt32();
        double precip = current.GetProperty("precipitation").GetDouble();
        int code = current.GetProperty("weather_code").GetInt32();
        double wind = current.GetProperty("wind_speed_10m").GetDouble();
        bool isDay = current.TryGetProperty("is_day", out var dayProp) && dayProp.GetInt32() == 1;

        var hourly = root.GetProperty("hourly");
        var times = hourly.GetProperty("time");
        var temps = hourly.GetProperty("temperature_2m");
        var codes = hourly.GetProperty("weather_code");
        JsonElement isDays = default;
        bool hasIsDay = hourly.TryGetProperty("is_day", out isDays);

        int count = Math.Min(times.GetArrayLength(), temps.GetArrayLength());
        DateTime now = DateTime.Now;
        var list = new List<HourlyForecast>();

        for (int i = 0; i < count && list.Count < 24; i++)
        {
            string? timeStr = times[i].GetString();
            if (!DateTime.TryParse(timeStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime t))
                continue;

            // Берём часы начиная с текущего или ближайшего будущего
            if (t < now.AddHours(-1)) continue;

            double hTemp = temps[i].GetDouble();
            int hCode = codes[i].GetInt32();
            bool hDay = hasIsDay && isDays[i].GetInt32() == 1;

            list.Add(new HourlyForecast(t, hTemp, hCode, hDay));
        }

        return new CurrentWeather(cityName, temp, appTemp, humidity, precip, wind, code, isDay, DateTime.Now, list);
    }
}
