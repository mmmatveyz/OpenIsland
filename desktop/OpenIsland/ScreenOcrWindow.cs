using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace OpenIsland;

/// <summary>
/// Быстрый снимок экрана с OCR: полупрозрачный оверлей с выделением области рамкой,
/// захват пикселей через BitBlt и распознавание текста через локальный WinRT OcrEngine.
/// </summary>
public sealed class ScreenOcrWindow : Window
{
    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

    [DllImport("gdi32.dll")]
    static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, [Out] byte[] lpvBits, ref BITMAPINFO lpbi, uint uUsage);

    const int SRCCOPY = 0x00CC0020;
    const uint DIB_RGB_COLORS = 0;
    const uint BI_RGB = 0;

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }

    static ScreenOcrWindow? _activeInstance;

    readonly Action<string, string>? _onRecognized;
    readonly Action? _onNoText;
    readonly Canvas _canvas;
    readonly Border _selectionBox;
    readonly Border _dimBadge;
    readonly TextBlock _dimText;
    readonly Border _hintBadge;

    bool _isDragging;
    Point _startPoint;

    public static bool IsOpen => _activeInstance != null;

    public static void Start(Action<string, string>? onRecognized = null, Action? onNoText = null)
    {
        if (_activeInstance != null)
        {
            _activeInstance.Activate();
            return;
        }

        var ocr = new ScreenOcrWindow(onRecognized, onNoText);
        _activeInstance = ocr;
        ocr.Show();
        ocr.Activate();
    }

    ScreenOcrWindow(Action<string, string>? onRecognized, Action? onNoText)
    {
        _onRecognized = onRecognized;
        _onNoText = onNoText;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        // Затемняющий оверлей для комфортного выделения
        Background = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        _canvas = new Canvas { IsHitTestVisible = true };

        // Рамка выделения
        _selectionBox = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(125, 122, 255)),
            BorderThickness = new Thickness(1.5),
            Background = new SolidColorBrush(Color.FromArgb(38, 125, 122, 255)),
            CornerRadius = new CornerRadius(2),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };

        // Бейдж размеров возле рамки
        _dimText = new TextBlock
        {
            FontSize = 10,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
        };

        _dimBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(200, 28, 28, 30)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 2, 5, 2),
            Child = _dimText,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };

        // Подсказка вверху по центру экрана
        var hintText = new TextBlock
        {
            Text = "Выделите область с текстом \u00B7 Esc: отмена",
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = Brushes.White,
        };

        _hintBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 28, 28, 30)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(160, 58, 58, 60)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 6, 14, 6),
            Child = hintText,
            IsHitTestVisible = false,
        };

        _canvas.Children.Add(_selectionBox);
        _canvas.Children.Add(_dimBadge);
        _canvas.Children.Add(_hintBadge);
        Content = _canvas;

        Loaded += (_, _) =>
        {
            Canvas.SetLeft(_hintBadge, Math.Max(20, (ActualWidth - 260) / 2));
            Canvas.SetTop(_hintBadge, 30);
        };

        MouseDown += ScreenOcr_MouseDown;
        MouseMove += ScreenOcr_MouseMove;
        MouseUp += ScreenOcr_MouseUp;
        KeyDown += ScreenOcr_KeyDown;
        Deactivated += (_, _) => CloseOcr();
        Closed += (_, _) => _activeInstance = null;
    }

    void ScreenOcr_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _startPoint = e.GetPosition(this);
            _isDragging = true;
            _selectionBox.Width = 0;
            _selectionBox.Height = 0;
            _selectionBox.Visibility = Visibility.Visible;
            _dimBadge.Visibility = Visibility.Visible;
            _hintBadge.Visibility = Visibility.Collapsed;
            CaptureMouse();
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            CloseOcr();
        }
    }

    void ScreenOcr_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        Point current = e.GetPosition(this);
        double x = Math.Min(_startPoint.X, current.X);
        double y = Math.Min(_startPoint.Y, current.Y);
        double w = Math.Abs(current.X - _startPoint.X);
        double h = Math.Abs(current.Y - _startPoint.Y);

        Canvas.SetLeft(_selectionBox, x);
        Canvas.SetTop(_selectionBox, y);
        _selectionBox.Width = w;
        _selectionBox.Height = h;

        _dimText.Text = $"{(int)w} \u00D7 {(int)h}";
        Canvas.SetLeft(_dimBadge, x + w - 40);
        Canvas.SetTop(_dimBadge, y + h + 6);
    }

    void ScreenOcr_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging || e.ChangedButton != MouseButton.Left) return;
        _isDragging = false;
        ReleaseMouseCapture();

        Point current = e.GetPosition(this);
        double dipX = Math.Min(_startPoint.X, current.X);
        double dipY = Math.Min(_startPoint.Y, current.Y);
        double dipW = Math.Abs(current.X - _startPoint.X);
        double dipH = Math.Abs(current.Y - _startPoint.Y);

        if (dipW < 8 || dipH < 8)
        {
            CloseOcr();
            return;
        }

        // Переводим координаты WPF DIP в физические пиксели экрана
        Point screenTopLeft = PointToScreen(new Point(dipX, dipY));
        Point screenBottomRight = PointToScreen(new Point(dipX + dipW, dipY + dipH));

        int physX = (int)Math.Round(screenTopLeft.X);
        int physY = (int)Math.Round(screenTopLeft.Y);
        int physW = (int)Math.Round(screenBottomRight.X - screenTopLeft.X);
        int physH = (int)Math.Round(screenBottomRight.Y - screenTopLeft.Y);

        // Закрываем оверлей ДО захвата экрана, чтобы сама рамка не попала в OCR
        CloseOcr();

        _ = Task.Run(async () =>
        {
            try
            {
                byte[]? pixels = CaptureScreenRegionBgra(physX, physY, physW, physH);
                if (pixels == null || pixels.Length == 0)
                {
                    Dispatcher.Invoke(() => _onNoText?.Invoke());
                    return;
                }

                var buffer = CryptographicBuffer.CreateFromByteArray(pixels);
                var softwareBitmap = SoftwareBitmap.CreateCopyFromBuffer(
                    buffer,
                    BitmapPixelFormat.Bgra8,
                    physW,
                    physH,
                    BitmapAlphaMode.Premultiplied);

                OcrEngine? engine = OcrEngine.TryCreateFromUserProfileLanguages()
                    ?? (OcrEngine.AvailableRecognizerLanguages.Count > 0
                        ? OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages[0])
                        : null);

                if (engine == null)
                {
                    Dispatcher.Invoke(() => _onNoText?.Invoke());
                    return;
                }

                OcrResult result = await engine.RecognizeAsync(softwareBitmap);
                var lines = result.Lines.Select(l => l.Text);
                string text = OcrResultFormatter.FormatText(lines);

                Dispatcher.Invoke(() =>
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        _onNoText?.Invoke();
                        return;
                    }

                    try
                    {
                        Clipboard.SetText(text);
                    }
                    catch (Exception ex)
                    {
                        App.LogDebug($"ScreenOcr: clipboard set failed: {ex.Message}");
                    }

                    string preview = OcrResultFormatter.FormatPreview(text, 60);
                    _onRecognized?.Invoke(text, preview);
                });
            }
            catch (Exception ex)
            {
                App.LogDebug($"ScreenOcr: recognize error: {ex.Message}");
                Dispatcher.Invoke(() => _onNoText?.Invoke());
            }
        });
    }

    void ScreenOcr_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseOcr();
        }
    }

    static byte[]? CaptureScreenRegionBgra(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;

        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;

        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr bitmap = CreateCompatibleBitmap(screenDc, width, height);
        IntPtr oldBitmap = SelectObject(memDc, bitmap);

        try
        {
            if (!BitBlt(memDc, 0, 0, width, height, screenDc, x, y, SRCCOPY))
                return null;

            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    // Отрицательная высота для формата top-down DIB (сверху вниз)
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BI_RGB,
                    biSizeImage = (uint)(width * height * 4),
                }
            };

            byte[] pixelBytes = new byte[width * height * 4];
            int scanLines = GetDIBits(memDc, bitmap, 0, (uint)height, pixelBytes, ref bmi, DIB_RGB_COLORS);
            if (scanLines == 0) return null;

            return pixelBytes;
        }
        finally
        {
            SelectObject(memDc, oldBitmap);
            DeleteObject(bitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    void CloseOcr()
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
