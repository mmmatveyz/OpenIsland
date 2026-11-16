using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

/// <summary>
/// Очистка markdown-разметки для карточки OpenCode: сообщения выводятся
/// обычным TextBlock, поэтому маркеры **жирного**, кода, ссылок и списков
/// не должны попадать на экран, а сам текст — не портиться.
/// </summary>
public class MarkdownTextTests
{
    [Fact]
    public void BoldAndItalicMarkersAreRemoved()
    {
        Assert.Equal("Готово и проверено", MarkdownText.ToPlainText("**Готово** и *проверено*"));
        Assert.Equal("Готово", MarkdownText.ToPlainText("__Готово__"));
        Assert.Equal("важно", MarkdownText.ToPlainText("_важно_"));
        Assert.Equal("устарело", MarkdownText.ToPlainText("~~устарело~~"));
    }

    [Fact]
    public void CodeLinksHeadingsAndQuotesAreFlattened()
    {
        Assert.Equal("Запусти build.ps1", MarkdownText.ToPlainText("Запусти `build.ps1`"));
        Assert.Equal("документация", MarkdownText.ToPlainText("[документация](https://example.com)"));
        Assert.Equal("Заголовок", MarkdownText.ToPlainText("## Заголовок"));
        Assert.Equal("заметка", MarkdownText.ToPlainText("> заметка"));
    }

    [Fact]
    public void BulletsAndCodeFencesAreCleaned()
    {
        string input = "- первый\n- второй";
        Assert.Equal("• первый\n• второй", MarkdownText.ToPlainText(input));

        Assert.Equal("код", MarkdownText.ToPlainText("```csharp\nкод\n```"));
    }

    [Fact]
    public void UnderscoresInIdentifiersAreKept()
    {
        const string input = "поля _needMessages и _messagesLoadGeneration важны";

        Assert.Equal(input, MarkdownText.ToPlainText(input));
    }

    [Fact]
    public void PlainTextIsReturnedUnchanged()
    {
        const string input = "Просто ответ агента без разметки.";

        Assert.Equal(input, MarkdownText.ToPlainText(input));
    }

    [Fact]
    public void EmptyAndNullBecomeEmptyString()
    {
        Assert.Equal("", MarkdownText.ToPlainText(null));
        Assert.Equal("", MarkdownText.ToPlainText(""));
    }
}
