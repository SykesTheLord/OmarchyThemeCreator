using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmarchyThemeCreator.Services;
using Serilog;
using SkiaSharp;

namespace OmarchyThemeCreator.ViewModels;

/// <summary>
/// Drives the pop-up wallpaper editor (<c>ImageEditorDialog</c>): the six adjustment sliders and
/// one-click presets, with a live preview that re-renders on every change. The source image is
/// decoded once at a reduced size so slider drags stay responsive; the caller re-applies
/// <see cref="Options"/> at full resolution when the user saves.
/// </summary>
public sealed partial class ImageEditorViewModel : ObservableObject, IDisposable
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ImageEditorViewModel>();

    /// <summary>Longest edge the preview is decoded to — small enough to re-filter per slider tick,
    /// large enough to judge the result.</summary>
    private const int PreviewMaxWidth = 900;

    private readonly ImageEditService _editor;
    private readonly SKBitmap _previewSource; // downscaled, decoded once, reused every render.

    // Set while a preset assigns several sliders at once, so the six property-changed hooks don't
    // each trigger a render; we render a single time after the batch instead.
    private bool _suppressRender;

    public string ImagePath { get; }

    /// <summary>The live, filtered preview the dialog binds to.</summary>
    [ObservableProperty] private Bitmap? _preview;

    // Same ranges/defaults as the Wallpaper tab's editor. Each change re-renders the preview.
    [ObservableProperty] private double _brightness = 1;
    [ObservableProperty] private double _contrast = 1;
    [ObservableProperty] private double _saturation = 1;
    [ObservableProperty] private double _blur;
    [ObservableProperty] private double _vignette;
    [ObservableProperty] private double _grain;

    partial void OnBrightnessChanged(double value) => Render();
    partial void OnContrastChanged(double value) => Render();
    partial void OnSaturationChanged(double value) => Render();
    partial void OnBlurChanged(double value) => Render();
    partial void OnVignetteChanged(double value) => Render();
    partial void OnGrainChanged(double value) => Render();

    public IReadOnlyList<string> PresetNames => _editor.PresetNames;

    public ImageEditorViewModel(string imagePath, ImageEditService editor)
    {
        ImagePath = imagePath;
        _editor = editor;
        _previewSource = DecodeDownscaled(imagePath, PreviewMaxWidth);
        Render();
    }

    /// <summary>The current slider values as edit options, ready to apply at full resolution.</summary>
    public ImageEditOptions Options => new()
    {
        Brightness = (float)Brightness,
        Contrast = (float)Contrast,
        Saturation = (float)Saturation,
        Blur = (float)Blur,
        Vignette = (float)Vignette,
        Grain = (float)Grain,
        ToneColor = SKColors.Transparent,
    };

    [RelayCommand]
    private void ApplyPreset(string name)
    {
        ImageEditOptions o = _editor.Preset(name);
        _suppressRender = true;
        Brightness = o.Brightness;
        Contrast = o.Contrast;
        Saturation = o.Saturation;
        Blur = o.Blur;
        Vignette = o.Vignette;
        Grain = o.Grain;
        _suppressRender = false;
        Render();
    }

    private void Render()
    {
        if (_suppressRender) return;
        try
        {
            using SKBitmap edited = _editor.Apply(_previewSource, Options);
            Preview = BitmapInterop.ToAvalonia(edited);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Editor preview render failed for {Path}", ImagePath);
        }
    }

    /// <summary>Decode <paramref name="path"/> and shrink it to at most <paramref name="maxWidth"/> px
    /// wide (keeping aspect), so per-slider re-filtering works on a small bitmap.</summary>
    private static SKBitmap DecodeDownscaled(string path, int maxWidth)
    {
        using SKBitmap full = SKBitmap.Decode(path)
                         ?? throw new InvalidOperationException("Could not decode image.");
        if (full.Width <= maxWidth) return full.Copy();

        int height = (int)Math.Round(full.Height * (maxWidth / (double)full.Width));
        return full.Resize(new SKImageInfo(maxWidth, height), SKFilterQuality.Medium) ?? full.Copy();
    }

    public void Dispose() => _previewSource.Dispose();
}
