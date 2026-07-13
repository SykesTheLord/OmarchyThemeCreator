using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmarchyThemeCreator.Models;
using OmarchyThemeCreator.Services;
using Serilog;
using SkiaSharp;

namespace OmarchyThemeCreator.ViewModels;

/// <summary>A wallhaven search result with its lazily-loaded thumbnail.</summary>
public sealed partial class WallhavenItem : ObservableObject
{
    public WallhavenResult Result { get; }
    public string Resolution => Result.Resolution;

    [ObservableProperty] private Bitmap? _thumb;

    /// <summary>Whether this result is ticked in the multi-select results grid.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>The analyzed color make-up of this image (prominent wallhaven colors + a weighted
    /// representative palette); null until the thumbnail has been analyzed. Drives both the manual
    /// multi-swatch filter and theme matching.</summary>
    [ObservableProperty] private ImageColors? _colors;

    public WallhavenItem(WallhavenResult result) => Result = result;
}

/// <summary>A togglable filter value (e.g. an aspect ratio) shown as a checkbox.</summary>
public sealed partial class SelectableOption : ObservableObject
{
    /// <summary>The value sent to wallhaven, e.g. <c>16x9</c>.</summary>
    public string Value { get; }
    /// <summary>The human-friendly label shown in the UI, e.g. <c>16:9</c>.</summary>
    public string Label { get; }

    [ObservableProperty] private bool _isSelected;

    public SelectableOption(string value, string label)
    {
        Value = value;
        Label = label;
    }
}

/// <summary>
/// Wallpaper tab: search/download wallpapers from wallhaven.cc and apply raster filters (the
/// image editor). Downloaded and edited images can be staged as backgrounds and fed to the
/// Extract tab.
/// </summary>
public sealed partial class WallpaperViewModel : ObservableObject
{
    private static readonly ILogger Log = Serilog.Log.ForContext<WallpaperViewModel>();

    private readonly WallhavenService _wallhaven;
    private readonly WallpaperColorAnalyzer _colorAnalyzer;
    private readonly ImageEditService _editor;
    private readonly SettingsService _settings;
    private readonly IPaletteHost _host;
    private readonly Action<string> _onWallpaperReady;

    public WallpaperViewModel(
        WallhavenService wallhaven, WallpaperColorAnalyzer colorAnalyzer, ImageEditService editor,
        SettingsService settings, IPaletteHost host, Action<string> onWallpaperReady)
    {
        _wallhaven = wallhaven;
        _colorAnalyzer = colorAnalyzer;
        _editor = editor;
        _settings = settings;
        _host = host;
        _onWallpaperReady = onWallpaperReady;
        PresetNames = _editor.PresetNames;
        _apiKey = settings.Current.WallhavenApiKey ?? "";
        ColorSwatches = WallhavenService.Colors.Select(c => new SelectableOption(c, c)).ToList();
    }

    // ---- Wallhaven search ---------------------------------------------

    /// <summary>The visible (color-filtered) results the grid binds to. Backed by
    /// <see cref="_allResults"/>, which holds every fetched result regardless of the filter.</summary>
    public ObservableCollection<WallhavenItem> Results { get; } = new();

    /// <summary>Every result fetched so far this search, in order, unfiltered.</summary>
    private readonly List<WallhavenItem> _allResults = new();

    public IReadOnlyList<string> SortOptions { get; } = new[]
        { "relevance", "random", "date_added", "views", "favorites", "toplist" };
    // Wallhaven's `ratios` param accepts a comma-separated list, so this is a multi-select.
    public IReadOnlyList<SelectableOption> RatioOptions { get; } = new[]
    {
        new SelectableOption("16x9", "16:9"),
        new SelectableOption("16x10", "16:10"),
        new SelectableOption("21x9", "21:9"),
        new SelectableOption("4x3", "4:3"),
        new SelectableOption("5x4", "5:4"),
        new SelectableOption("1x1", "1:1"),
        new SelectableOption("9x16", "9:16"),
        new SelectableOption("10x16", "10:16"),
    };
    public IReadOnlyList<string> ResolutionOptions { get; } = new[]
        { "", "1920x1080", "2560x1440", "3840x2160", "1366x768", "1280x800", "1080x1920" };

    /// <summary>The wallhaven palette as togglable swatches. Unlike wallhaven's own single-color
    /// filter, several can be ticked at once; results are then analyzed locally to keep only those
    /// that actually contain every ticked color (see <see cref="MatchesOnly"/>).</summary>
    public IReadOnlyList<SelectableOption> ColorSwatches { get; }

    /// <summary>The ticked swatch colors (hex, no '#'), in palette order.</summary>
    private IReadOnlyList<string> SelectedColors =>
        ColorSwatches.Where(o => o.IsSelected).Select(o => o.Value).ToList();

    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private WallhavenItem? _selectedResult;
    [ObservableProperty] private bool _searching;

    // Purity
    [ObservableProperty] private bool _sfw = true;
    [ObservableProperty] private bool _sketchy;
    [ObservableProperty] private bool _nsfw;

    // Categories
    [ObservableProperty] private bool _general = true;
    [ObservableProperty] private bool _anime = true;
    [ObservableProperty] private bool _people = true;

    // Sorting / resolution / ratio / color
    [ObservableProperty] private string _sorting = "relevance";
    [ObservableProperty] private string _atLeastResolution = "";

    /// <summary>When on (and at least one swatch is ticked), hide fetched results that don't
    /// contain every ticked color once their thumbnail has been analyzed. Turning it off shows the
    /// raw wallhaven results while keeping the ticked colors as the server-side hint.</summary>
    [ObservableProperty] private bool _matchesOnly = true;

    partial void OnMatchesOnlyChanged(bool value) => RebuildResults();

    /// <summary>When on, results are scored against the current theme palette (analyzed per image)
    /// and only close matches are kept. This supersedes the manual swatch filter and, unlike it,
    /// picks no visible swatch — the whole theme drives the match.</summary>
    [ObservableProperty] private bool _matchTheme;

    partial void OnMatchThemeChanged(bool value) => RebuildResults();

    /// <summary>Comma-joined values of the checked ratio options, or null when none are.</summary>
    private string? SelectedRatios
    {
        get
        {
            string joined = string.Join(",",
                RatioOptions.Where(o => o.IsSelected).Select(o => o.Value));
            return joined.Length == 0 ? null : joined;
        }
    }

    // API key
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NsfwAvailable))]
    private string _apiKey;

    public bool NsfwAvailable => !string.IsNullOrWhiteSpace(ApiKey);

    // Paging
    private int _currentPage = 1;
    private int _lastPage = 1;
    public bool CanLoadMore => _currentPage < _lastPage;

    [ObservableProperty] private bool _resultsExpanded = true;

    // Size (px) of the result thumbnails, driven by a slider so the user can enlarge images to
    // inspect them before picking. Height derives from a 3:2 landscape ratio; NotifyPropertyChangedFor
    // makes the XAML-bound ThumbnailHeight update whenever the slider moves.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThumbnailHeight))]
    private double _thumbnailSize = 120;

    public double ThumbnailHeight => ThumbnailSize * 2 / 3;

    private WallpaperFilter BuildFilter() => new()
    {
        Query = SearchQuery,
        Sfw = Sfw, Sketchy = Sketchy, Nsfw = Nsfw,
        General = General, Anime = Anime, People = People,
        Sorting = Sorting,
        AtLeastResolution = string.IsNullOrWhiteSpace(AtLeastResolution) ? null : AtLeastResolution,
        Ratio = SelectedRatios,
        // wallhaven's `colors` param takes a single value. In theme-match mode send the accent's
        // nearest wallhaven color as a hidden server-side hint (no visible swatch is picked); the
        // full-palette scoring still decides. Otherwise send the first ticked swatch, with any
        // additional ticked colors enforced locally by the analyzer.
        Color = MatchTheme
            ? WallhavenService.NearestColor(_host.CurrentPalette.Accent)
            : SelectedColors.FirstOrDefault(),
    };

    /// <summary>Minimum theme-match score (0..1) a result must reach to survive the theme filter.</summary>
    private const double ThemeMatchCutoff = 0.4;

    /// <summary>Target visible-result count the first search auto-loads toward before giving up.</summary>
    private const int TargetResults = 20;

    /// <summary>Cap on pages auto-loaded per search, so a very restrictive filter can't spin forever.</summary>
    private const int MaxAutoLoadPages = 8;

    /// <summary>True when the manual swatch filter should hide non-matching results. Only engages for
    /// 2+ ticked colors (wallhaven filters a single color server-side) and never while theme match is
    /// on, which takes over the color filtering entirely.</summary>
    private bool ColorFilterActive => !MatchTheme && MatchesOnly && SelectedColors.Count >= 2;

    /// <summary>Whether any local color filter is active, so analysis completions need to re-filter.</summary>
    private bool AnyFilterActive => MatchTheme || ColorFilterActive;

    /// <summary>The current theme's colors as RGB targets for theme matching (accent, the six ANSI
    /// hues, plus background/foreground), deduped. Read fresh so it reflects live palette edits.</summary>
    private IReadOnlyList<ColorMath.Rgb> ThemeTargets()
    {
        ThemeColors p = _host.CurrentPalette;
        IEnumerable<string> hexes = new[]
        {
            p.Accent, p.Background, p.Foreground,
            p.Ansi[1], p.Ansi[2], p.Ansi[3], p.Ansi[4], p.Ansi[5], p.Ansi[6],
        };
        return hexes
            .Select(h => ThemeColors.NormalizeHex(h) ?? "#000000")
            .Distinct()
            .Select(ColorMath.ToRgb)
            .ToList();
    }

    /// <summary>Whether a result passes the active color filter. Results whose thumbnail hasn't been
    /// analyzed yet (Colors still null) are shown optimistically so the grid populates immediately;
    /// they're re-evaluated when their analysis lands. Theme match supersedes the swatch filter.</summary>
    private bool Matches(WallhavenItem item, IReadOnlyList<ColorMath.Rgb> themeTargets)
    {
        if (item.Colors is null) return true;
        if (MatchTheme)
            return WallpaperColorAnalyzer.ThemeMatch(item.Colors, themeTargets) >= ThemeMatchCutoff;
        if (ColorFilterActive)
            return SelectedColors.All(c => item.Colors.Dominant.Contains(c));
        return true;
    }

    /// <summary>Recompute the visible <see cref="Results"/> from <see cref="_allResults"/> under the
    /// current filter. Cheap (a page is ~24 items) so it's fine to call per analysis result.</summary>
    private void RebuildResults()
    {
        IReadOnlyList<ColorMath.Rgb> targets = MatchTheme ? ThemeTargets() : Array.Empty<ColorMath.Rgb>();
        Results.Clear();
        foreach (WallhavenItem item in _allResults)
            if (Matches(item, targets)) Results.Add(item);
        OnPropertyChanged(nameof(CanLoadMore));
    }

    [RelayCommand]
    private async Task Search()
    {
        Searching = true;
        try
        {
            // Fetch page 1, then — because a color/theme filter can prune most of a page — keep
            // pulling further pages until enough results survive the filter or wallhaven runs out.
            // We await each page's analysis (Task.WhenAll) so Results.Count is the real post-filter
            // count before deciding whether to load more. Bounded by MaxAutoLoadPages.
            List<Task> tasks = await FetchPageAsync(1, replace: true);
            for (int pages = 1; tasks.Count > 0; pages++)
            {
                await Task.WhenAll(tasks);
                if (Results.Count >= TargetResults || !CanLoadMore || pages >= MaxAutoLoadPages)
                    break;
                tasks = await FetchPageAsync(_currentPage + 1, replace: false);
            }
            // Leave a failed fetch's error message in place; only summarize when something came back.
            if (_allResults.Count > 0) ReportSearchStatus();
        }
        finally
        {
            Searching = false;
        }
    }

    [RelayCommand]
    private async Task LoadMore()
    {
        if (!CanLoadMore) return;
        Searching = true;
        try
        {
            List<Task> tasks = await FetchPageAsync(_currentPage + 1, replace: false);
            await Task.WhenAll(tasks);
            ReportSearchStatus();
        }
        finally
        {
            Searching = false;
        }
    }

    /// <summary>Fetch one page of results, appending them (or replacing on a fresh search), kicking
    /// off each thumbnail's load+analysis. Returns those per-item tasks so the caller can await the
    /// page settling before deciding whether to auto-load more. Does not toggle <see cref="Searching"/>
    /// (the command owns that across the whole auto-load loop).</summary>
    private async Task<List<Task>> FetchPageAsync(int page, bool replace)
    {
        if (replace)
        {
            _allResults.Clear();
            Results.Clear();
        }

        string? key = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey;
        WallhavenPage result;
        try
        {
            result = await _wallhaven.SearchAsync(BuildFilter(), key, page);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "wallhaven search failed (page {Page})", page);
            _host.SetStatus(ex.Message);
            return new List<Task>();
        }

        _currentPage = page;
        _lastPage = result.LastPage;
        IReadOnlyList<ColorMath.Rgb> targets = MatchTheme ? ThemeTargets() : Array.Empty<ColorMath.Rgb>();
        List<Task> tasks = new List<Task>(result.Items.Count);
        bool addedAny = false;
        foreach (WallhavenResult r in result.Items)
        {
            WallhavenItem item = new WallhavenItem(r);
            _allResults.Add(item);
            if (Matches(item, targets)) Results.Add(item); // optimistic: shown until analysis proves otherwise
            tasks.Add(LoadThumbAsync(item));
            addedAny = true;
        }
        OnPropertyChanged(nameof(CanLoadMore));
        if (addedAny) ResultsExpanded = true;
        Log.Information("wallhaven returned {Count} result(s), page {Page}/{Last}",
            _allResults.Count, _currentPage, _lastPage);
        return tasks;
    }

    /// <summary>Status line summarizing what wallhaven returned and, when a color filter is on, how
    /// many results survived it.</summary>
    private void ReportSearchStatus()
    {
        string filterNote =
            MatchTheme ? $" · theme match: {Results.Count} of {_allResults.Count} shown"
            : ColorFilterActive ? $" · color match: {Results.Count} of {_allResults.Count} shown"
            : "";
        _host.SetStatus($"wallhaven: page {_currentPage}/{_lastPage}, {_allResults.Count} fetched.{filterNote}");
    }

    [RelayCommand]
    private void SelectColor(SelectableOption swatch)
    {
        swatch.IsSelected = !swatch.IsSelected;
        RebuildResults();
    }

    [RelayCommand]
    private void SaveApiKey()
    {
        _settings.Current.WallhavenApiKey = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey.Trim();
        _settings.Save();
        if (!NsfwAvailable) Nsfw = false;
        _host.SetStatus(NsfwAvailable ? "Saved wallhaven API key (NSFW enabled)." : "Cleared wallhaven API key.");
    }

    private async Task LoadThumbAsync(WallhavenItem item)
    {
        byte[]? bytes = await _wallhaven.TryGetBytesAsync(item.Result.ThumbUrl);
        if (bytes is null) return;
        try
        {
            using MemoryStream ms = new MemoryStream(bytes);
            item.Thumb = new Bitmap(ms);
        }
        catch { /* ignore undecodable thumb */ }

        // Analyze the (small) thumbnail off the UI thread so the color/theme filters have real data;
        // the continuation resumes on the UI thread, where touching Results is safe.
        ImageColors colors = await Task.Run(() => _colorAnalyzer.Analyze(bytes));
        item.Colors = colors;
        if (AnyFilterActive) RebuildResults();
    }

    [RelayCommand]
    private async Task Download()
    {
        if (SelectedResult is null)
        {
            Log.Debug("Download command: no result selected; ignoring");
            return;
        }
        Log.Information("Download command invoked for wallhaven {Id} ({Url})",
            SelectedResult.Result.Id, SelectedResult.Result.FullUrl);
        try
        {
            string path = await _wallhaven.DownloadAsync(SelectedResult.Result.FullUrl, _host.StagingDir);
            Log.Information("Downloaded wallhaven {Id} to {Path}", SelectedResult.Result.Id, path);
            _host.SetStatus($"Downloaded {Path.GetFileName(path)}.");
            LoadIntoEditor(path);
            _onWallpaperReady(path);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Download failed for wallhaven {Id}", SelectedResult.Result.Id);
            _host.SetStatus(ex.Message);
        }
    }

    /// <summary>
    /// Downloads every ticked result and copies it into the current theme's backgrounds. Stops
    /// early if the theme hasn't been saved yet (AddBackground reports that itself); per-image
    /// download failures are surfaced but don't abort the rest of the batch.
    /// </summary>
    [RelayCommand]
    private async Task AddSelectedToTheme()
    {
        List<WallhavenItem> selected = Results.Where(r => r.IsSelected).ToList();
        Log.Information("AddSelectedToTheme invoked: {Count} wallpaper(s) ticked", selected.Count);
        if (selected.Count == 0)
        {
            _host.SetStatus("No wallpapers selected.");
            return;
        }

        Searching = true;
        try
        {
            // Downloads are network-bound and used to run one-at-a-time, so clicking "Add selected"
            // stalled on each full-res image in turn. Fire them all off together and await the batch
            // instead — the wall-clock cost collapses to roughly the slowest single download.
            _host.SetStatus($"Downloading {selected.Count} wallpaper(s)…");
            (WallhavenItem item, string? path)[] downloads = await Task.WhenAll(
                selected.Select(async item =>
                {
                    try
                    {
                        Log.Debug("Downloading wallhaven {Id} from {Url}", item.Result.Id, item.Result.FullUrl);
                        return (item, path: (string?)await _wallhaven.DownloadAsync(
                            item.Result.FullUrl, _host.StagingDir));
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to download wallhaven {Id}", item.Result.Id);
                        return (item, path: (string?)null);
                    }
                }));

            // Add sequentially: AddBackground mutates the theme (and may pop a naming dialog), which
            // must happen one at a time on the UI thread.
            int added = 0, duplicates = 0, failed = 0;
            foreach ((WallhavenItem item, string? path) in downloads)
            {
                if (path is null)
                {
                    failed++;
                    _host.SetStatus($"Failed to download {item.Result.Id}.");
                    continue;
                }
                BackgroundAddResult? result = await _host.AddBackground(path);
                if (result is null)
                {
                    // theme not saved yet; AddBackground set the status. This aborts the batch.
                    Log.Warning("Aborting AddSelectedToTheme after {Added} added: host rejected background " +
                        "(theme likely not saved)", added);
                    return;
                }
                if (result.Value.WasNew) added++; else duplicates++;
            }
            Log.Information("AddSelectedToTheme finished: {Added} new, {Duplicates} already present, {Failed} failed, of {Total} selected",
                added, duplicates, failed, selected.Count);
            if (added > 0 || duplicates > 0)
                _host.SetStatus(duplicates == 0
                    ? $"Added {added} wallpaper(s) to the theme's backgrounds."
                    : $"Added {added} new wallpaper(s); {duplicates} were already backgrounds of this theme.");
        }
        finally
        {
            Searching = false;
        }
    }

    // ---- Image editor -------------------------------------------------

    public IReadOnlyList<string> PresetNames { get; }

    [ObservableProperty] private string? _editorImagePath;
    [ObservableProperty] private Bitmap? _editedPreview;
    [ObservableProperty] private bool _editorExpanded;

    [ObservableProperty] private double _brightness = 1;
    [ObservableProperty] private double _contrast = 1;
    [ObservableProperty] private double _saturation = 1;
    [ObservableProperty] private double _blur;
    [ObservableProperty] private double _vignette;
    [ObservableProperty] private double _grain;

    [RelayCommand]
    private async Task PickImage()
    {
        string? path = await _host.PickImageAsync();
        if (string.IsNullOrEmpty(path)) return;
        LoadIntoEditor(path);
    }

    private void LoadIntoEditor(string path)
    {
        EditorImagePath = path;
        EditedPreview = BitmapInterop.TryLoad(path);
        EditorExpanded = true;
    }

    [RelayCommand]
    private void ApplyPreset(string name)
    {
        ImageEditOptions o = _editor.Preset(name);
        Brightness = o.Brightness;
        Contrast = o.Contrast;
        Saturation = o.Saturation;
        Blur = o.Blur;
        Vignette = o.Vignette;
        Grain = o.Grain;
        Render(o);
    }

    [RelayCommand]
    private void Render() => Render(CurrentOptions());

    private void Render(ImageEditOptions options)
    {
        if (string.IsNullOrEmpty(EditorImagePath)) return;
        try
        {
            using SKBitmap edited = _editor.Apply(EditorImagePath, options);
            EditedPreview = BitmapInterop.ToAvalonia(edited);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Image edit render failed for {Path}", EditorImagePath);
            _host.SetStatus("Edit failed: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveToBackgrounds()
    {
        if (string.IsNullOrEmpty(EditorImagePath))
        {
            Log.Debug("SaveToBackgrounds: no image in the editor; ignoring");
            return;
        }
        Log.Information("SaveToBackgrounds invoked for {Path}", EditorImagePath);
        try
        {
            string outPath;
            using (SKBitmap edited = _editor.Apply(EditorImagePath, CurrentOptions()))
            {
                string stem = Path.GetFileNameWithoutExtension(EditorImagePath);
                outPath = Path.Combine(_host.StagingDir, $"{stem}-edited.png");
                _editor.Save(edited, outPath);
            }
            Log.Debug("Rendered edited wallpaper to staging file {OutPath}", outPath);

            string dest = (await _host.AddBackground(outPath))?.Path ?? outPath;
            _onWallpaperReady(dest);
            Log.Information("Saved edited wallpaper to {Dest}", dest);
            _host.SetStatus($"Saved edited wallpaper: {Path.GetFileName(dest)}.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Saving edited wallpaper failed for {Path}", EditorImagePath);
            _host.SetStatus("Save failed: " + ex.Message);
        }
    }

    private ImageEditOptions CurrentOptions() => new()
    {
        Brightness = (float)Brightness,
        Contrast = (float)Contrast,
        Saturation = (float)Saturation,
        Blur = (float)Blur,
        Vignette = (float)Vignette,
        Grain = (float)Grain,
        ToneColor = SKColors.Transparent,
    };
}
