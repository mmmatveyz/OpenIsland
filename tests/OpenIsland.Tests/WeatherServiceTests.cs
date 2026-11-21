using System;
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
}
