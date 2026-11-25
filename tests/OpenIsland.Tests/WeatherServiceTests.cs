using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

public class WeatherServiceTests
{
    const string SampleOpenMeteoJson = """
    {
      "latitude": 55.75,
      "longitude": 37.62,
      "current": {
        "time": "2026-11-21T12:00",
        "temperature_2m": 14.2,
        "relative_humidity_2m": 68,
        "apparent_temperature": 12.8,
        "is_day": 1,
        "precipitation": 0.0,
        "weather_code": 2,
        "wind_speed_10m": 12.5
      },
      "hourly": {
        "time": ["2026-11-21T12:00", "2026-11-21T13:00", "2026-11-21T14:00"],
        "temperature_2m": [14.2, 15.0, 14.8],
        "weather_code": [2, 1, 0],
        "is_day": [1, 1, 1]
      }
    }
    """;

    const string SampleGeocodingJson = """
    {
      "results": [
        {
          "id": 524901,
          "name": "Москва",
          "latitude": 55.75222,
          "longitude": 37.61556,
          "country": "Россия",
          "admin1": "Москва"
        },
        {
          "id": 491422,
          "name": "Сочи",
          "latitude": 43.59917,
          "longitude": 39.72569,
          "country": "Россия",
          "admin1": "Краснодарский край"
        }
      ]
    }
    """;

    [Fact]
    public void ParseOpenMeteoJson_ParsesCurrentWeatherAndHourly()
    {
        CurrentWeather weather = WeatherService.ParseOpenMeteoJson(SampleOpenMeteoJson, "Москва");

        Assert.Equal("Москва", weather.CityName);
        Assert.Equal(14.2, weather.Temperature);
        Assert.Equal("+14°", weather.FormattedTemp);
        Assert.Equal(12.8, weather.ApparentTemperature);
        Assert.Equal("+13°", weather.FormattedApparent);
        Assert.Equal(68, weather.RelativeHumidity);
        Assert.Equal("68%", weather.FormattedHumidity);
        Assert.Equal(2, weather.WmoCode);
        Assert.True(weather.IsDay);
        Assert.Equal(Glyph.CloudSun, weather.Condition.Glyph);
        Assert.Equal("Переменная облачность", weather.Condition.Description);

        Assert.NotNull(weather.Hourly);
    }

    [Fact]
    public void ParseGeocodingJson_ValidResults_ReturnsListOfCities()
    {
        IReadOnlyList<GeoCity> cities = WeatherService.ParseGeocodingJson(SampleGeocodingJson);

        Assert.Equal(2, cities.Count);
        Assert.Equal("Москва", cities[0].Name);
        Assert.Equal("Россия", cities[0].Country);
        Assert.Equal("Москва", cities[0].AdminRegion);
        Assert.Equal(55.75222, cities[0].Latitude);
        Assert.Equal(37.61556, cities[0].Longitude);
        Assert.Equal("Москва, Россия", cities[0].DisplayName);

        Assert.Equal("Сочи", cities[1].Name);
        Assert.Equal("Россия", cities[1].Country);
        Assert.Equal("Краснодарский край", cities[1].AdminRegion);
        Assert.Equal(43.59917, cities[1].Latitude);
        Assert.Equal(39.72569, cities[1].Longitude);
        Assert.Equal("Сочи, Краснодарский край, Россия", cities[1].DisplayName);
    }

    [Fact]
    public void ParseGeocodingJson_EmptyOrMissingResults_ReturnsEmptyList()
    {
        Assert.Empty(WeatherService.ParseGeocodingJson(""));
        Assert.Empty(WeatherService.ParseGeocodingJson("{}"));
        Assert.Empty(WeatherService.ParseGeocodingJson("""{"results": []}"""));
        Assert.Empty(WeatherService.ParseGeocodingJson("not json"));
    }

    [Fact]
    public async Task ForceRefreshDuringLoad_IsQueuedAndRerun()
    {
        var handler = new WeatherStubHandler(SampleOpenMeteoJson);
        TaskCompletionSource<bool> gate = handler.HoldFirstForecast();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        using var service = new WeatherService(http, hasManualLocation: () => false);

        Task<CurrentWeather?> first = service.GetWeatherAsync();
        Task<CurrentWeather?> forced = service.GetWeatherAsync(force: true);

        Assert.False(first.IsCompleted);
        Assert.Equal(1, handler.ForecastRequests);

        gate.SetResult(true);
        CurrentWeather? result = await first;
        await forced;

        Assert.NotNull(result);
        Assert.Equal(2, handler.ForecastRequests);
        Assert.NotNull(service.Current);
    }

    [Fact]
    public async Task OfflineIpLookup_IsRetriedOnNextRefresh()
    {
        var handler = new WeatherStubHandler(SampleOpenMeteoJson) { IpOffline = true };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        using var service = new WeatherService(http, hasManualLocation: () => false);

        CurrentWeather? first = await service.GetWeatherAsync();

        Assert.NotNull(first);
        Assert.Equal("Москва", first!.CityName);
        Assert.Equal(1, handler.IpRequests);

        CurrentWeather? second = await service.GetWeatherAsync(force: true);

        Assert.NotNull(second);
        Assert.Equal(2, handler.IpRequests);
    }

    sealed class WeatherStubHandler : HttpMessageHandler
    {
        readonly string _forecastJson;
        TaskCompletionSource<bool>? _firstForecastGate;

        public int IpRequests;
        public int ForecastRequests;
        public bool IpOffline;

        public WeatherStubHandler(string forecastJson) => _forecastJson = forecastJson;

        public TaskCompletionSource<bool> HoldFirstForecast()
        {
            _firstForecastGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return _firstForecastGate;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string url = request.RequestUri?.ToString() ?? string.Empty;

            if (url.Contains("ip-api.com", StringComparison.Ordinal))
            {
                IpRequests++;
                if (IpOffline) throw new HttpRequestException("offline");
                return Json("""{"status":"success","city":"Самара","lat":53.2,"lon":50.15}""");
            }

            if (url.Contains("geocoding-api", StringComparison.Ordinal))
            {
                return Json("""{"results":[]}""");
            }

            ForecastRequests++;
            if (ForecastRequests == 1 && _firstForecastGate != null)
            {
                await _firstForecastGate.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }

            return Json(_forecastJson);
        }

        static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }
}
