using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

/// <summary>
/// Логика истории буфера обмена проверяется через <see cref="ClipboardService.Add"/>,
/// поэтому тесты не трогают системный буфер (и не зависят от того, занят он или нет).
/// </summary>
public class ClipboardServiceTests
{
    [Fact]
    public void Add_PutsNewFragmentOnTop()
    {
        var service = new ClipboardService();

        service.Add("первый");
        service.Add("второй");

        Assert.Equal(["второй", "первый"], service.Items.Select(entry => entry.Text).ToArray());
    }

    [Fact]
    public void Add_MovesDuplicateToTopWithoutDuplicating()
    {
        var service = new ClipboardService();
        service.Add("a");
        service.Add("b");
        service.Add("a");

        Assert.Equal(["a", "b"], service.Items.Select(entry => entry.Text).ToArray());
    }

    [Fact]
    public void Add_IgnoresEmptyAndWhitespaceFragments()
    {
        var service = new ClipboardService();

        service.Add("");
        service.Add("   ");
        service.Add("\n\t ");

        Assert.Empty(service.Items);
    }

    [Fact]
    public void Add_KeepsOnlyNewestThirty()
    {
        var service = new ClipboardService();

        for (int i = 1; i <= 35; i++) service.Add($"запись {i}");

        Assert.Equal(30, service.Items.Count);
        Assert.Equal("запись 35", service.Items[0].Text);
        Assert.Equal("запись 6", service.Items[^1].Text);
    }

    [Fact]
    public void Add_TrimsLongFragmentToStoredLimit()
    {
        var service = new ClipboardService();

        service.Add(new string('я', 25_000));

        Assert.Equal(20_000, service.Items[0].Text.Length);
        Assert.Equal('я', service.Items[0].Text[^1]);
    }

    [Fact]
    public void Add_RaisesChangedOnlyForAcceptedFragments()
    {
        var service = new ClipboardService();
        int raised = 0;
        service.Changed += () => raised++;

        service.Add("раз");
        service.Add("раз"); // дубликат на самом верху — история не меняется
        service.Add("два");

        Assert.Equal(2, raised);
    }

    [Fact]
    public void Clear_DropsHistoryAndRaisesChanged()
    {
        var service = new ClipboardService();
        service.Add("фрагмент");
        int raised = 0;
        service.Changed += () => raised++;

        service.Clear();

        Assert.Empty(service.Items);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Clear_OnEmptyHistoryDoesNothing()
    {
        var service = new ClipboardService();
        int raised = 0;
        service.Changed += () => raised++;

        service.Clear();

        Assert.Equal(0, raised);
    }
}
