using System;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmarchyThemeCreator.Models;
using OmarchyThemeCreator.Services;
using Serilog;

namespace OmarchyThemeCreator.ViewModels;

/// <summary>
/// Drives the Extract tab: pick a wallpaper, choose an extraction mode, tune with sliders, and
/// push the generated palette into the editor. Re-extracts automatically whenever the mode or a
/// slider changes so the preview tracks the controls.
/// </summary>
public sealed partial class ExtractViewModel : ObservableObject
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ExtractViewModel>();

    private readonly PaletteExtractionService _extractor;
    private readonly IPaletteHost _host;

    public ExtractViewModel(PaletteExtractionService extractor, IPaletteHost host)
    {
        _extractor = extractor;
        _host = host;
    }

    public Array Modes => Enum.GetValues(typeof(ExtractionMode));

    [ObservableProperty] private string? _wallpaperPath;
    [ObservableProperty] private Bitmap? _wallpaperThumb;
    [ObservableProperty] private ExtractionMode _mode = ExtractionMode.Normal;
    [ObservableProperty] private bool _lightMode;

    // Tuning sliders — neutral defaults (factors 1.0, additive 0).
    [ObservableProperty] private double _vibrance;
    [ObservableProperty] private double _saturation = 1;
    [ObservableProperty] private double _contrast;
    [ObservableProperty] private double _brightness = 1;
    [ObservableProperty] private double _temperature;
    [ObservableProperty] private double _shadows;
    [ObservableProperty] private double _highlights;

    partial void OnModeChanged(ExtractionMode value) => Extract();
    partial void OnLightModeChanged(bool value) { _host.SetLightMode(value); Extract(); }
    partial void OnVibranceChanged(double value) => Extract();
    partial void OnSaturationChanged(double value) => Extract();
    partial void OnContrastChanged(double value) => Extract();
    partial void OnBrightnessChanged(double value) => Extract();
    partial void OnTemperatureChanged(double value) => Extract();
    partial void OnShadowsChanged(double value) => Extract();
    partial void OnHighlightsChanged(double value) => Extract();

    [RelayCommand]
    private async Task PickWallpaper()
    {
        string? path = await _host.PickImageAsync();
        if (string.IsNullOrEmpty(path)) return;
        WallpaperPath = path;
        WallpaperThumb = BitmapInterop.TryLoad(path);
        Extract();
    }

    [RelayCommand]
    private void Extract()
    {
        if (string.IsNullOrEmpty(WallpaperPath)) return;
        try
        {
            ThemeColors colors = _extractor.Extract(WallpaperPath, BuildOptions());
            _host.ApplyPalette(
                colors,
                $"Extracted palette from {System.IO.Path.GetFileName(WallpaperPath)}.",
                coalesceKey: "extract:" + WallpaperPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Palette extraction failed for {Path}", WallpaperPath);
            _host.SetStatus("Extraction failed: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task UseAsBackground()
    {
        if (string.IsNullOrEmpty(WallpaperPath))
        {
            Log.Debug("UseAsBackground: no wallpaper loaded; ignoring");
            return;
        }
        Log.Information("UseAsBackground invoked for {Path}", WallpaperPath);
        BackgroundAddResult? result = await _host.AddBackground(WallpaperPath);
        if (result is not null)
            _host.SetStatus(result.Value.WasNew
                ? "Added wallpaper as a background."
                : "That wallpaper is already a background of this theme.");
    }

    /// <summary>Called by the editor when the tab should adopt an externally chosen wallpaper.</summary>
    public void LoadWallpaper(string path)
    {
        WallpaperPath = path;
        WallpaperThumb = BitmapInterop.TryLoad(path);
    }

    private ExtractionOptions BuildOptions()
    {
        double dominantHue = ColorMath.ToHsl(_host.CurrentPalette.Accent).H;
        return new ExtractionOptions
        {
            Mode = Mode,
            LightMode = LightMode,
            DominantHue = dominantHue,
            Vibrance = Vibrance,
            Saturation = Saturation,
            Contrast = Contrast,
            Brightness = Brightness,
            Temperature = Temperature,
            Shadows = Shadows,
            Highlights = Highlights,
        };
    }
}
