using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmarchyThemeCreator.Models;
using OmarchyThemeCreator.Services;
using Serilog;

namespace OmarchyThemeCreator.ViewModels;

/// <summary>
/// "Current Wallpapers" tab: manages the wallpapers already in the open theme — rename, remove,
/// and edit (which hands off to the Wallpaper tab's image editor). Its <see cref="Items"/>
/// collection is the single source of truth for the open theme's backgrounds; the host reads it
/// when building/saving the theme.
/// </summary>
public sealed partial class CurrentWallpapersViewModel : ObservableObject
{
    private static readonly ILogger Log = Serilog.Log.ForContext<CurrentWallpapersViewModel>();

    private const int ThumbnailWidth = 240;

    private readonly ThemeRepository _repo;
    private readonly ImageEditService _editor;
    private readonly MainWindowViewModel _host;

    private Theme? _theme; // same object as the host's current theme; carries Path for asset ops.

    public ObservableCollection<WallpaperItem> Items { get; } = new();

    [ObservableProperty] private WallpaperItem? _selected;

    /// <summary>True when the theme has no wallpapers — drives the empty-state hint.</summary>
    public bool IsEmpty => Items.Count == 0;

    public CurrentWallpapersViewModel(ThemeRepository repo, ImageEditService editor, MainWindowViewModel host)
    {
        _repo = repo;
        _editor = editor;
        _host = host;
        // ObservableCollection raises CollectionChanged, but a bool derived from its Count needs
        // its own change notification for the UI to re-evaluate. We also re-flag the default (first)
        // wallpaper on every add/remove/reorder from the same hook.
        Items.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            RefreshDefaultFlags();
        };
    }

    /// <summary>Mark the first wallpaper as the theme's default (the one the Omarchy switcher starts
    /// on) and clear the flag on the rest, so the "Default" badge follows the list order.</summary>
    private void RefreshDefaultFlags()
    {
        for (int i = 0; i < Items.Count; i++)
            Items[i].IsDefault = i == 0;
    }

    /// <summary>Rebuild the grid for a freshly-loaded (or saved) theme.</summary>
    public void Load(Theme theme)
    {
        _theme = theme;
        Items.Clear();
        foreach (string path in theme.Backgrounds)
            Items.Add(new WallpaperItem(path, BitmapInterop.TryLoadThumbnail(path, ThumbnailWidth)));
    }

    /// <summary>Append a background that was added elsewhere (e.g. the editor's Save to backgrounds
    /// or the Add flow) without a full reload, de-duplicating by path.</summary>
    public void AddExisting(string path)
    {
        if (Items.Any(i => i.Path == path)) return;
        Items.Add(new WallpaperItem(path, BitmapInterop.TryLoadThumbnail(path, ThumbnailWidth)));
    }

    [RelayCommand]
    private Task Add() => _host.AddBackgroundsCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task Rename(WallpaperItem? item)
    {
        if (item is null || _theme?.Path is null)
        {
            _host.SetStatus("Save the theme before renaming backgrounds.");
            return;
        }
        if (_host.PromptForNameAsync is null) return;

        string suggestion = Theme.ToDisplayName(
            System.IO.Path.GetFileNameWithoutExtension(item.Path));
        string? chosen = await _host.PromptForNameAsync(
            "Rename background",
            "Give this wallpaper a new name. It keeps its ordering prefix so the Omarchy background " +
            "switcher still cycles the wallpapers in order.",
            suggestion,
            item.Path);
        if (string.IsNullOrWhiteSpace(chosen)) return;

        string newPath = _repo.RenameBackground(_theme, item.Path, chosen);
        item.Path = newPath;
        _host.SetStatus($"Renamed background to {item.FileName}.");
    }

    [RelayCommand]
    private void MoveUp(WallpaperItem? item) => Reorder(item, -1);

    [RelayCommand]
    private void MoveDown(WallpaperItem? item) => Reorder(item, +1);

    /// <summary>Shift a wallpaper one slot up or down and persist the new order to disk. Order is the
    /// Omarchy switcher's cycle order, so moving a wallpaper to the top makes it the default. The
    /// files are renumbered (their <c>NN-</c> prefix rewritten), so each item's path is refreshed.</summary>
    private void Reorder(WallpaperItem? item, int delta)
    {
        if (item is null || _theme?.Path is null)
        {
            _host.SetStatus("Save the theme before reordering backgrounds.");
            return;
        }
        int from = Items.IndexOf(item);
        int to = from + delta;
        if (from < 0 || to < 0 || to >= Items.Count) return;

        Items.Move(from, to);

        IReadOnlyList<string> newPaths = _repo.ReorderBackgrounds(
            _theme, Items.Select(i => i.Path).ToList());
        // The renumber renamed the files; point each item at its new on-disk path (which also
        // refreshes DisplayName/FileName).
        for (int i = 0; i < Items.Count; i++)
            Items[i].Path = newPaths[i];

        Selected = item;
        _host.SetStatus(to == 0
            ? $"{item.DisplayName} is now the default wallpaper."
            : "Reordered wallpapers.");
    }

    [RelayCommand]
    private async Task Remove(WallpaperItem? item)
    {
        if (item is null || _theme is null) return;

        bool ok = _host.ConfirmAsync is null
                 || await _host.ConfirmAsync("Remove wallpaper",
                     $"Remove '{item.FileName}' from this theme? The file is deleted from the theme's " +
                     "backgrounds folder.");
        if (!ok) return;

        _repo.RemoveBackground(_theme, item.Path);
        Items.Remove(item);
        Log.Information("Removed background {File} from theme {Theme}", item.FileName, _theme.Name);
        _host.SetStatus($"Removed {item.FileName}.");
    }

    [RelayCommand]
    private async Task PurgeAll()
    {
        if (_theme is null || Items.Count == 0)
        {
            _host.SetStatus("No wallpapers to purge.");
            return;
        }

        int count = Items.Count;
        bool ok = _host.ConfirmAsync is null
                 || await _host.ConfirmAsync("Purge all wallpapers",
                     $"Delete all {count} wallpaper(s) from this theme? Their files are removed from the " +
                     "theme's backgrounds folder. This cannot be undone.");
        if (!ok) return;

        // Snapshot the list first: RemoveBackground mutates theme.Backgrounds as we go.
        foreach (WallpaperItem item in Items.ToList())
            _repo.RemoveBackground(_theme, item.Path);
        Items.Clear();
        Log.Information("Purged {Count} background(s) from theme {Theme}", count, _theme.Name);
        _host.SetStatus($"Purged {count} wallpaper(s).");
    }

    [RelayCommand]
    private async Task Edit(WallpaperItem? item)
    {
        if (item is null || _host.ShowImageEditorAsync is null) return;

        ImageEditorViewModel editor = new ImageEditorViewModel(item.Path, _editor);
        try
        {
            bool saved = await _host.ShowImageEditorAsync(editor);
            if (!saved) return;

            // Re-apply the chosen edits at full resolution and overwrite the background in place.
            // The dialog's live preview ran on a downscaled copy; this is the real, final render.
            using (SkiaSharp.SKBitmap rendered = _editor.Apply(item.Path, editor.Options))
                _editor.Save(rendered, item.Path);

            // The file changed on disk — reload its thumbnail so the grid reflects the edit.
            item.Thumb = BitmapInterop.TryLoadThumbnail(item.Path, ThumbnailWidth);
            Log.Information("Saved edits to background {File}", item.FileName);
            _host.SetStatus($"Saved edits to {item.DisplayName}.");
        }
        catch (System.Exception ex)
        {
            Log.Warning(ex, "Editing background {Path} failed", item.Path);
            _host.SetStatus("Edit failed: " + ex.Message);
        }
        finally
        {
            editor.Dispose();
        }
    }

    [RelayCommand]
    private async Task Preview(WallpaperItem? item)
    {
        if (item is null) return;
        await _host.PreviewImage(item.Path);
    }
}
