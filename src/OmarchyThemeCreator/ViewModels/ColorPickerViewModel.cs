using System;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmarchyThemeCreator.Models;
using OmarchyThemeCreator.Services;

namespace OmarchyThemeCreator.ViewModels;

/// <summary>
/// Backing model for <c>ColorPickerDialog</c>. The single source of truth is <see cref="Hsv"/>; every
/// other readout (hex, RGB, HSB, CMYK, swatch brush) is derived from it. Editing any editable field
/// funnels back into <see cref="Hsv"/>, and a single <c>_suppress</c> guard breaks the feedback loop —
/// the same reentrancy pattern used by <see cref="ColorField"/>.
/// </summary>
public sealed partial class ColorPickerViewModel : ObservableObject
{
    private readonly IScreenColorPicker _picker;
    private bool _suppress;

    // Source of truth (two-way bound to the ColorWheel).
    [ObservableProperty] private HsvColor _hsv;

    // Editable readouts (drive the color).
    [ObservableProperty] private string _hex = "#000000";
    [ObservableProperty] private int _r;
    [ObservableProperty] private int _g;
    [ObservableProperty] private int _b;
    [ObservableProperty] private int _hueDeg;
    [ObservableProperty] private int _satPct;
    [ObservableProperty] private int _briPct;

    // Read-only readouts (informational).
    [ObservableProperty] private int _cyan;
    [ObservableProperty] private int _magenta;
    [ObservableProperty] private int _yellow;
    [ObservableProperty] private int _black;

    // Swatch brushes.
    [ObservableProperty] private IBrush _currentBrush = Brushes.Black;
    public IBrush OriginalBrush { get; }

    public Color OriginalColor { get; }

    /// <summary>The picked color, as committed on OK.</summary>
    public Color CurrentColor => Hsv.ToRgb();

    public bool IsEyedropperAvailable => _picker.IsAvailable;

    public ColorPickerViewModel(Color initial, IScreenColorPicker picker)
    {
        _picker = picker;
        OriginalColor = initial;
        OriginalBrush = new SolidColorBrush(initial);
        Hsv = initial.ToHsv(); // triggers OnHsvChanged -> populates every readout
    }

    // ---- the funnel: any change routes through Hsv ---------------------

    partial void OnHsvChanged(HsvColor value)
    {
        if (_suppress) return;
        RefreshFromHsv();
    }

    private void RefreshFromHsv()
    {
        _suppress = true;
        try
        {
            Color c = Hsv.ToRgb();
            R = c.R; G = c.G; B = c.B;
            Hex = $"#{c.R:x2}{c.G:x2}{c.B:x2}";
            HueDeg = (int)Math.Round(Hsv.H);
            SatPct = (int)Math.Round(Hsv.S * 100);
            BriPct = (int)Math.Round(Hsv.V * 100);

            ColorMath.Cmyk k = ColorMath.RgbToCmyk(new ColorMath.Rgb(c.R, c.G, c.B));
            Cyan = (int)Math.Round(k.C * 100);
            Magenta = (int)Math.Round(k.M * 100);
            Yellow = (int)Math.Round(k.Y * 100);
            Black = (int)Math.Round(k.K * 100);

            CurrentBrush = new SolidColorBrush(c);
            OnPropertyChanged(nameof(CurrentColor));
        }
        finally { _suppress = false; }
    }

    // Editing hex / RGB rebuilds the color; hue may reset on grays (fine — it's an explicit value).
    partial void OnHexChanged(string value)
    {
        if (_suppress) return;
        if (ThemeColors.NormalizeHex(value) is { } norm)
            Hsv = Color.Parse(norm).ToHsv();
    }

    partial void OnRChanged(int value) => SetFromRgb();
    partial void OnGChanged(int value) => SetFromRgb();
    partial void OnBChanged(int value) => SetFromRgb();

    private void SetFromRgb()
    {
        if (_suppress) return;
        Color c = Color.FromRgb((byte)Clamp255(R), (byte)Clamp255(G), (byte)Clamp255(B));
        Hsv = c.ToHsv();
    }

    // Editing H/S/B sets Hsv directly, preserving hue at S/V = 0.
    partial void OnHueDegChanged(int value) => SetFromHsb();
    partial void OnSatPctChanged(int value) => SetFromHsb();
    partial void OnBriPctChanged(int value) => SetFromHsb();

    private void SetFromHsb()
    {
        if (_suppress) return;
        Hsv = new HsvColor(1, ((HueDeg % 360) + 360) % 360, Clamp01(SatPct / 100.0), Clamp01(BriPct / 100.0));
    }

    [RelayCommand]
    private async Task Eyedropper()
    {
        if (await _picker.PickAsync() is { } picked)
            Hsv = picked.ToHsv();
    }

    private static int Clamp255(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
