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

public sealed record GeoCity(string Name, string Country, string AdminRegion, double Latitude, double Longitude)
{
    public string DisplayName => string.IsNullOrWhiteSpace(AdminRegion) || AdminRegion == Name
        ? (string.IsNullOrWhiteSpace(Country) ? Name : $"{Name}, {Country}")
        : $"{Name}, {AdminRegion}, {Country}";
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
    readonly Func<bool> _hasManualLocation;
    CurrentWeather? _cached;
    DateTime _cachedAtUtc;
    bool _isLoading;
    bool _reloadRequested;
    string? _lastError;
    double _cachedLat = 55.75, _cachedLon = 37.62;
    string _cachedCity = "Москва";
    bool _geoResolved;

    public event Action? Changed;

    public CurrentWeather? Current => _cached;
    public bool IsLoading => _isLoading;
    public string? LastError => _lastError;
    public bool IsManualLocation => Settings.HasManualWeatherLocation;

    public WeatherService(HttpClient? http = null, Func<bool>? hasManualLocation = null)
    {
        _http = http ?? new HttpClient { Timeout = RequestTimeout };
        // В тестах признак ручной локации подменяется, чтобы не читать реальный реестр.
        _hasManualLocation = hasManualLocation ?? (() => Settings.HasManualWeatherLocation);
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    /// <summary>
    /// Ищет города по названию через геокодер Open-Meteo.
    /// </summary>
    public async Task<IReadOnlyList<GeoCity>> SearchCitiesAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            return Array.Empty<GeoCity>();

        try
        {
            string url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query.Trim())}&count=6&language=ru&format=json";
            using var response = await _http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(ct);
            return ParseGeocodingJson(json);
        }
        catch (Exception ex)
        {
            App.LogDebug($"WeatherService: search cities error: {ex.Message}");
            return Array.Empty<GeoCity>();
        }
    }

    /// <summary>
    /// Устанавливает город вручную, сохраняет в настройках и обновляет прогноз.
    /// </summary>
    public async Task SetManualCityAsync(GeoCity city)
    {
        Settings.WeatherCity = city.Name;
        Settings.WeatherLat = city.Latitude;
        Settings.WeatherLon = city.Longitude;
        _cachedCity = city.Name;
        _cachedLat = city.Latitude;
        _cachedLon = city.Longitude;
        _geoResolved = true;
        await GetWeatherAsync(force: true);
    }

    /// <summary>
    /// Сбрасывает ручной выбор города на автоопределение по IP.
    /// </summary>
    public async Task ResetToAutoLocationAsync()
    {
        Settings.ResetWeatherLocation();
        _geoResolved = false;
        await GetWeatherAsync(force: true);
    }

    /// <summary>
    /// Получает текущую погоду. Если в кэше есть свежие данные (до 25 мин), возвращает их сразу.
    /// Пока запрос выполняется, повторный принудительный запрос не теряется: он ставится
    /// в очередь и выполняется сразу после текущего, уже с новыми координатами и городом.
    /// </summary>
    public async Task<CurrentWeather?> GetWeatherAsync(bool force = false)
    {
        if (!force && _cached != null && (DateTime.UtcNow - _cachedAtUtc) < CacheDuration)
        {
            return _cached;
        }

        if (_isLoading)
        {
            if (force) _reloadRequested = true;
            return _cached;
        }

        _isLoading = true;
        try
        {
            _lastError = null;
            Changed?.Invoke();

            CurrentWeather? result = _cached;
            do
            {
                _reloadRequested = false;
                result = await FetchWeatherAsync();
            }
            while (_reloadRequested);

            return result;
        }
        finally
        {
            _isLoading = false;
            Changed?.Invoke();
        }
    }

    async Task<CurrentWeather?> FetchWeatherAsync()
    {
        try
        {
            if (!_geoResolved)
            {
                await ResolveLocationAsync();
            }

            // Снимаем координаты и город до запроса: пока идёт сеть, пользователь может
            // выбрать другой город, и ответ со старыми координатами не должен получить новое имя.
            double lat = _cachedLat;
            double lon = _cachedLon;
            string city = _cachedCity;

            string url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={lat:F4}&longitude={lon:F4}&current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,precipitation,weather_code,wind_speed_10m&hourly=temperature_2m,weather_code,is_day&forecast_days=2&timezone=auto");

            using var response = await _http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            CurrentWeather parsed = ParseOpenMeteoJson(json, city);

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
    }

    async Task ResolveLocationAsync()
    {
        if (_hasManualLocation())
        {
            _cachedCity = Settings.WeatherCity;
            _cachedLat = Settings.WeatherLat;
            _cachedLon = Settings.WeatherLon;
            _geoResolved = true;
            return;
        }

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
        catch (Exception ex)
        {
            // Ошибка гео-поиска: остаёмся на дефолтных координатах, но не помечаем локацию
            // разрешённой — при следующем обновлении попробуем определить город снова.
            App.LogDebug($"WeatherService: ip location error: {ex.Message}");
            _geoResolved = false;
        }
    }

    public static IReadOnlyList<GeoCity> ParseGeocodingJson(string json)
    {
        var list = new List<GeoCity>();
        if (string.IsNullOrWhiteSpace(json)) return list;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return list;
            }

            foreach (var item in results.EnumerateArray())
            {
                string name = item.TryGetProperty("name", out var np) ? np.GetString() ?? "" : "";
                string country = item.TryGetProperty("country", out var cp) ? cp.GetString() ?? "" : "";
                string admin = item.TryGetProperty("admin1", out var ap) ? ap.GetString() ?? "" : "";
                double lat = item.TryGetProperty("latitude", out var lp) ? lp.GetDouble() : 0.0;
                double lon = item.TryGetProperty("longitude", out var lonp) ? lonp.GetDouble() : 0.0;

                if (!string.IsNullOrWhiteSpace(name))
                {
                    list.Add(new GeoCity(name, country, admin, lat, lon));
                }
            }
        }
        catch (Exception ex)
        {
            App.LogDebug($"WeatherService: ParseGeocodingJson error: {ex.Message}");
        }

        return list;
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
