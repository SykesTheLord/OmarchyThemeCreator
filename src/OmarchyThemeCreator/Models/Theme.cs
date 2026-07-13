using System.Collections.Generic;
using System.Text;

namespace OmarchyThemeCreator.Models;

/// <summary>
/// An Omarchy theme loaded from (or destined for) a folder under
/// <c>~/.config/omarchy/themes/&lt;name&gt;</c> or a built-in themes directory.
/// </summary>
public sealed class Theme
{
    /// <summary>kebab-case directory name / id (e.g. "tokyo-night").</summary>
    public string Name { get; set; } = "my-theme";

    /// <summary>Absolute path to the theme folder, or null if not yet saved.</summary>
    public string? Path { get; set; }

    /// <summary>True when this theme lives in a read-only built-in themes directory.</summary>
    public bool IsBuiltIn { get; set; }

    public ThemeColors Colors { get; set; } = new();

    /// <summary>Contents of icons.theme (e.g. "Yaru-magenta"), or null if absent.</summary>
    public string? IconTheme { get; set; }

    /// <summary>Whether the theme ships a light.mode marker.</summary>
    public bool LightMode { get; set; }

    /// <summary>Absolute paths of images currently in the theme's backgrounds/ folder.</summary>
    public List<string> Backgrounds { get; set; } = new();

    /// <summary>Display name derived from the kebab-case name (e.g. "Tokyo Night").</summary>
    public string DisplayName => ToDisplayName(Name);

    public static string ToDisplayName(string name)
    {
        string[] parts = name.Split('-', System.StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
                parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
        }
        return string.Join(' ', parts);
    }

    public static string ToSlug(string display)
    {
        string lower = display.Trim().ToLowerInvariant();
        StringBuilder sb = new System.Text.StringBuilder(lower.Length);
        bool lastDash = false;
        foreach (char ch in lower)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastDash = false;
            }
            else if (!lastDash && sb.Length > 0)
            {
                sb.Append('-');
                lastDash = true;
            }
        }
        return sb.ToString().Trim('-');
    }
}
