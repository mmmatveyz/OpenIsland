using System;
using System.Collections.Generic;
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
}
