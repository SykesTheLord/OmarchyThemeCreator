using System;
using System.Collections.Generic;
using System.Globalization;

namespace OmarchyThemeCreator.Models;

/// <summary>
/// The full Omarchy <c>colors.toml</c> palette: the six named colors plus the 16 ANSI
/// colors. Keys and ordering mirror what Omarchy's template substitution expects
/// (<c>omarchy-theme-set-templates</c>).
/// </summary>
public sealed class ThemeColors
{
    // Named keys, in the order Omarchy writes them.
    public string Accent { get; set; } = "#7aa2f7";
    public string Cursor { get; set; } = "#c0caf5";
    public string Foreground { get; set; } = "#a9b1d6";
    public string Background { get; set; } = "#1a1b26";
    public string SelectionForeground { get; set; } = "#c0caf5";
    public string SelectionBackground { get; set; } = "#7aa2f7";

    // 16 ANSI colors (color0..color15).
    public string[] Ansi { get; } = new string[16]
    {
        "#32344a", "#f7768e", "#9ece6a", "#e0af68",
        "#7aa2f7", "#ad8ee6", "#449dab", "#787c99",
        "#444b6a", "#ff7a93", "#b9f27c", "#ff9e64",
        "#7da6ff", "#bb9af7", "#0db9d7", "#acb0d0",
    };

    /// <summary>All keys as they appear in colors.toml, in canonical order.</summary>
    public IEnumerable<KeyValuePair<string, string>> AsPairs()
    {
        yield return new("accent", Accent);
        yield return new("cursor", Cursor);
        yield return new("foreground", Foreground);
        yield return new("background", Background);
        yield return new("selection_foreground", SelectionForeground);
        yield return new("selection_background", SelectionBackground);
        for (int i = 0; i < Ansi.Length; i++)
            yield return new($"color{i}", Ansi[i]);
    }

    public void Set(string key, string value)
    {
        switch (key)
        {
            case "accent": Accent = value; break;
            case "cursor": Cursor = value; break;
            case "foreground": Foreground = value; break;
            case "background": Background = value; break;
            case "selection_foreground": SelectionForeground = value; break;
            case "selection_background": SelectionBackground = value; break;
            default:
                if (key.StartsWith("color", StringComparison.Ordinal)
                    && int.TryParse(key.AsSpan(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx)
                    && idx is >= 0 and < 16)
                {
                    Ansi[idx] = value;
                }
                break;
        }
    }

    public ThemeColors Clone()
    {
        ThemeColors c = new ThemeColors
        {
            Accent = Accent,
            Cursor = Cursor,
            Foreground = Foreground,
            Background = Background,
            SelectionForeground = SelectionForeground,
            SelectionBackground = SelectionBackground,
        };
        Array.Copy(Ansi, c.Ansi, Ansi.Length);
        return c;
    }

    /// <summary>Normalize a hex color to <c>#rrggbb</c> lowercase, or null if invalid.</summary>
    public static string? NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string s = value.Trim();
        if (!s.StartsWith('#')) s = "#" + s;
        if (s.Length != 7) return null;
        for (int i = 1; i < 7; i++)
            if (!Uri.IsHexDigit(s[i])) return null;
        return s.ToLowerInvariant();
    }

    /// <summary>Convert <c>#rrggbb</c> to decimal "r,g,b" as Omarchy's <c>{{ key_rgb }}</c> does.</summary>
    public static string ToRgb(string hex)
    {
        string s = hex.TrimStart('#');
        int r = Convert.ToInt32(s.Substring(0, 2), 16);
        int g = Convert.ToInt32(s.Substring(2, 2), 16);
        int b = Convert.ToInt32(s.Substring(4, 2), 16);
        return $"{r},{g},{b}";
    }
}
