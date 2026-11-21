using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

public class WeatherMathTests
{
    [Theory]
    [InlineData(0, true, Glyph.Sun, "Ясно")]
    [InlineData(0, false, Glyph.Moon, "Ясно")]
    [InlineData(2, true, Glyph.CloudSun, "Переменная облачность")]
    [InlineData(2, false, Glyph.CloudMoon, "Переменная облачность")]
    [InlineData(3, true, Glyph.Cloud, "Пасмурно")]
    [InlineData(45, true, Glyph.Fog, "Туман")]
    [InlineData(61, true, Glyph.Rain, "Дождь")]
    [InlineData(71, true, Glyph.Snow, "Снег")]
    [InlineData(95, true, Glyph.Storm, "Гроза")]
    public void DescribeWmoCode_MapsAccurately(int code, bool isDay, Glyph expectedGlyph, string expectedText)
    {
        var (glyph, text) = WeatherMath.DescribeWmoCode(code, isDay);
        Assert.Equal(expectedGlyph, glyph);
        Assert.Equal(expectedText, text);
    }

    [Theory]
    [InlineData(21.4, "+21°")]
    [InlineData(0.2, "0°")]
    [InlineData(-0.1, "0°")]
    [InlineData(-7.8, "-8°")]
    [InlineData(1.0, "+1°")]
    public void FormatTemp_FormatsSignedIntegers(double input, string expected)
    {
        Assert.Equal(expected, WeatherMath.FormatTemp(input));
    }

    [Fact]
    public void FormatWind_ConvertsKmhToMs()
    {
        // 36 км/ч = 10 м/с
        Assert.Equal("10.0 м/с", WeatherMath.FormatWind(36.0));
        Assert.Equal("0.0 м/с", WeatherMath.FormatWind(0.0));
    }

    [Fact]
    public void FormatPrecipitation_HandlesZeroAndValues()
    {
        Assert.Equal("0 мм", WeatherMath.FormatPrecipitation(0));
        Assert.Equal("0 мм", WeatherMath.FormatPrecipitation(0.02));
        Assert.Equal("3.5 мм", WeatherMath.FormatPrecipitation(3.5));
    }

    [Fact]
    public void FormatHumidity_ClampsAndFormats()
    {
        Assert.Equal("0%", WeatherMath.FormatHumidity(-5));
        Assert.Equal("65%", WeatherMath.FormatHumidity(65.2));
        Assert.Equal("100%", WeatherMath.FormatHumidity(150));
    }
}
