using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OmarchyThemeCreator.Models;
using Serilog;

namespace OmarchyThemeCreator.Services;

/// <summary>A named starter palette shown in the Presets tab.</summary>
public sealed record NamedPalette(string Name, ThemeColors Colors, bool LightMode = false);

/// <summary>
/// Ships a set of well-known color schemes (Dracula, Nord, Gruvbox, Catppuccin, …) as ready
/// palettes, and imports community Base16 <c>.yaml</c> schemes into the Omarchy 22-key palette.
/// Base16 parsing is deliberately tolerant (like <see cref="ColorsTomlService"/>) so no YAML
/// dependency is needed.
/// </summary>
public sealed class PresetService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<PresetService>();

    public IReadOnlyList<NamedPalette> BuiltIns { get; } = BuildPresets();

    /// <summary>The standard Base16 → ANSI mapping (matches base16-shell).</summary>
    private static readonly int[] Base16ToAnsi =
    {
        0x00, 0x08, 0x0B, 0x0A, 0x0D, 0x0E, 0x0C, 0x05,
        0x03, 0x08, 0x0B, 0x0A, 0x0D, 0x0E, 0x0C, 0x07,
    };

    /// <summary>
    /// Parse a Base16 scheme file (<c>baseXX: "hex"</c> lines) into a palette. Accepts the
    /// modern <c>palette:</c>-nested form and the flat legacy form.
    /// </summary>
    public ThemeColors ImportBase16(string path)
    {
        string?[] bases = new string?[16];
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            int idx = line.IndexOf("base", StringComparison.OrdinalIgnoreCase);
            if (idx < 0 || idx + 6 > line.Length) continue;

            string hexPart = line.Substring(idx + 4, 2);
            if (!int.TryParse(hexPart, System.Globalization.NumberStyles.HexNumber, null, out int n) || n is < 0 or > 15)
                continue;

            string? value = ExtractHex(line.Substring(idx + 6));
            if (value is not null) bases[n] = value;
        }

        if (bases[0] is null || bases[5] is null)
        {
            Log.Warning("Base16 import rejected {Path}: missing base00/base05", path);
            throw new InvalidOperationException("Not a recognizable Base16 scheme (missing base00/base05).");
        }

        string B(int i) => bases[i] ?? bases[0]!;

        ThemeColors colors = new ThemeColors
        {
            Background = B(0x00),
            Foreground = B(0x05),
            Accent = B(0x0D),
            Cursor = B(0x05),
            SelectionBackground = B(0x02),
            SelectionForeground = B(0x05),
        };
        for (int i = 0; i < 16; i++)
            colors.Ansi[i] = B(Base16ToAnsi[i]);
        Log.Information("Imported Base16 scheme from {Path}", path);
        return colors;
    }

    /// <summary>Pull the first <c>#rrggbb</c> or bare <c>rrggbb</c> hex out of a value fragment.</summary>
    private static string? ExtractHex(string rest)
    {
        string span = rest.Trim().Trim(':', ' ', '"', '\'');
        int hash = span.IndexOf('#');
        if (hash >= 0) span = span.Substring(hash + 1);
        string hex = new string(span.TakeWhile(Uri.IsHexDigit).ToArray());
        return hex.Length == 6 ? ThemeColors.NormalizeHex("#" + hex) : null;
    }

    // ---- Built-in schemes ---------------------------------------------

    private static ThemeColors Build(string bg, string fg, string accent, params string[] ansi)
    {
        if (ansi.Length != 16) throw new ArgumentException("Need 16 ANSI colors.", nameof(ansi));
        ThemeColors c = new ThemeColors
        {
            Background = bg,
            Foreground = fg,
            Accent = accent,
            Cursor = fg,
            SelectionBackground = accent,
            SelectionForeground = bg,
        };
        for (int i = 0; i < 16; i++) c.Ansi[i] = ansi[i];
        return c;
    }

    private static List<NamedPalette> BuildPresets() => new()
    {
        new("Tokyo Night", new ThemeColors()), // the app default

        new("Dracula", Build("#282a36", "#f8f8f2", "#bd93f9",
            "#21222c", "#ff5555", "#50fa7b", "#f1fa8c", "#bd93f9", "#ff79c6", "#8be9fd", "#f8f8f2",
            "#6272a4", "#ff6e6e", "#69ff94", "#ffffa5", "#d6acff", "#ff92df", "#a4ffff", "#ffffff")),

        new("Nord", Build("#2e3440", "#d8dee9", "#88c0d0",
            "#3b4252", "#bf616a", "#a3be8c", "#ebcb8b", "#81a1c1", "#b48ead", "#88c0d0", "#e5e9f0",
            "#4c566a", "#bf616a", "#a3be8c", "#ebcb8b", "#81a1c1", "#b48ead", "#8fbcbb", "#eceff4")),

        new("Gruvbox Dark", Build("#282828", "#ebdbb2", "#fabd2f",
            "#282828", "#cc241d", "#98971a", "#d79921", "#458588", "#b16286", "#689d6a", "#a89984",
            "#928374", "#fb4934", "#b8bb26", "#fabd2f", "#83a598", "#d3869b", "#8ec07c", "#ebdbb2")),

        new("Gruvbox Light", Build("#fbf1c7", "#3c3836", "#d65d0e",
            "#fbf1c7", "#cc241d", "#98971a", "#d79921", "#458588", "#b16286", "#689d6a", "#7c6f64",
            "#928374", "#9d0006", "#79740e", "#b57614", "#076678", "#8f3f71", "#427b58", "#3c3836"),
            LightMode: true),

        new("Catppuccin Mocha", Build("#1e1e2e", "#cdd6f4", "#cba6f7",
            "#45475a", "#f38ba8", "#a6e3a1", "#f9e2af", "#89b4fa", "#f5c2e7", "#94e2d5", "#bac2de",
            "#585b70", "#f38ba8", "#a6e3a1", "#f9e2af", "#89b4fa", "#f5c2e7", "#94e2d5", "#a6adc8")),

        new("Catppuccin Macchiato", Build("#24273a", "#cad3f5", "#c6a0f6",
            "#494d64", "#ed8796", "#a6da95", "#eed49f", "#8aadf4", "#f5bde6", "#8bd5ca", "#b8c0e0",
            "#5b6078", "#ed8796", "#a6da95", "#eed49f", "#8aadf4", "#f5bde6", "#8bd5ca", "#a5adcb")),

        new("Catppuccin Frappe", Build("#303446", "#c6d0f5", "#ca9ee6",
            "#51576d", "#e78284", "#a6d189", "#e5c890", "#8caaee", "#f4b8e4", "#81c8be", "#b5bfe2",
            "#626880", "#e78284", "#a6d189", "#e5c890", "#8caaee", "#f4b8e4", "#81c8be", "#a5adce")),

        new("Catppuccin Latte", Build("#eff1f5", "#4c4f69", "#8839ef",
            "#5c5f77", "#d20f39", "#40a02b", "#df8e1d", "#1e66f5", "#ea76cb", "#179299", "#acb0be",
            "#6c6f85", "#d20f39", "#40a02b", "#df8e1d", "#1e66f5", "#ea76cb", "#179299", "#bcc0cc"),
            LightMode: true),

        new("Solarized Dark", Build("#002b36", "#839496", "#268bd2",
            "#073642", "#dc322f", "#859900", "#b58900", "#268bd2", "#d33682", "#2aa198", "#eee8d5",
            "#002b36", "#cb4b16", "#586e75", "#657b83", "#839496", "#6c71c4", "#93a1a1", "#fdf6e3")),

        new("Solarized Light", Build("#fdf6e3", "#657b83", "#268bd2",
            "#073642", "#dc322f", "#859900", "#b58900", "#268bd2", "#d33682", "#2aa198", "#eee8d5",
            "#002b36", "#cb4b16", "#586e75", "#657b83", "#839496", "#6c71c4", "#93a1a1", "#fdf6e3"),
            LightMode: true),

        new("Rosé Pine", Build("#191724", "#e0def4", "#c4a7e7",
            "#26233a", "#eb6f92", "#31748f", "#f6c177", "#9ccfd8", "#c4a7e7", "#ebbcba", "#e0def4",
            "#6e6a86", "#eb6f92", "#31748f", "#f6c177", "#9ccfd8", "#c4a7e7", "#ebbcba", "#e0def4")),

        new("Rosé Pine Moon", Build("#232136", "#e0def4", "#c4a7e7",
            "#393552", "#eb6f92", "#3e8fb0", "#f6c177", "#9ccfd8", "#c4a7e7", "#ea9a97", "#e0def4",
            "#6e6a86", "#eb6f92", "#3e8fb0", "#f6c177", "#9ccfd8", "#c4a7e7", "#ea9a97", "#e0def4")),

        new("Everforest Dark", Build("#2d353b", "#d3c6aa", "#a7c080",
            "#343f44", "#e67e80", "#a7c080", "#dbbc7f", "#7fbbb3", "#d699b6", "#83c092", "#d3c6aa",
            "#475258", "#e67e80", "#a7c080", "#dbbc7f", "#7fbbb3", "#d699b6", "#83c092", "#d3c6aa")),

        new("Kanagawa", Build("#1f1f28", "#dcd7ba", "#7e9cd8",
            "#16161d", "#c34043", "#76946a", "#c0a36e", "#7e9cd8", "#957fb8", "#6a9589", "#c8c093",
            "#727169", "#e82424", "#98bb6c", "#e6c384", "#7fb4ca", "#938aa9", "#7aa89f", "#dcd7ba")),

        new("One Dark", Build("#282c34", "#abb2bf", "#61afef",
            "#282c34", "#e06c75", "#98c379", "#e5c07b", "#61afef", "#c678dd", "#56b6c2", "#abb2bf",
            "#5c6370", "#e06c75", "#98c379", "#e5c07b", "#61afef", "#c678dd", "#56b6c2", "#ffffff")),

        new("Monokai", Build("#272822", "#f8f8f2", "#a6e22e",
            "#272822", "#f92672", "#a6e22e", "#f4bf75", "#66d9ef", "#ae81ff", "#a1efe4", "#f8f8f2",
            "#75715e", "#f92672", "#a6e22e", "#f4bf75", "#66d9ef", "#ae81ff", "#a1efe4", "#f9f8f5")),

        new("Nightfox", Build("#192330", "#cdcecf", "#719cd6",
            "#393b44", "#c94f6d", "#81b29a", "#dbc074", "#719cd6", "#9d79d6", "#63cdcf", "#dfdfe0",
            "#575860", "#d16983", "#8ebaa4", "#e0c989", "#86abdc", "#baa1e2", "#7ad5d6", "#e4e4e5")),

        new("Ayu Dark", Build("#0a0e14", "#b3b1ad", "#39bae6",
            "#01060e", "#ea6c73", "#91b362", "#f9af4f", "#53bdfa", "#fae994", "#90e1c6", "#c7c7c7",
            "#686868", "#f07178", "#c2d94c", "#ffb454", "#59c2ff", "#ffee99", "#95e6cb", "#ffffff")),

        new("Zenburn", Build("#3f3f3f", "#dcdccc", "#f0dfaf",
            "#4d4d4d", "#705050", "#60b48a", "#f0dfaf", "#506070", "#dc8cc3", "#8cd0d3", "#dcdccc",
            "#709080", "#dca3a3", "#c3bf9f", "#e0cf9f", "#94bff3", "#ec93d3", "#93e0e3", "#ffffff")),

        new("Sakura", Build("#1c1720", "#f2d5e0", "#e39ec1",
            "#2a2230", "#e35f7d", "#a3d9a5", "#e6c384", "#8aa9e6", "#e39ec1", "#7ad5cf", "#f2d5e0",
            "#4a3f52", "#f27a94", "#b6e6b8", "#f0d29a", "#a0bcf0", "#f0b6d4", "#96e6e0", "#fbeaf1")),
    };
}
