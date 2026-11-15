using System.Windows;
using System.Windows.Media;

namespace OpenIsland;

public sealed class LineBar : FrameworkElement
{
    const double UnplayedOpacity = 0.2;
    const double SampleStep = 4;
    const double MinThickness = 2, RestThickness = 6;
    const double BulgeThickness = 8, BulgeReach = 28;
    const double WaveThickness = 10, WaveSpeed = 800;

    sealed class Column(double left, double right)
    {
        public double Left { get; } = left;
        public double Right { get; } = right;
        public double Center => (Left + Right) / 2;
        public Spring Rise { get; } = new(RestThickness);
        public bool Raised { get; set; }
    }

    readonly record struct Ripple(double At, double From, bool Swelling);

    readonly List<Column> _columns = [];
    readonly List<Ripple> _ripples = [];

    double _fill, _thickness = RestThickness;
    double _columnsWidth = -1;
    bool _columnsStale = true;
    SeekHover _hover = SeekHover.Magnifier;
    double _time, _lastX;
    bool _pointed, _swollen;

    public double Fill
    {
        get => _fill;
        set
        {
            if (value == _fill) return;
            _fill = value;
            InvalidateVisual();
        }
    }

    public double Thickness
    {
        get => _thickness;
        set
        {
            if (value == _thickness) return;
            _thickness = value;
            InvalidateVisual();
        }
    }

    internal void Advance(double dt, double? pointer, SeekHover hover)
    {
        double width = ActualWidth;
        if (width <= 0) return;

        if (_columnsStale || width != _columnsWidth) BuildColumns(width);
        if (hover != _hover)
        {
            _hover = hover;
            _ripples.Clear();
            _pointed = _swollen = false;
            foreach (Column column in _columns)
            {
                column.Raised = false;
                TuneRise(column);
            }
        }

        _time += dt;
        bool pointed = pointer.HasValue;
        double x = Math.Clamp(pointer ?? _lastX, 0, width);
        if (pointed != _pointed)
        {
            _pointed = pointed;
            _ripples.Add(new Ripple(_time, x, pointed));
        }
        if (pointed) _lastX = x;

        foreach (Column column in _columns)
        {
            column.Rise.Target = RiseFor(column, x);
            column.Rise.Advance(dt);
        }
        ForgetPassedRipples(width);

        InvalidateVisual();
    }

    void BuildColumns(double width)
    {
        _columns.Clear();
        _columnsWidth = width;
        _columnsStale = false;
        int count = Math.Max(1, (int)Math.Ceiling(width / SampleStep));
        for (int i = 0; i < count; i++) _columns.Add(new Column(width * i / count, width * (i + 1) / count));
        foreach (Column column in _columns)
        {
            column.Raised = _swollen;
            TuneRise(column);
        }
    }

    void ForgetPassedRipples(double width)
    {
        while (_ripples.Count > 0 && _time - _ripples[0].At >= width / WaveSpeed)
        {
            _swollen = _ripples[0].Swelling;
            _ripples.RemoveAt(0);
        }
    }

    void TuneRise(Column column)
    {
        if (_hover == SeekHover.Wave) column.Rise.Tune(520, 20);
        else column.Rise.Tune(420, 26);
    }

    int ColumnAt(double x)
    {
        for (int i = 0; i < _columns.Count; i++)
            if (x < _columns[i].Right) return i;
        return _columns.Count - 1;
    }

    double RiseFor(Column column, double x) => _hover switch
    {
        SeekHover.Wave => Raise(column) ? WaveThickness : RestThickness,
        _ => _pointed ? RestThickness + BulgeThickness * Math.Exp(-Math.Pow((column.Center - x) / BulgeReach, 2)) : RestThickness,
    };

    bool Raise(Column column)
    {
        for (int i = _ripples.Count - 1; i >= 0; i--)
        {
            if (_time - _ripples[i].At < Math.Abs(column.Center - _ripples[i].From) / WaveSpeed) continue;
            column.Raised = _ripples[i].Swelling;
            break;
        }
        return column.Raised;
    }

    double ThicknessOf(Column column) => Math.Max(MinThickness, Math.Max(_thickness, column.Rise.Value));

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth, height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        if (_columnsStale || width != _columnsWidth) BuildColumns(width);

        double middle = height / 2, fill = Math.Clamp(_fill, 0, width);
        Geometry shape = SolidShape(width, middle);
        dc.DrawGeometry(Shades.White(UnplayedOpacity), null, shape);
        if (fill <= 0) return;

        double radius = Math.Min(ThicknessOf(_columns[ColumnAt(fill)]) / 2, width / 2);
        var reach = new CombinedGeometry(GeometryCombineMode.Union,
            new RectangleGeometry(new Rect(0, 0, Math.Max(0, fill - radius), height)),
            new EllipseGeometry(new Point(fill - radius, middle), radius, radius));
        dc.PushClip(reach);
        dc.DrawGeometry(Brushes.White, null, shape);
        dc.Pop();
    }

    Geometry SolidShape(double width, double middle)
    {
        double left = Math.Min(ThicknessOf(_columns[0]) / 2, width / 2);
        double right = Math.Min(ThicknessOf(_columns[^1]) / 2, width / 2);
        var shape = new StreamGeometry();
        using (StreamGeometryContext outline = shape.Open())
        {
            outline.BeginFigure(new Point(left, middle - left), true, true);
            foreach (Column column in _columns)
                if (column.Center > left && column.Center < width - right)
                    outline.LineTo(new Point(column.Center, middle - ThicknessOf(column) / 2), true, true);
            outline.LineTo(new Point(width - right, middle - right), true, true);
            outline.ArcTo(new Point(width - right, middle + right), new Size(right, right), 0, false, SweepDirection.Clockwise, true, true);
            for (int i = _columns.Count - 1; i >= 0; i--)
                if (_columns[i].Center > left && _columns[i].Center < width - right)
                    outline.LineTo(new Point(_columns[i].Center, middle + ThicknessOf(_columns[i]) / 2), true, true);
            outline.LineTo(new Point(left, middle + left), true, true);
            outline.ArcTo(new Point(left, middle - left), new Size(left, left), 0, false, SweepDirection.Clockwise, true, true);
        }
        shape.Freeze();
        return shape;
    }
}
