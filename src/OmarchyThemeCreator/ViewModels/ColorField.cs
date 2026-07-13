using System;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using OmarchyThemeCreator.Models;

namespace OmarchyThemeCreator.ViewModels;

/// <summary>
/// One editable entry in the palette (e.g. "background" or "color4"). Exposes the value
/// both as a hex string (text box) and as an Avalonia <see cref="Color"/> (color picker),
/// keeping the two in sync and notifying the parent editor of any change.
/// </summary>
public sealed partial class ColorField : ObservableObject
{
    private readonly Action<ColorField> _onChanged;
    private bool _suppress;

    public string Key { get; }
    public string Label { get; }

    [ObservableProperty]
    private string _hex;

    [ObservableProperty]
    private Color _color;

    [ObservableProperty]
    private bool _isValid = true;

    public ColorField(string key, string label, string hex, Action<ColorField> onChanged)
    {
        Key = key;
        Label = label;
        _onChanged = onChanged;
        _hex = hex;
        _color = ParseColor(hex);
    }

    /// <summary>The current value as a normalized #rrggbb string, or null if invalid.</summary>
    public string? NormalizedHex => ThemeColors.NormalizeHex(Hex);

    partial void OnHexChanged(string value)
    {
        if (_suppress) return;
        string? norm = ThemeColors.NormalizeHex(value);
        IsValid = norm is not null;
        if (norm is null) return;

        _suppress = true;
        Color = ParseColor(norm);
        _suppress = false;
        _onChanged(this);
    }

    partial void OnColorChanged(Color value)
    {
        if (_suppress) return;
        _suppress = true;
        Hex = $"#{value.R:x2}{value.G:x2}{value.B:x2}";
        IsValid = true;
        _suppress = false;
        _onChanged(this);
    }

    private static Color ParseColor(string hex)
    {
        string norm = ThemeColors.NormalizeHex(hex) ?? "#000000";
        return Color.Parse(norm);
    }
}
