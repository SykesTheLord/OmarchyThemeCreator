using System.Collections.Generic;
using System.Threading.Tasks;
using OmarchyThemeCreator.Models;
using OmarchyThemeCreator.Services;

namespace OmarchyThemeCreator.ViewModels;

/// <summary>
/// The surface the per-tab view-models use to push results back into the main editor. Every
/// palette change funnels through <see cref="ApplyPalette"/> so the live preview, undo history,
/// and contrast panel stay in sync no matter which tab produced the change.
/// </summary>
public interface IPaletteHost
{
    /// <summary>The palette the tabs read when they need the current colors as a starting point.</summary>
    ThemeColors CurrentPalette { get; }

    /// <summary>
    /// Replace the working palette (snapshotting history) and refresh preview/contrast. When
    /// <paramref name="coalesceKey"/> repeats (e.g. a slider drag re-extracting the same
    /// wallpaper), the change folds into the previous undo step instead of adding a new one.
    /// </summary>
    void ApplyPalette(ThemeColors next, string status, string? coalesceKey = null);

    void SetLightMode(bool light);
    void SetStatus(string status);

    /// <summary>Pick a single image file (returns local path or null).</summary>
    Task<string?> PickImageAsync();

    /// <summary>Pick any single file (used for Base16 .yaml import).</summary>
    Task<string?> PickFileAsync();

    /// <summary>
    /// Copy an image into the current theme's backgrounds/ (must be saved first), prompting the
    /// user to name a genuinely-new background and renaming it to the Omarchy switcher convention.
    /// Returns the add result (final path + whether it was newly added), or null if the theme isn't
    /// saved or the copy failed.
    /// </summary>
    Task<BackgroundAddResult?> AddBackground(string sourcePath);

    /// <summary>A folder edited/downloaded wallpapers can be staged in before a theme is saved.</summary>
    string StagingDir { get; }
}
