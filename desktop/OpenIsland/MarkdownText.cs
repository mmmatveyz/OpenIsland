using System.Text.RegularExpressions;

namespace OpenIsland;

/// <summary>
/// Преобразует markdown-ответ агента в читаемый простой текст. Карточка
/// OpenCode рисует сообщения обычным <c>TextBlock</c> без разметки, поэтому
/// маркеры <c>**жирного**</c>, курсива, кода, ссылок и списков убираются,
/// а сам текст остаётся нетронутым. Живёт отдельно как чистая логика и
/// покрывается юнит-тестами.
/// </summary>
internal static partial class MarkdownText
{
    public static string ToPlainText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";

        string value = text.Replace("\r\n", "\n").Replace('\r', '\n');
        value = FenceLine().Replace(value, "");     // ``` и ```язык
        value = Bullet().Replace(value, "$1• ");    // "- item" -> "• item"
        value = Image().Replace(value, "$1");       // ![alt](url) -> alt
        value = Link().Replace(value, "$1");        // [текст](url) -> текст
        value = Heading().Replace(value, "");       // "## Заголовок" -> "Заголовок"
        value = Quote().Replace(value, "");         // "> цитата" -> "цитата"
        value = InlineCode().Replace(value, "$1");  // `код` -> код
        value = BoldStar().Replace(value, "$1");    // **жирный** -> жирный
        value = BoldUnderscore().Replace(value, "$1");
        value = Strike().Replace(value, "$1");      // ~~зачёркнутый~~ -> зачёркнутый
        value = ItalicStar().Replace(value, "$1");  // *курсив* -> курсив
        value = ItalicUnderscore().Replace(value, "$1");
        value = ExtraBlankLines().Replace(value, "\n\n");
        return value.Trim();
    }

    [GeneratedRegex(@"(?m)^\s*```.*$")]
    private static partial Regex FenceLine();

    [GeneratedRegex(@"(?m)^(\s*)[-*+]\s+")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"!\[([^\]\n]*)\]\([^)\n]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]\n]*)\]\([^)\n]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"(?m)^\s{0,3}#{1,6}\s*")]
    private static partial Regex Heading();

    [GeneratedRegex(@"(?m)^\s{0,3}>\s?")]
    private static partial Regex Quote();

    [GeneratedRegex(@"`([^`\n]+)`")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"\*\*(?!\s)([^*\n]+?)\*\*")]
    private static partial Regex BoldStar();

    [GeneratedRegex(@"(?<!\w)__(?!\s)([^_\n]+?)__(?!\w)")]
    private static partial Regex BoldUnderscore();

    [GeneratedRegex(@"~~(?!\s)([^~\n]+?)~~")]
    private static partial Regex Strike();

    // Одинарные * и _ как курсив, но без риска съесть идентификаторы
    // вида _messagesLoadGeneration (после закрывающего маркера не должно
    // идти слово).
    [GeneratedRegex(@"\*(?!\s)([^*\n]+?)(?<!\s)\*")]
    private static partial Regex ItalicStar();

    [GeneratedRegex(@"(?<!\w)_(?!\s)([^_\n]+?)(?<!\s)_(?!\w)")]
    private static partial Regex ItalicUnderscore();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExtraBlankLines();
}
