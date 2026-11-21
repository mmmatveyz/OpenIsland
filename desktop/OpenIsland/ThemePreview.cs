using System.Windows;
using System.Windows.Media;

namespace OpenIsland;

public sealed class ThemePreview : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(ThemePreset), typeof(ThemePreview),
        new FrameworkPropertyMetadata(ThemePreset.Default, FrameworkPropertyMetadataOptions.AffectsRender));

    public ThemePreset Kind
    {
        get => (ThemePreset)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        bool isLight = ThemeService.EffectiveThemeMode == ThemeMode.Light;
        ThemePalette p = ThemeService.Resolve(isLight ? ThemeMode.Light : ThemeMode.Dark, Kind);

        var outerRect = new Rect(2, 2, Math.Max(w - 4, 1), Math.Max(h - 4, 1));
        dc.DrawRoundedRectangle(new SolidColorBrush(p.CardBackground), new Pen(new SolidColorBrush(p.CardBorder), 1), outerRect, 6, 6);

        // Draw miniature inner card
        double pillW = outerRect.Width * 0.68;
        double pillH = outerRect.Height * 0.36;
        var pillRect = new Rect(outerRect.Left + (outerRect.Width - pillW) / 2, outerRect.Top + 3.5, pillW, pillH);
        dc.DrawRoundedRectangle(new SolidColorBrush(p.CardSubBackground), null, pillRect, 3, 3);

        // Accent indicator dot
        Color dotColor = Kind switch
        {
            ThemePreset.Nord => Color.FromRgb(0x88, 0xC0, 0xD0),
            ThemePreset.Cyberpunk => Color.FromRgb(0xFF, 0x71, 0xCE),
            ThemePreset.Oled => isLight ? Color.FromRgb(0x11, 0x11, 0x12) : Colors.White,
            _ => isLight ? Color.FromRgb(0x7D, 0x7A, 0xFF) : Colors.White
        };
        dc.DrawEllipse(new SolidColorBrush(dotColor), null,
            new Point(outerRect.Left + outerRect.Width / 2, outerRect.Bottom - 6.5), 2.2, 2.2);
    }
}
