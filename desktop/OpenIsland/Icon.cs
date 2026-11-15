using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace OpenIsland;

public enum Glyph
{
    Mute, Quiet, Mid, Loud, Headphones, Speaker, Vpn, Offline, Wifi, Wired, Bell, Note, Battery, Minus, Plus, Chevron, Back,
    Timer, Gear, Lines, Quote, Sparkle, Rim, Expand, Windows, Power, Look, Size, Gap, Drop, Moon, Tray, Cross, Bolt, VpnOff, Bars, Update,
    Segments, Stars, Pointer, Record, Folder, Bulge, Sun, Cloud, CloudSun, CloudMoon, Rain, Snow, Storm, Fog, Qr, Pin, Location, Search,
    Check, PointerText, Parting,
}

public sealed class Icon : FrameworkElement
{
    const double GridSize = 24;
    const double SymbolEm = 20;
    const double WidestSymbol = GridSize;
    const double Tolerance = 0.01;
    const double PieceSeconds = 0.16;
    const double PieceStagger = 0.06;
    const double Absent = 0.001, Complete = 0.999;
    const double SweepReach = GridSize * 4;
    const string SymbolFont = "./Fonts/#Phosphor-Fill";
    static readonly FontWeight SymbolWeight = FontWeights.Normal;
    static readonly Vector Across = new(1, 0);

    readonly record struct Symbol(string Name, int Code);

    sealed record Piece(Geometry Shape, Geometry Cut, Vector Sweep)
    {
        public bool Drawn => Sweep != default;
    }

    struct Stage
    {
        public double Shown, From, To, Start;
    }

    static readonly GlyphTypeface? Face = LoadFace();

    static readonly Symbol SpeakerWaves = new("speaker waves", 0);
    static readonly Symbol SpeakerSlash = new("speaker slash", 0);
    static readonly Symbol WifiArcs = new("wifi arcs", 0);
    static readonly Symbol WifiSlash = new("wifi slash", 0);
    static readonly Symbol Shield = new("shield", 0xE40A);
    static readonly Symbol CheckedShield = new("shield-check", 0xE40C);

    // Волны громкости и wifi рисуются собственными контурами (не из шрифта): так сохраняется
    // пошаговая анимация волн и не остаётся зависимости от конкретного шрифта-иконника.
    static readonly Dictionary<Symbol, Geometry> Custom = new()
    {
        [SpeakerWaves] = SpeakerWaveGeometry(),
        [SpeakerSlash] = Stroke(new Point(4, 2.8), new Point(24, 21.2), 1.8),
        [WifiArcs] = WifiArcGeometry(),
        [WifiSlash] = Stroke(new Point(4.5, 4.5), new Point(19.5, 19.5), 1.7),
    };

    static readonly Piece[] SoundPieces = BuildSoundPieces();
    static readonly Piece[] NetworkPieces = BuildNetworkPieces();
    static readonly Piece[] ShieldPieces = BuildShieldPieces();

    static readonly Dictionary<Glyph, (Piece[] Pieces, int Mask)> Composite = new()
    {
        [Glyph.Mute] = (SoundPieces, 0b10001),
        [Glyph.Quiet] = (SoundPieces, 0b00011),
        [Glyph.Mid] = (SoundPieces, 0b00111),
        [Glyph.Loud] = (SoundPieces, 0b01111),
        [Glyph.Wifi] = (NetworkPieces, 0b0111),
        [Glyph.Offline] = (NetworkPieces, 0b1111),
        [Glyph.Vpn] = (ShieldPieces, 0b11),
        [Glyph.VpnOff] = (ShieldPieces, 0b01),
    };

    static readonly Dictionary<Glyph, Symbol> Symbols = new()
    {
        [Glyph.Headphones] = new("headphones", 0xE2A6),
        [Glyph.Speaker] = new("speaker-hifi", 0xEA08),
        [Glyph.Wired] = new("plug", 0xE946),
        [Glyph.Bell] = new("bell", 0xE0CE),
        [Glyph.Note] = new("music-note", 0xE33C),
        [Glyph.Battery] = new("battery-full", 0xE0C0),
        [Glyph.Minus] = new("minus", 0xE32A),
        [Glyph.Plus] = new("plus", 0xE3D4),
        [Glyph.Chevron] = new("caret-right", 0xE13A),
        [Glyph.Back] = new("caret-left", 0xE138),
        [Glyph.Timer] = new("timer", 0xE492),
        [Glyph.Gear] = new("gear-six", 0xE272),
        [Glyph.Lines] = new("text-align-left", 0xE484),
        [Glyph.Quote] = new("quotes", 0xE660),
        [Glyph.Sparkle] = new("sparkle", 0xE6A2),
        [Glyph.Rim] = new("pill", 0xE700),
        [Glyph.Expand] = new("corners-out", 0xE1D0),
        [Glyph.Windows] = new("squares-four", 0xE464),
        [Glyph.Update] = new("download-simple", 0xE20C),
        [Glyph.Power] = new("power", 0xE3DA),
        [Glyph.Look] = new("circle-half", 0xE18C),
        [Glyph.Size] = new("arrows-out", 0xE0A2),
        [Glyph.Gap] = new("arrows-out-line-vertical", 0xE536),
        [Glyph.Drop] = new("drop", 0xE210),
        [Glyph.Moon] = new("moon", 0xE330),
        [Glyph.Tray] = new("tray", 0xE4AA),
        [Glyph.Cross] = new("x", 0xE4F6),
        [Glyph.Bolt] = new("lightning", 0xE2DE),
        [Glyph.Bars] = new("waveform", 0xE802),
        [Glyph.Segments] = new("rows", 0xE5A2),
        [Glyph.Stars] = new("star-four", 0xE6A4),
        [Glyph.Pointer] = new("cursor", 0xE1DC),
        [Glyph.PointerText] = new("cursor-text", 0xE7D8),
        [Glyph.Parting] = new("text-t", 0xE48A),
        [Glyph.Bulge] = new("cursor-click", 0xE7C8),
        [Glyph.Record] = new("record", 0xE3EE),
        [Glyph.Folder] = new("folder", 0xE24A),
        [Glyph.Sun] = new("sun", 0xE472),
        [Glyph.Cloud] = new("cloud", 0xE1AA),
        [Glyph.CloudSun] = new("cloud-sun", 0xE540),
        [Glyph.CloudMoon] = new("cloud-moon", 0xE53E),
        [Glyph.Rain] = new("cloud-rain", 0xE1B4),
        [Glyph.Snow] = new("cloud-snow", 0xE1B8),
        [Glyph.Storm] = new("cloud-lightning", 0xE1B2),
        [Glyph.Fog] = new("cloud-fog", 0xE53C),
        [Glyph.Qr] = new("qr-code", 0xE3E6),
        [Glyph.Pin] = new("map-pin", 0xE316),
        [Glyph.Location] = new("map-pin-area", 0xEE3A),
        [Glyph.Search] = new("magnifying-glass", 0xE30C),
        [Glyph.Check] = new("check", 0xE182),
    };

    static readonly Dictionary<Glyph, Geometry> Outlines = [];
    static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(Glyph), typeof(Icon),
        new FrameworkPropertyMetadata(Glyph.Note, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Icon)d).OnKindChanged()));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(Icon),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    readonly FrameLoop _loop;
    Piece[]? _pieces;
    Stage[] _stages = [];

    public Icon() => _loop = new FrameLoop(_ => Advance());

    public Glyph Kind
    {
        get => (Glyph)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public void Play(Glyph? from, double wait)
    {
        if (_pieces == null || !Composite.TryGetValue(Kind, out var composite)) return;
        int had = from is { } other && Composite.TryGetValue(other, out var before) && before.Pieces == composite.Pieces ? before.Mask : 0;
        for (int i = 0; i < _stages.Length; i++) _stages[i].Shown = Bit(had, i);
        Animate(composite.Mask, wait);
    }

    void OnKindChanged()
    {
        if (!Composite.TryGetValue(Kind, out var composite))
        {
            _pieces = null;
            _loop.Stop();
            return;
        }

        if (composite.Pieces == _pieces && IsVisible)
        {
            Animate(composite.Mask, 0);
            return;
        }
        _pieces = composite.Pieces;
        _stages = new Stage[_pieces.Length];
        for (int i = 0; i < _stages.Length; i++) _stages[i].Shown = Bit(composite.Mask, i);
        _loop.Stop();
    }

    void Animate(int mask, double wait)
    {
        double at = Clock.Elapsed.TotalSeconds + wait;
        for (int i = _stages.Length - 1; i >= 0; i--)
            if (Bit(mask, i) == 0) at = Schedule(ref _stages[i], 0, at);
        for (int i = 0; i < _stages.Length; i++)
            if (Bit(mask, i) != 0) at = Schedule(ref _stages[i], 1, at);
        InvalidateVisual();
        _loop.Start();
    }

    static double Schedule(ref Stage stage, double to, double at)
    {
        stage.From = stage.Shown;
        stage.To = to;
        stage.Start = at;
        return Math.Abs(to - stage.Shown) > Absent ? at + PieceStagger : at;
    }

    bool Advance()
    {
        double now = Clock.Elapsed.TotalSeconds;
        bool moving = false;
        for (int i = 0; i < _stages.Length; i++)
        {
            ref Stage stage = ref _stages[i];
            double t = Math.Clamp((now - stage.Start) / PieceSeconds, 0, 1);
            stage.Shown = stage.From + (stage.To - stage.From) * t * t * (3 - 2 * t);
            moving |= t < 1;
        }
        InvalidateVisual();
        return moving;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        dc.PushTransform(new ScaleTransform(size / GridSize, size / GridSize));
        if (_loop.Running && _pieces != null) DrawPieces(dc, _pieces);
        else dc.DrawGeometry(Fill, null, Outline(Kind));
        dc.Pop();
        dc.Pop();
    }

    void DrawPieces(DrawingContext dc, Piece[] pieces)
    {
        var parts = new (Geometry? Shape, double Opacity)[pieces.Length];
        Geometry? cuts = null;
        for (int i = pieces.Length - 1; i >= 0; i--)
        {
            Piece piece = pieces[i];
            double shown = _stages[i].Shown;
            if (shown <= Absent) continue;

            double share = piece.Drawn ? shown : 1;
            Geometry shape = Revealed(piece, piece.Shape, share);
            if (cuts != null && !shape.IsEmpty()) shape = Exclude(shape, cuts);
            parts[i] = (shape, piece.Drawn ? 1 : shown);
            if (piece.Cut.IsEmpty()) continue;
            Geometry cut = Revealed(piece, piece.Cut, share);
            cuts = cuts == null ? cut : Union(cuts, cut);
        }

        foreach ((Geometry? shape, double opacity) in parts)
        {
            if (shape == null || shape.IsEmpty()) continue;
            dc.PushOpacity(opacity);
            dc.DrawGeometry(Fill, null, shape);
            dc.Pop();
        }
    }

    static Geometry Revealed(Piece piece, Geometry part, double share)
    {
        if (share >= Complete || part.IsEmpty()) return part;

        Rect bounds = Rect.Union(piece.Shape.Bounds, piece.Cut.Bounds);
        Vector along = piece.Sweep / piece.Sweep.Length, across = new(-along.Y, along.X);
        Point[] corners = [bounds.TopLeft, bounds.TopRight, bounds.BottomLeft, bounds.BottomRight];
        double start = corners.Min(corner => (Vector)corner * along), end = corners.Max(corner => (Vector)corner * along);
        double reach = start + (end - start) * Math.Max(share, 0);

        var front = new StreamGeometry();
        using (StreamGeometryContext g = front.Open())
        {
            g.BeginFigure((Point)(along * (start - 1) - across * SweepReach), true, true);
            g.LineTo((Point)(along * reach - across * SweepReach), false, false);
            g.LineTo((Point)(along * reach + across * SweepReach), false, false);
            g.LineTo((Point)(along * (start - 1) + across * SweepReach), false, false);
        }
        return Intersect(part, front);
    }

    static int Bit(int mask, int index) => mask >> index & 1;

    static GlyphTypeface? LoadFace()
    {
        var family = new FontFamily(new Uri("pack://application:,,,/"), SymbolFont);
        var typeface = new Typeface(family, FontStyles.Normal, SymbolWeight, FontStretches.Normal);
        return typeface.TryGetGlyphTypeface(out GlyphTypeface face) ? face : null;
    }

    static Geometry Outline(Symbol symbol)
    {
        if (Custom.TryGetValue(symbol, out Geometry? custom)) return custom;
        if (Face == null) return Geometry.Empty;
        if (Face.CharacterToGlyphMap.TryGetValue(symbol.Code, out ushort index)) return Face.GetGlyphOutline(index, SymbolEm, SymbolEm);

        App.Log(new KeyNotFoundException($"Icon glyph {symbol.Name} is missing from {SymbolFont}"));
        return Geometry.Empty;
    }

    static Geometry Outline(Glyph kind)
    {
        if (Outlines.TryGetValue(kind, out Geometry? outline)) return outline;
        outline = Composite.TryGetValue(kind, out var composite) ? Outline(composite.Pieces, composite.Mask) : Placed(Outline(Symbols[kind]));
        return Outlines[kind] = outline;
    }

    static Geometry Outline(Piece[] pieces, int mask)
    {
        Geometry shape = Geometry.Empty;
        for (int i = 0; i < pieces.Length; i++)
        {
            if (Bit(mask, i) == 0) continue;
            if (!pieces[i].Cut.IsEmpty()) shape = Exclude(shape, pieces[i].Cut);
            shape = Union(shape, pieces[i].Shape);
        }
        shape.Freeze();
        return shape;
    }

    static Geometry SpeakerWaveGeometry()
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        group.Children.Add(Cone());
        for (int i = 0; i < 3; i++) group.Children.Add(Arc(new Point(14.5, 12), 4 + i * 3, -58, 58, 1.6));
        group.Freeze();
        return group;
    }

    static Geometry WifiArcGeometry()
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (int i = 0; i < 3; i++) group.Children.Add(Arc(new Point(12, 19.5), 4.5 + i * 3, -122, -58, 1.6));
        group.Freeze();
        return group;
    }

    static Geometry Cone()
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(new Point(3, 9.5), true, true);
            path.LineTo(new Point(8.5, 9.5), true, false);
            path.LineTo(new Point(15, 3.5), true, false);
            path.LineTo(new Point(15, 20.5), true, false);
            path.LineTo(new Point(8.5, 14.5), true, false);
            path.LineTo(new Point(3, 14.5), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    static Geometry Stroke(Point from, Point to, double thickness)
    {
        var figure = new PathFigure { StartPoint = from, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new LineSegment(to, true));
        var path = new PathGeometry();
        path.Figures.Add(figure);
        return path.GetWidenedPathGeometry(RoundPen(thickness));
    }

    static Geometry Arc(Point center, double radius, double fromDeg, double toDeg, double thickness)
    {
        var figure = new PathFigure { StartPoint = OnCircle(center, radius, fromDeg), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(OnCircle(center, radius, toDeg), new Size(radius, radius), 0,
            Math.Abs(toDeg - fromDeg) > 180, SweepDirection.Clockwise, true));
        var path = new PathGeometry();
        path.Figures.Add(figure);
        return path.GetWidenedPathGeometry(RoundPen(thickness));
    }

    static Point OnCircle(Point center, double radius, double degrees) =>
        new(center.X + radius * Math.Cos(degrees * Math.PI / 180), center.Y + radius * Math.Sin(degrees * Math.PI / 180));

    static Pen RoundPen(double thickness) =>
        new(Brushes.Black, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

    static Piece[] BuildSoundPieces()
    {
        Geometry[] parts = Contours(Outline(SpeakerWaves), 4, bounds => bounds.Left);
        return Placed([Fading(parts[0]), Fading(parts[1]), Fading(parts[2]), Fading(parts[3]), Fading(Outline(SpeakerSlash))]);
    }

    static Piece[] BuildNetworkPieces()
    {
        Geometry arcs = Outline(WifiArcs);
        Geometry[] parts = Contours(arcs, 3, bounds => -bounds.Bottom);
        return Placed([Fading(parts[0]), Fading(parts[1]), Fading(parts[2]), Fading(Outline(WifiSlash))]);
    }

    static Piece[] BuildShieldPieces()
    {
        Geometry shield = Outline(Shield);
        return Placed([Fading(shield), Drawn(Outline(CheckedShield), shield, Across)]);
    }

    static Piece Fading(Geometry shape) => new(shape, Geometry.Empty, default);

    static Piece Drawn(Geometry whole, Geometry under, Vector sweep) => new(Exclude(whole, under), Exclude(under, whole), sweep);

    static Geometry[] Contours(Geometry outline, int count, Func<Rect, double> order)
    {
        PathGeometry path = PathGeometry.CreateFromGeometry(outline);
        Geometry[] contours = path.Figures
            .Select(figure => (Geometry)new PathGeometry([figure.Clone()], path.FillRule, path.Transform))
            .OrderBy(contour => order(contour.Bounds))
            .ToArray();
        if (contours.Length == count) return contours;

        var whole = new Geometry[count];
        Array.Fill(whole, Geometry.Empty);
        whole[0] = outline;
        return whole;
    }

    static Piece[] Placed(Piece[] pieces)
    {
        Rect bounds = Rect.Empty;
        foreach (Piece piece in pieces)
        {
            bounds.Union(piece.Shape.Bounds);
            bounds.Union(piece.Cut.Bounds);
        }
        Transform placement = Placement(bounds);
        return pieces.Select(piece => piece with { Shape = Placed(piece.Shape, placement), Cut = Placed(piece.Cut, placement) }).ToArray();
    }

    static Geometry Placed(Geometry shape) => Placed(shape, Placement(shape.Bounds));

    static Geometry Placed(Geometry shape, Transform placement)
    {
        if (shape.IsEmpty()) return Geometry.Empty;

        var placed = new GeometryGroup { FillRule = FillRule.Nonzero, Children = { shape }, Transform = placement };
        placed.Freeze();
        return placed;
    }

    static Transform Placement(Rect bounds)
    {
        if (bounds.IsEmpty) return Transform.Identity;

        double scale = Math.Min(1, WidestSymbol / Math.Max(bounds.Width, bounds.Height));
        Matrix placement = Matrix.Identity;
        placement.Translate(-(bounds.Left + bounds.Width / 2), -(bounds.Top + bounds.Height / 2));
        placement.Scale(scale, scale);
        placement.Translate(GridSize / 2, GridSize / 2);
        return new MatrixTransform(placement);
    }

    static Geometry Union(Geometry a, Geometry b) =>
        Geometry.Combine(a, b, GeometryCombineMode.Union, null, Tolerance, ToleranceType.Absolute);

    static Geometry Exclude(Geometry a, Geometry b) =>
        Geometry.Combine(a, b, GeometryCombineMode.Exclude, null, Tolerance, ToleranceType.Absolute);

    static Geometry Intersect(Geometry a, Geometry b) =>
        Geometry.Combine(a, b, GeometryCombineMode.Intersect, null, Tolerance, ToleranceType.Absolute);
}
