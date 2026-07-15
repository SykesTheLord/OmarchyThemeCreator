using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OmarchyThemeCreator.Models;
using Serilog;
using SkiaSharp;

namespace OmarchyThemeCreator.Services;

/// <summary>Outcome of adding a background: its destination path and whether it was newly tracked
/// (as opposed to a duplicate that overwrote an already-present background of the same name).</summary>
public readonly record struct BackgroundAddResult(string Path, bool WasNew);

/// <summary>
/// Discovers, loads, saves, and clones Omarchy themes. Writes only into the user themes
/// directory (<c>~/.config/omarchy/themes</c>); the built-in themes directory is treated
/// as read-only clone source, mirroring Omarchy's own behavior.
/// </summary>
public sealed class ThemeRepository
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ThemeRepository>();

    private static readonly string[] ImageExtensions =
        { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" };

    // Config files Omarchy generates from colors.toml via its template engine
    // (<c>$OMARCHY_PATH/default/themed/*.tpl</c>, applied by <c>omarchy-theme-set-templates</c>).
    // These are pure colour mappings, so they can always be regenerated from the palette. A cloned
    // built-in brings its own hand-authored copies along; because omarchy-theme-set only templates a
    // file when the theme doesn't already ship one, those stale copies would shadow our edited
    // colors.toml and btop/terminals would keep the *original* theme's colours. We strip them on
    // Save so Omarchy re-templates them from the current palette. Deliberately excludes files that
    // can carry structural (non-colour) customisation a theme author wrote by hand —
    // waybar.css, helix.toml, obsidian.css — which we must not silently discard.
    private static readonly string[] TemplatedThemeFiles =
        { "btop.theme", "alacritty.toml", "foot.ini", "kitty.conf", "ghostty.conf",
          "mako.ini", "swayosd.css", "walker.css", "hyprland.conf", "hyprlock.conf" };

    public string UserThemesDir { get; }
    public string SystemThemesDir { get; }

    public ThemeRepository()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        UserThemesDir = Path.Combine(home, ".config", "omarchy", "themes");

        string? omarchyPath = Environment.GetEnvironmentVariable("OMARCHY_PATH");
        if (string.IsNullOrWhiteSpace(omarchyPath))
            omarchyPath = Path.Combine(home, ".local", "share", "omarchy");
        SystemThemesDir = Path.Combine(omarchyPath, "themes");
    }

    /// <summary>List installed themes (user + built-in), deduped by name, sorted.</summary>
    public IReadOnlyList<Theme> ListThemes()
    {
        Dictionary<string, Theme> byName = new Dictionary<string, Theme>(StringComparer.Ordinal);

        // Built-in first, so a user theme of the same name overrides it as editable.
        foreach (string dir in EnumerateThemeDirs(SystemThemesDir))
            byName[Path.GetFileName(dir)] = MakeStub(dir, isBuiltIn: true);
        foreach (string dir in EnumerateThemeDirs(UserThemesDir))
            byName[Path.GetFileName(dir)] = MakeStub(dir, isBuiltIn: false);

        return byName.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<string> EnumerateThemeDirs(string root)
    {
        if (!Directory.Exists(root)) yield break;
        foreach (string dir in Directory.EnumerateDirectories(root))
            yield return dir;
    }

    private static Theme MakeStub(string dir, bool isBuiltIn) => new()
    {
        Name = Path.GetFileName(dir),
        Path = dir,
        IsBuiltIn = isBuiltIn,
    };

    /// <summary>Fully load a theme's palette, icons, light mode, and backgrounds.</summary>
    public Theme Load(string dir)
    {
        Theme theme = new Theme
        {
            Name = Path.GetFileName(dir),
            Path = dir,
            IsBuiltIn = IsUnder(dir, SystemThemesDir) && !IsUnder(dir, UserThemesDir),
        };

        string colorsPath = Path.Combine(dir, "colors.toml");
        if (File.Exists(colorsPath))
            theme.Colors = ColorsTomlService.Load(colorsPath);

        string iconsPath = Path.Combine(dir, "icons.theme");
        if (File.Exists(iconsPath))
            theme.IconTheme = File.ReadAllText(iconsPath).Trim();

        theme.LightMode = File.Exists(Path.Combine(dir, "light.mode"));

        string bgDir = Path.Combine(dir, "backgrounds");
        if (Directory.Exists(bgDir))
        {
            theme.Backgrounds = Directory.EnumerateFiles(bgDir)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
        }

        return theme;
    }

    /// <summary>
    /// Persist a theme into the user themes directory. Returns the theme folder path.
    /// Never writes to the built-in themes directory.
    /// </summary>
    public string Save(Theme theme)
    {
        Log.Information("Save requested for theme {Name} (icon={Icon}, light={Light}, {BgCount} background(s))",
            theme.Name, theme.IconTheme ?? "<none>", theme.LightMode, theme.Backgrounds.Count);

        string slug = Theme.ToSlug(theme.Name);
        Log.Debug("Slugified theme name {Name} -> {Slug}", theme.Name, slug);
        if (slug.Length == 0)
        {
            Log.Warning("Save aborted: theme name {Name} slugified to empty", theme.Name);
            throw new InvalidOperationException("Theme name is empty.");
        }

        // Where the theme previously lived on disk (if it was already saved). Captured before we
        // repoint theme.Path so a rename can clean up the stale folder at the end.
        string? previousPath = theme.Path;

        string dir = Path.Combine(UserThemesDir, slug);
        bool existed = Directory.Exists(dir);
        Directory.CreateDirectory(dir);
        Log.Debug("{Action} theme directory {Dir}", existed ? "Reusing existing" : "Created", dir);

        string colorsPath = Path.Combine(dir, "colors.toml");
        ColorsTomlService.Save(colorsPath, theme.Colors);
        Log.Debug("Wrote colors.toml ({Bytes} bytes) to {Path}", FileLength(colorsPath), colorsPath);

        // Remove any colour-derived config files (btop.theme, terminal themes, …) so Omarchy
        // regenerates them from the palette we just wrote. Without this, a stale copy cloned from a
        // built-in shadows colors.toml and the app's palette edits never reach btop. See
        // TemplatedThemeFiles for why the list is what it is.
        foreach (string derived in TemplatedThemeFiles)
        {
            string derivedPath = Path.Combine(dir, derived);
            if (File.Exists(derivedPath))
            {
                File.Delete(derivedPath);
                Log.Debug("Removed stale derived config {File} so Omarchy re-templates it from colors.toml", derived);
            }
        }

        string iconsPath = Path.Combine(dir, "icons.theme");
        if (!string.IsNullOrWhiteSpace(theme.IconTheme))
        {
            File.WriteAllText(iconsPath, theme.IconTheme.Trim() + "\n");
            Log.Debug("Wrote icons.theme = {Icon}", theme.IconTheme.Trim());
        }
        else if (File.Exists(iconsPath))
        {
            File.Delete(iconsPath);
            Log.Debug("Removed icons.theme (no icon theme selected)");
        }

        string lightPath = Path.Combine(dir, "light.mode");
        if (theme.LightMode)
        {
            File.WriteAllText(lightPath, string.Empty);
            Log.Debug("Wrote light.mode marker");
        }
        else if (File.Exists(lightPath))
        {
            File.Delete(lightPath);
            Log.Debug("Removed light.mode marker (dark theme)");
        }

        // Backgrounds are written to disk when added/cloned, not by Save — so a rename (or saving an
        // opened built-in) lands colors in a new directory whose backgrounds/ is empty while the
        // tracked paths still point at the old folder. Copy any background that isn't already under
        // this theme's folder into dir/backgrounds/ and repoint it, so a saved theme always owns its
        // wallpapers. Copy (never move): the source may be a read-only built-in the user keeps.
        string bgDir = Path.Combine(dir, "backgrounds");
        string bgDirFull = Path.GetFullPath(bgDir);
        int relocated = 0;
        for (int i = 0; i < theme.Backgrounds.Count; i++)
        {
            string bg = theme.Backgrounds[i];
            // Already in this theme's backgrounds/ (exact folder match, not a prefix) — leave as-is.
            if (string.Equals(Path.GetFullPath(Path.GetDirectoryName(bg) ?? ""), bgDirFull, StringComparison.Ordinal))
                continue;
            if (!File.Exists(bg))
            {
                Log.Warning("Save: tracked background {Path} no longer exists; skipping", bg);
                continue;
            }
            Directory.CreateDirectory(bgDir);
            string destBg = Path.Combine(bgDir, Path.GetFileName(bg));
            File.Copy(bg, destBg, overwrite: true);
            theme.Backgrounds[i] = destBg;
            relocated++;
        }
        if (relocated > 0)
            Log.Information("Copied {Count} background(s) into {Dir}", relocated, bgDir);

        // A rename writes the theme under a new slug directory, but the old one lingers — the
        // switcher would then list both the old and new name. Now that colors, icons, light.mode
        // and backgrounds have all been written/copied into `dir`, delete the previous folder.
        // Guard hard: only ever remove a directory that is (a) under the user themes dir — never a
        // read-only built-in — and (b) genuinely different from the new target.
        if (previousPath is not null
            && IsUnder(previousPath, UserThemesDir)
            && Directory.Exists(previousPath)
            && !string.Equals(Path.GetFullPath(previousPath), Path.GetFullPath(dir), StringComparison.Ordinal))
        {
            Directory.Delete(previousPath, recursive: true);
            Log.Information("Removed old theme directory {Old} after rename to {New}", previousPath, dir);
        }

        theme.Name = slug;
        theme.Path = dir;
        theme.IsBuiltIn = false;
        Log.Information("Saved theme {Name} to {Dir}", slug, dir);
        return dir;
    }

    private static long FileLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return -1; }
    }

    /// <summary>
    /// Copy an existing theme (built-in or user) into the user themes directory under a new
    /// name, so it can be modified safely. Returns the fully loaded clone.
    /// </summary>
    public Theme Clone(Theme source, string newName)
    {
        if (source.Path is null)
            throw new InvalidOperationException("Source theme has no path.");

        string slug = Theme.ToSlug(newName);
        if (slug.Length == 0)
            throw new InvalidOperationException("New theme name is empty.");

        string dest = Path.Combine(UserThemesDir, slug);
        if (Directory.Exists(dest))
            throw new IOException($"A theme named '{slug}' already exists.");

        CopyDirectory(source.Path, dest);
        Log.Information("Cloned theme {Source} to {Dest}", source.Name, dest);
        return Load(dest);
    }

    public bool Exists(string name) =>
        Directory.Exists(Path.Combine(UserThemesDir, Theme.ToSlug(name)));

    public BackgroundAddResult AddBackground(Theme theme, string sourceImagePath)
    {
        Log.Debug("AddBackground: theme {Name}, source {Source}", theme.Name, sourceImagePath);
        if (theme.Path is null)
        {
            Log.Warning("AddBackground refused: theme {Name} has no path (not saved yet)", theme.Name);
            throw new InvalidOperationException("Save the theme before adding backgrounds.");
        }
        if (!File.Exists(sourceImagePath))
            Log.Warning("AddBackground: source {Source} does not exist; File.Copy will throw", sourceImagePath);

        string bgDir = Path.Combine(theme.Path, "backgrounds");
        Directory.CreateDirectory(bgDir);
        string dest = Path.Combine(bgDir, Path.GetFileName(sourceImagePath));
        bool overwriting = File.Exists(dest);
        File.Copy(sourceImagePath, dest, overwrite: true);
        Log.Debug("Copied background {Source} -> {Dest} ({Bytes} bytes, overwrite={Overwrite})",
            sourceImagePath, dest, FileLength(dest), overwriting);

        bool wasNew = !theme.Backgrounds.Contains(dest);
        if (wasNew)
            theme.Backgrounds.Add(dest);
        else
            Log.Debug("Background {Dest} already a background of this theme; overwrote file, list unchanged", dest);

        Log.Information("Added background {File} to theme {Name} (new={New}, now {Count} background(s))",
            Path.GetFileName(dest), theme.Name, wasNew, theme.Backgrounds.Count);
        return new BackgroundAddResult(dest, wasNew);
    }

    /// <summary>
    /// Rename a background to the Omarchy switcher convention: a two-digit ordering prefix plus a
    /// kebab-case slug of <paramref name="newBaseName"/>, keeping the original extension
    /// (e.g. "Sunset Ridge" → "03-sunset-ridge.jpg"). The numeric prefix drives the order the
    /// Omarchy background switcher cycles wallpapers. Updates the theme's tracked list in place and
    /// returns the new absolute path; leaves the file untouched if the name slugifies to empty.
    /// </summary>
    public string RenameBackground(Theme theme, string currentPath, string newBaseName)
    {
        if (theme.Path is null)
            throw new InvalidOperationException("Theme has no path.");

        string slug = Theme.ToSlug(newBaseName);
        if (slug.Length == 0)
        {
            Log.Warning("RenameBackground: {Name} slugified to empty; leaving {Path} unchanged",
                newBaseName, currentPath);
            return currentPath;
        }

        string bgDir = Path.Combine(theme.Path, "backgrounds");
        string ext = Path.GetExtension(currentPath);
        int prefix = NextBackgroundPrefix(bgDir, currentPath);
        string newPath = EnsureUniquePath(Path.Combine(bgDir, $"{prefix:D2}-{slug}{ext}"));

        if (string.Equals(newPath, currentPath, StringComparison.Ordinal))
        {
            Log.Debug("RenameBackground: target equals current ({Path}); nothing to do", currentPath);
            return currentPath;
        }

        File.Move(currentPath, newPath);
        int idx = theme.Backgrounds.IndexOf(currentPath);
        if (idx >= 0) theme.Backgrounds[idx] = newPath;
        else if (!theme.Backgrounds.Contains(newPath)) theme.Backgrounds.Add(newPath);

        Log.Information("Renamed background {Old} -> {New} in theme {Theme}",
            Path.GetFileName(currentPath), Path.GetFileName(newPath), theme.Name);
        return newPath;
    }

    /// <summary>
    /// Renumber the theme's backgrounds to match <paramref name="orderedPaths"/>, assigning fresh
    /// sequential <c>NN-</c> ordering prefixes (01-, 02-, …) so the Omarchy switcher cycles them in
    /// this order — the first entry becomes the default the switcher starts on. Renames on disk in
    /// two passes (via hidden temp names) so swapping two files can't collide, rewrites
    /// <see cref="Theme.Backgrounds"/> in place, and returns the new absolute paths in order.
    /// </summary>
    public IReadOnlyList<string> ReorderBackgrounds(Theme theme, IReadOnlyList<string> orderedPaths)
    {
        if (theme.Path is null)
            throw new InvalidOperationException("Theme has no path.");

        string bgDir = Path.Combine(theme.Path, "backgrounds");

        // Pass 1: park every file under a unique temp name so no final target can collide with a
        // source that hasn't moved yet (e.g. swapping 01- and 02-).
        var staged = new List<(string temp, string slug, string ext)>(orderedPaths.Count);
        foreach (string path in orderedPaths)
        {
            string ext = Path.GetExtension(path);
            string slug = Regex.Replace(Path.GetFileNameWithoutExtension(path), @"^\d+-", "");
            if (slug.Length == 0) slug = "wallpaper";
            string temp = Path.Combine(bgDir, $".reorder-{Guid.NewGuid():N}{ext}");
            File.Move(path, temp);
            staged.Add((temp, slug, ext));
        }

        // Pass 2: move each parked file into its final NN-slug.ext slot.
        var result = new List<string>(staged.Count);
        for (int i = 0; i < staged.Count; i++)
        {
            (string temp, string slug, string ext) = staged[i];
            string finalPath = EnsureUniquePath(Path.Combine(bgDir, $"{i + 1:D2}-{slug}{ext}"));
            File.Move(temp, finalPath);
            result.Add(finalPath);
        }

        theme.Backgrounds = new List<string>(result);
        Log.Information("Reordered {Count} background(s) in theme {Theme}", result.Count, theme.Name);
        return result;
    }

    /// <summary>Next free two-digit ordering prefix, i.e. one past the highest <c>NN-</c> already
    /// present in the backgrounds folder (ignoring <paramref name="excludePath"/>).</summary>
    private static int NextBackgroundPrefix(string bgDir, string excludePath)
    {
        int max = 0;
        foreach (string file in Directory.EnumerateFiles(bgDir))
        {
            if (string.Equals(file, excludePath, StringComparison.Ordinal)) continue;
            Match m = Regex.Match(Path.GetFileName(file), @"^(\d+)-");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int n))
                max = Math.Max(max, n);
        }
        return max + 1;
    }

    /// <summary>Append <c>-2</c>, <c>-3</c>, … before the extension until the path is free, so a
    /// rename never clobbers an unrelated existing background.</summary>
    private static string EnsureUniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        string dir = Path.GetDirectoryName(path)!;
        string stem = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            string candidate = Path.Combine(dir, $"{stem}-{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    public void RemoveBackground(Theme theme, string imagePath)
    {
        Log.Debug("RemoveBackground: theme {Name}, path {Path}", theme.Name, imagePath);
        if (File.Exists(imagePath))
        {
            File.Delete(imagePath);
            Log.Debug("Deleted background file {Path}", imagePath);
        }
        else
        {
            Log.Debug("Background file {Path} was already gone from disk", imagePath);
        }
        bool removed = theme.Backgrounds.Remove(imagePath);
        Log.Information("Removed background {File} from theme {Name} (tracked={Tracked}, now {Count} background(s))",
            Path.GetFileName(imagePath), theme.Name, removed, theme.Backgrounds.Count);
    }

    /// <summary>
    /// Set the theme's picker image by re-encoding <paramref name="sourceImagePath"/> (e.g. a
    /// screenshot) to <c>preview.png</c> in the theme folder. Omarchy's theme menu shows this file,
    /// falling back to <c>preview.jpg</c> then the first background. We always write PNG under the
    /// exact name Omarchy checks first, so any source format the user picks lands correctly.
    /// Returns the written path.
    /// </summary>
    public string SetPreviewImage(Theme theme, string sourceImagePath)
    {
        if (theme.Path is null)
            throw new InvalidOperationException("Save the theme before setting a preview image.");
        if (!File.Exists(sourceImagePath))
            throw new FileNotFoundException("Preview source image not found.", sourceImagePath);

        using SKBitmap src = SKBitmap.Decode(sourceImagePath)
            ?? throw new InvalidOperationException("Could not decode the selected image.");
        using SKImage image = SKImage.FromBitmap(src);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);

        string dest = Path.Combine(theme.Path, "preview.png");
        using (FileStream fs = File.Create(dest))
            data.SaveTo(fs);

        Log.Information("Set preview image for theme {Name} from {Source} ({Bytes} bytes)",
            theme.Name, sourceImagePath, FileLength(dest));
        return dest;
    }

    /// <summary>Delete a user theme's folder from disk. Refuses built-in themes.</summary>
    public void Delete(Theme theme)
    {
        if (theme.IsBuiltIn)
            throw new InvalidOperationException("Built-in themes cannot be deleted.");
        if (theme.Path is null)
            throw new InvalidOperationException("Theme has no path.");
        // Defense in depth: never delete anything outside the user themes directory.
        if (!IsUnder(theme.Path, UserThemesDir))
            throw new InvalidOperationException("Refusing to delete outside the user themes directory.");
        Log.Information("Deleting theme {Name} at {Path}", theme.Name, theme.Path);
        if (Directory.Exists(theme.Path))
            Directory.Delete(theme.Path, recursive: true);
    }

    private static bool IsUnder(string path, string root)
    {
        string full = Path.GetFullPath(path);
        string rootFull = Path.GetFullPath(root);
        return full.StartsWith(rootFull, StringComparison.Ordinal);
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (string file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (string sub in Directory.EnumerateDirectories(source))
        {
            // Skip a nested .git so the clone is a clean, unversioned theme folder.
            if (Path.GetFileName(sub) == ".git") continue;
            CopyDirectory(sub, Path.Combine(dest, Path.GetFileName(sub)));
        }
    }
}
