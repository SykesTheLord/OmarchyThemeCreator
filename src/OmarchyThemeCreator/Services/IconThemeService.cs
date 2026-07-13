using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OmarchyThemeCreator.Services;

/// <summary>
/// Discovers installed icon themes suitable for a theme's <c>icons.theme</c> file.
/// Omarchy themes typically use Yaru variants, so those are surfaced first.
/// </summary>
public sealed class IconThemeService
{
    private static readonly string[] IconRoots =
    {
        "/usr/share/icons",
        "/usr/local/share/icons",
    };

    // Sizes worth showing in a tiny preview, smallest-useful first; and recognizable icons that
    // most themes ship (a folder is both distinctive per-pack and near-universal).
    private static readonly string[] PreferredSizes =
        { "48x48", "64x64", "32x32", "256x256", "24x24", "22x22", "16x16" };
    private static readonly string[] PreferredIcons =
        { "folder.png", "user-home.png", "system-file-manager.png", "text-editor.png", "firefox.png" };

    /// <summary>Yaml/Yaru variants plus any other installed icon themes, Yaru first.</summary>
    public IReadOnlyList<string> ListIconThemes()
    {
        SortedSet<string> found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string root in AllRoots())
        {
            if (!Directory.Exists(root)) continue;
            foreach (string dir in Directory.EnumerateDirectories(root))
            {
                // A valid icon theme has an index.theme file.
                if (File.Exists(Path.Combine(dir, "index.theme")))
                    found.Add(Path.GetFileName(dir));
            }
        }

        // Yaru variants first (Omarchy convention), then everything else.
        IEnumerable<string> yaru = found.Where(n => n.StartsWith("Yaru", StringComparison.OrdinalIgnoreCase));
        IEnumerable<string> rest = found.Where(n => !n.StartsWith("Yaru", StringComparison.OrdinalIgnoreCase));
        return yaru.Concat(rest).ToList();
    }

    /// <summary>
    /// Path to a representative PNG icon from the given theme (a folder icon preferred, since Yaru
    /// variants tint it per-pack), or null if the theme ships no raster icons we can display.
    /// SVG-only themes return null — we have no SVG rasterizer wired in.
    /// </summary>
    public string? FindExampleIcon(string themeName)
    {
        foreach (string root in AllRoots())
        {
            string themeDir = Path.Combine(root, themeName);
            if (!Directory.Exists(themeDir)) continue;
            try
            {
                // 1. A recognizable named icon at a preferred size.
                foreach (string size in PreferredSizes)
                {
                    string sizeDir = Path.Combine(themeDir, size);
                    if (!Directory.Exists(sizeDir)) continue;
                    foreach (string name in PreferredIcons)
                    {
                        string? hit = Directory
                            .EnumerateFiles(sizeDir, name, SearchOption.AllDirectories)
                            .FirstOrDefault();
                        if (hit is not null) return hit;
                    }
                }

                // 2. Failing that, any PNG at a preferred size.
                foreach (string size in PreferredSizes)
                {
                    string sizeDir = Path.Combine(themeDir, size);
                    if (!Directory.Exists(sizeDir)) continue;
                    string? any = Directory
                        .EnumerateFiles(sizeDir, "*.png", SearchOption.AllDirectories)
                        .FirstOrDefault();
                    if (any is not null) return any;
                }

                // 3. Last resort: any PNG anywhere in the theme.
                string? anyPng = Directory
                    .EnumerateFiles(themeDir, "*.png", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (anyPng is not null) return anyPng;
            }
            catch (Exception)
            {
                // Unreadable dir — try the next root, degrade to no preview.
            }
        }
        return null;
    }

    private static IEnumerable<string> AllRoots()
    {
        foreach (string root in IconRoots) yield return root;
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "icons");
    }
}
