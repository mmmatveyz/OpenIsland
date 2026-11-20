using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OpenIsland;

/// <summary>
/// Экранная пипетка: полноэкранный прозрачный оверлей с лупой/превью цвета пикселя под курсором,
/// копированием #RRGGBB в буфер по ЛКМ и отменой по Esc/ПКМ.
/// </summary>
public sealed class ColorPickerWindow : Window
{
    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X;
        public int Y;
    }

    static ColorPickerWindow? _activeInstance;

    readonly Action<Color, string, string>? _onPicked;
    readonly Canvas _canvas;
    readonly Border _loupeCard;
    readonly Border _colorCircle;
    readonly TextBlock _hexBlock;
    readonly TextBlock _rgbBlock;

    Color _currentColor = Colors.Black;
    string _currentHex = "#000000";
    string _currentRgb = "rgb(0, 0, 0)";

    public static bool IsOpen => _activeInstance != null;

    public static void Start(Action<Color, string, string>? onPicked = null)
    {
        if (_activeInstance != null)
        {
            _activeInstance.Activate();
            return;
        }

        var picker = new ColorPickerWindow(onPicked);
        _activeInstance = picker;
        picker.Show();
        picker.Activate();
    }

    ColorPickerWindow(Action<Color, string, string>? onPicked)
    {
        _onPicked = onPicked;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        // Почти прозрачный фон (альфа 1), чтобы ловить мышь по всей площади экрана
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        _canvas = new Canvas { IsHitTestVisible = false };

        // Карточка-лупа возле курсора
        _colorCircle = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
            Margin = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Black,
        };

        _hexBlock = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Text = "#000000",
        };

        _rgbBlock = new TextBlock
        {
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(142, 142, 147)),
            Text = "rgb(0, 0, 0)",
            Margin = new Thickness(0, 1, 0, 0),
        };

        var hintBlock = new TextBlock
        {
            FontSize = 8.5,
            Foreground = new SolidColorBrush(Color.FromRgb(99, 99, 102)),
            Text = "ЛКМ: копия \u00B7 Esc: отмена",
            Margin = new Thickness(0, 2, 0, 0),
        };

        var textPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        textPanel.Children.Add(_hexBlock);
        textPanel.Children.Add(_rgbBlock);
        textPanel.Children.Add(hintBlock);

        var loupeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        loupeRow.Children.Add(_colorCircle);
        loupeRow.Children.Add(textPanel);

        _loupeCard = new Border
        {
            Width = 152,
            Height = 58,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(240, 28, 28, 30)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(200, 58, 58, 60)),
            Child = loupeRow,
        };

        _canvas.Children.Add(_loupeCard);
        Content = _canvas;

        Loaded += (_, _) => UpdateColorUnderCursor();
        MouseMove += ColorPicker_MouseMove;
        MouseDown += ColorPicker_MouseDown;
        KeyDown += ColorPicker_KeyDown;
        Deactivated += (_, _) => ClosePicker();
        Closed += (_, _) => _activeInstance = null;
    }

    void ColorPicker_MouseMove(object sender, MouseEventArgs e)
    {
        Point windowPos = e.GetPosition(this);
        UpdateColorUnderCursor();
        PositionLoupe(windowPos);
    }

    void PositionLoupe(Point windowPos)
    {
        const double offsetX = 20, offsetY = 20;
        double targetX = windowPos.X + offsetX;
        double targetY = windowPos.Y + offsetY;

        // Если лупа выходит за правый край окна, сдвигаем её влево от курсора
        if (targetX + _loupeCard.Width > ActualWidth - 10)
            targetX = windowPos.X - _loupeCard.Width - offsetX;

        // Если лупа выходит за нижний край, сдвигаем её вверх
        if (targetY + _loupeCard.Height > ActualHeight - 10)
            targetY = windowPos.Y - _loupeCard.Height - offsetY;

        Canvas.SetLeft(_loupeCard, Math.Max(10, targetX));
        Canvas.SetTop(_loupeCard, Math.Max(10, targetY));
    }

    void UpdateColorUnderCursor()
    {
        if (!GetCursorPos(out POINT pt)) return;

        IntPtr hdc = GetDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero) return;

        try
        {
            uint colorRef = GetPixel(hdc, pt.X, pt.Y);
            var (r, g, b) = ColorFormat.FromColorRef(colorRef);

            _currentColor = Color.FromRgb(r, g, b);
            _currentHex = ColorFormat.FormatHex(r, g, b);
            _currentRgb = ColorFormat.FormatRgb(r, g, b);

            _colorCircle.Background = new SolidColorBrush(_currentColor);
            _hexBlock.Text = _currentHex;
            _rgbBlock.Text = _currentRgb;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }
    }

    void ColorPicker_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            UpdateColorUnderCursor();
            try
            {
                Clipboard.SetText(_currentHex);
            }
            catch (Exception ex)
            {
                App.LogDebug($"ColorPicker: failed to set clipboard: {ex.Message}");
            }

            var pickedColor = _currentColor;
            var pickedHex = _currentHex;
            var pickedRgb = _currentRgb;

            ClosePicker();
            _onPicked?.Invoke(pickedColor, pickedHex, pickedRgb);
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            ClosePicker();
        }
    }

    void ColorPicker_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ClosePicker();
        }
    }

    void ClosePicker()
    {
        try
        {
            Close();
        }
        catch
        {
            // Окно уже закрыто
        }
    }
}
