using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmarchyThemeCreator.Models;
using OmarchyThemeCreator.Services;
using Serilog;

namespace OmarchyThemeCreator.ViewModels;

/// <summary>A preset row: its name plus swatch brushes for the palette preview.</summary>
public sealed class PresetItem
{
    public NamedPalette Palette { get; }
    public string Name => Palette.Name;
    public List<IBrush> Swatches { get; }

    public PresetItem(NamedPalette palette)
    {
        Palette = palette;
        Swatches = new List<IBrush>(9)
        {
            Brush(palette.Colors.Background),
            Brush(palette.Colors.Foreground),
            Brush(palette.Colors.Accent),
        };
        // A few representative ANSI colors.
        foreach (int i in new[] { 1, 2, 3, 4, 5, 6 })
            Swatches.Add(Brush(palette.Colors.Ansi[i]));
    }

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
}

/// <summary>
/// Presets tab: apply a built-in scheme or import a community Base16 <c>.yaml</c> file. Both
/// paths funnel a fresh palette clone into the editor via <see cref="IPaletteHost.ApplyPalette"/>.
/// </summary>
public sealed partial class PresetsViewModel : ObservableObject
{
    private static readonly ILogger Log = Serilog.Log.ForContext<PresetsViewModel>();

    private readonly PresetService _presets;
    private readonly IPaletteHost _host;

    public ObservableCollection<PresetItem> Items { get; } = new();

    [ObservableProperty] private PresetItem? _selected;

    public PresetsViewModel(PresetService presets, IPaletteHost host)
    {
        _presets = presets;
        _host = host;
        foreach (NamedPalette p in presets.BuiltIns)
            Items.Add(new PresetItem(p));
    }

    [RelayCommand]
    private void Apply()
    {
        if (Selected is null) return;
        _host.SetLightMode(Selected.Palette.LightMode);
        _host.ApplyPalette(Selected.Palette.Colors.Clone(), $"Applied preset '{Selected.Name}'.");
    }

    [RelayCommand]
    private async Task ImportBase16()
    {
        string? path = await _host.PickFileAsync();
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            ThemeColors colors = _presets.ImportBase16(path);
            _host.ApplyPalette(colors, $"Imported Base16 scheme from {System.IO.Path.GetFileName(path)}.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Base16 import failed for {Path}", path);
            _host.SetStatus("Base16 import failed: " + ex.Message);
        }
    }
}
