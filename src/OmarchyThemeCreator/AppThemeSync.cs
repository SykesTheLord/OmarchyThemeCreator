using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using OmarchyThemeCreator.Models;

namespace OmarchyThemeCreator;

/// <summary>
/// Maps an Omarchy <see cref="ThemeColors"/> palette onto the running application's Avalonia
/// resources so the editor's own chrome matches the live desktop theme. Updates the
/// <c>DynamicResource</c> brushes consumed by <c>MainWindow.axaml</c>, overrides Fluent's
/// <c>SystemAccentColor</c> family (checkboxes, sliders, focus rings, selection, tab underline),
/// and flips the light/dark theme variant based on the background's luminance.
///
/// Lives in the app root rather than <c>Services/</c> because it deliberately touches Avalonia;
/// keep the pure I/O in <c>LiveOmarchyThemeService</c>. Must be called on the UI thread.
/// </summary>
public static class AppThemeSync
{
    public static void Apply(Application app, ThemeColors c)
    {
        IResourceDictionary res = app.Resources;
        string bg = c.Background, fg = c.Foreground, accent = c.Accent;

        // Custom surface palette: nudge the background toward the foreground for subtle elevation,
        // so panels/insets/borders read correctly against any bg (dark or light theme alike).
        SetBrush(res, "AppBackgroundBrush", bg);
        SetBrush(res, "AppSurfaceBrush", ColorMath.MixHex(bg, fg, 0.06));
        SetBrush(res, "AppSurfaceAltBrush", ColorMath.MixHex(bg, fg, 0.11));
        SetBrush(res, "AppBorderBrush", ColorMath.MixHex(bg, fg, 0.22));
        SetBrush(res, "AppForegroundBrush", fg);
        SetBrush(res, "AppMutedBrush", ColorMath.MixHex(fg, bg, 0.40));
        SetBrush(res, "AppAccentBrush", accent);
        SetBrush(res, "AppAccentForegroundBrush", ColorMath.ReadableTextOn(accent));
        SetBrush(res, "AppSelectionBrush", c.SelectionBackground);
        SetBrush(res, "AppSelectionForegroundBrush", c.SelectionForeground);
        SetBrush(res, "AppDangerBrush", c.Ansi[1]); // ANSI red

        // Fluent's accent color family — set all seven so accent-driven control chrome follows.
        res["SystemAccentColor"] = Color.Parse(accent);
        res["SystemAccentColorLight1"] = Shift(accent, 0.08);
        res["SystemAccentColorLight2"] = Shift(accent, 0.16);
        res["SystemAccentColorLight3"] = Shift(accent, 0.24);
        res["SystemAccentColorDark1"] = Shift(accent, -0.08);
        res["SystemAccentColorDark2"] = Shift(accent, -0.16);
        res["SystemAccentColorDark3"] = Shift(accent, -0.24);

        // Match Fluent's base control chrome to the theme's overall lightness.
        app.RequestedThemeVariant =
            ColorMath.RelativeLuminance(bg) < 0.5 ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private static void SetBrush(IResourceDictionary res, string key, string hex) =>
        res[key] = new SolidColorBrush(Color.Parse(hex));

    /// <summary>Lighten (positive) or darken (negative) a hex color by an HSL-lightness delta.</summary>
    private static Color Shift(string hex, double delta)
    {
        ColorMath.Hsl h = ColorMath.ToHsl(hex);
        return Color.Parse(ColorMath.FromHsl(h with { L = Math.Clamp(h.L + delta, 0, 1) }));
    }
}
