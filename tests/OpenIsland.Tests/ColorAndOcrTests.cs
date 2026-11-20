using System;
using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

public class ColorFormatTests
{
    [Fact]
    public void FormatHex_PadsZeroesAndUppercases()
    {
        Assert.Equal("#000000", ColorFormat.FormatHex(0, 0, 0));
        Assert.Equal("#FFFFFF", ColorFormat.FormatHex(255, 255, 255));
        Assert.Equal("#0A1B2C", ColorFormat.FormatHex(10, 27, 44));
        Assert.Equal("#7D7AFF", ColorFormat.FormatHex(125, 122, 255));
    }

    [Fact]
    public void FormatRgb_BuildsCssString()
    {
        Assert.Equal("rgb(0, 0, 0)", ColorFormat.FormatRgb(0, 0, 0));
        Assert.Equal("rgb(255, 128, 64)", ColorFormat.FormatRgb(255, 128, 64));
    }

    [Fact]
    public void FromColorRef_ExtractsBytesCorrectly()
    {
        // Win32 COLORREF: 0x00BBGGRR
        uint colorRef = 0x00FF8040; // R=0x40 (64), G=0x80 (128), B=0xFF (255)
        var (r, g, b) = ColorFormat.FromColorRef(colorRef);
        Assert.Equal(64, r);
        Assert.Equal(128, g);
        Assert.Equal(255, b);
    }

    [Theory]
    [InlineData("#7D7AFF", 125, 122, 255)]
    [InlineData("7D7AFF", 125, 122, 255)]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#ffffff", 255, 255, 255)]
    public void TryParseHex_ParsesValidStrings(string input, byte expR, byte expG, byte expB)
    {
        Assert.True(ColorFormat.TryParseHex(input, out byte r, out byte g, out byte b));
        Assert.Equal(expR, r);
        Assert.Equal(expG, g);
        Assert.Equal(expB, b);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#GGGGGG")]
    public void TryParseHex_RejectsInvalidStrings(string? input)
    {
        Assert.False(ColorFormat.TryParseHex(input, out _, out _, out _));
    }
}

public class OcrResultFormatterTests
{
    [Fact]
    public void FormatText_CleansAndJoinsLines()
    {
        string[] input = ["  Line 1  ", "", "   ", "Line 2", "\tLine 3\t"];
        string result = OcrResultFormatter.FormatText(input);

        Assert.Equal($"Line 1{Environment.NewLine}Line 2{Environment.NewLine}Line 3", result);
    }

    [Fact]
    public void FormatText_EmptyOrNullReturnsEmpty()
    {
        Assert.Equal(string.Empty, OcrResultFormatter.FormatText(null));
        Assert.Equal(string.Empty, OcrResultFormatter.FormatText([]));
        Assert.Equal(string.Empty, OcrResultFormatter.FormatText(["   ", ""]));
    }

    [Fact]
    public void FormatPreview_FlattensAndTruncates()
    {
        string input = "First line\r\nSecond line with some additional words that exceed preview length limit";
        string preview = OcrResultFormatter.FormatPreview(input, 30);

        Assert.EndsWith("\u2026", preview);
        Assert.True(preview.Length <= 30);
        Assert.DoesNotContain("\r", preview);
        Assert.DoesNotContain("\n", preview);
    }

    [Fact]
    public void FormatPreview_ShortStringNotTruncated()
    {
        string input = "Short text";
        Assert.Equal("Short text", OcrResultFormatter.FormatPreview(input, 30));
    }

    [Fact]
    public void WindowsOcr_IsAvailableInRuntime()
    {
        // Проверяем, что WinRT OCR доступен и может инициализироваться
        var languages = Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages;
        Assert.NotNull(languages);
        byte[] pixels = new byte[4 * 4 * 4];
        var buf = Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(pixels);
        var sb = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(
            buf,
            Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            4,
            4,
            Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
        Assert.NotNull(sb);
    }
}
