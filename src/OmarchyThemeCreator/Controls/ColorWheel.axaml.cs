using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace OmarchyThemeCreator.Controls;

/// <summary>
/// A Photoshop-style color picker: an outer hue ring plus an inner saturation/value field, each with
/// a draggable thumb. The source of truth is <see cref="HsvColor"/> (not RGB) so the hue survives when
/// saturation or value hit 0 — otherwise the ring thumb would jump around on grays. <see cref="Color"/>
/// is a two-way mirror for convenience.
/// </summary>
public partial class ColorWheel : UserControl
{
    public static readonly StyledProperty<HsvColor> HsvColorProperty =
        AvaloniaProperty.Register<ColorWheel, HsvColor>(
            nameof(HsvColor), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<ColorWheel, Color>(
            nameof(Color), defaultBindingMode: BindingMode.TwoWay);

    public HsvColor HsvColor
    {
        get => GetValue(HsvColorProperty);
        set => SetValue(HsvColorProperty, value);
    }

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    private readonly SolidColorBrush _svBaseBrush = new(Colors.Red);
    private bool _updating;

    private enum DragMode { None, Hue, Sv }
    private DragMode _drag;

    // Geometry, recomputed from the actual control size.
    private double _c, _outerR, _innerR, _midR, _ringT, _sqHalf, _hueThumbR, _svThumbR;

    public ColorWheel()
    {
        InitializeComponent();
        SvBase.Fill = _svBaseBrush;

        Root.PointerPressed += OnPointerPressed;
        Root.PointerMoved += OnPointerMoved;
        Root.PointerReleased += OnPointerReleased;
        SizeChanged += (_, _) => { ComputeGeometry(); Relayout(); };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_updating) return;

        if (change.Property == HsvColorProperty)
        {
            _updating = true;
            SetValue(ColorProperty, HsvColor.ToRgb()); // keep the RGB mirror in sync
            _updating = false;
            Relayout();
        }
        else if (change.Property == ColorProperty)
        {
            _updating = true;
            SetValue(HsvColorProperty, Color.ToHsv());
            _updating = false;
            Relayout();
        }
    }

    // ---- geometry & rendering -----------------------------------------

    private void ComputeGeometry()
    {
        double sz = Math.Min(Bounds.Width, Bounds.Height);
        if (sz <= 0) { _c = 0; return; }

        _c = sz / 2;
        _outerR = sz / 2;
        _ringT = sz * 0.12;
        _midR = _outerR - _ringT / 2;
        _innerR = _outerR - _ringT;
        _sqHalf = _innerR * 0.60;
        _hueThumbR = sz * 0.038;
        _svThumbR = sz * 0.030;

        Ring.Width = Ring.Height = 2 * _midR;
        Ring.StrokeThickness = _ringT;
        Canvas.SetLeft(Ring, _c - _midR);
        Canvas.SetTop(Ring, _c - _midR);

        SvArea.Width = SvArea.Height = 2 * _sqHalf;
        Canvas.SetLeft(SvArea, _c - _sqHalf);
        Canvas.SetTop(SvArea, _c - _sqHalf);

        HueThumb.Width = HueThumb.Height = 2 * _hueThumbR;
        SvThumb.Width = SvThumb.Height = 2 * _svThumbR;
    }

    /// <summary>Reposition both thumbs and refresh the SV base hue from the current HsvColor.</summary>
    private void Relayout()
    {
        if (_c <= 0) return;
        HsvColor hsv = HsvColor;

        _svBaseBrush.Color = new HsvColor(1, hsv.H, 1, 1).ToRgb();

        // Hue thumb on the ring. Screen angle 0 is east; we offset by 90° so it lines up with the
        // conic gradient (which starts at the top and runs clockwise).
        double theta = (hsv.H - 90) * Math.PI / 180.0;
        double hx = _c + _midR * Math.Cos(theta);
        double hy = _c + _midR * Math.Sin(theta);
        Canvas.SetLeft(HueThumb, hx - _hueThumbR);
        Canvas.SetTop(HueThumb, hy - _hueThumbR);

        // SV thumb: saturation across, value up.
        double sx = (_c - _sqHalf) + hsv.S * (2 * _sqHalf);
        double sy = (_c - _sqHalf) + (1 - hsv.V) * (2 * _sqHalf);
        Canvas.SetLeft(SvThumb, sx - _svThumbR);
        Canvas.SetTop(SvThumb, sy - _svThumbR);
    }

    // ---- pointer interaction ------------------------------------------

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_c <= 0) return;
        Point p = e.GetPosition(Root);
        double d = Math.Sqrt((p.X - _c) * (p.X - _c) + (p.Y - _c) * (p.Y - _c));

        _drag = d <= _innerR ? DragMode.Sv
              : d <= _outerR + _ringT ? DragMode.Hue
              : DragMode.None;

        if (_drag == DragMode.None) return;
        e.Pointer.Capture(Root);
        ApplyPointer(p);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_drag != DragMode.None)
            ApplyPointer(e.GetPosition(Root));
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _drag = DragMode.None;
        e.Pointer.Capture(null);
    }

    private void ApplyPointer(Point p)
    {
        HsvColor cur = HsvColor;
        if (_drag == DragMode.Hue)
        {
            double ang = Math.Atan2(p.Y - _c, p.X - _c) * 180.0 / Math.PI;
            double h = (ang + 90 + 360) % 360;
            HsvColor = new HsvColor(1, h, cur.S, cur.V);
        }
        else if (_drag == DragMode.Sv)
        {
            double s = Clamp01((p.X - (_c - _sqHalf)) / (2 * _sqHalf));
            double v = Clamp01(1 - (p.Y - (_c - _sqHalf)) / (2 * _sqHalf));
            HsvColor = new HsvColor(1, cur.H, s, v);
        }
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
