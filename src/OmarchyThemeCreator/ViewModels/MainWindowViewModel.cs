using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmarchyThemeCreator.Models;
using OmarchyThemeCreator.Services;
using Serilog;

namespace OmarchyThemeCreator.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IPaletteHost
{
    private static readonly ILogger Log = Serilog.Log.ForContext<MainWindowViewModel>();

    private readonly ThemeRepository _repo;
    private readonly OmarchyCliService _cli;
    private readonly IconThemeService _iconService;
    private readonly ExportService _export;

    private ThemeColors _working = new();
    private Theme? _current; // the loaded/saved theme (carries Path for asset ops)

    // Undo/redo of palette snapshots (bounded). A run of manual field edits coalesces into one.
    private const int HistoryLimit = 50;
    private readonly LinkedList<ThemeColors> _undo = new();
    private readonly Stack<ThemeColors> _redo = new();
    private bool _editBurstActive;

    // Set by the View so the VM can drive pickers and render the live preview control.
    public Func<Task<IReadOnlyList<string>>>? PickImagesAsync { get; set; }
    public Func<Task<string?>>? PickSingleImageHook { get; set; }
    public Func<Task<string?>>? PickFileForImportHook { get; set; }
    public Func<Task<string?>>? PickFolderAsync { get; set; }
    public Func<Control?>? PreviewControlProvider { get; set; }
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }
    // (title, message, defaultText, imagePath?) -> entered name, or null if the user skips/cancels.
    // imagePath, when non-null, is previewed as a thumbnail in the dialog.
    public Func<string, string, string, string?, Task<string?>>? PromptForNameAsync { get; set; }
    // Show a full-size modal preview of an image on disk.
    public Func<string, Task>? PreviewImageHook { get; set; }
    // (label, initial color) -> chosen color, or null if the user cancels the picker dialog.
    public Func<string, Color, Task<Color?>>? PickColorAsync { get; set; }
    // Show the modal wallpaper editor for the given VM; true when the user saves their edits.
    public Func<ImageEditorViewModel, Task<bool>>? ShowImageEditorAsync { get; set; }

    public ObservableCollection<Theme> Themes { get; } = new();
    public ObservableCollection<ColorField> Colors { get; } = new();
    public ObservableCollection<string> IconThemes { get; } = new();
    public PreviewViewModel Preview { get; } = new();

    // Per-tab view-models (built from the injected services).
    public ExtractViewModel Extract { get; }
    public PresetsViewModel Presets { get; }
    public WallpaperViewModel Wallpaper { get; }
    public ContrastViewModel Contrast { get; } = new();
    public CurrentWallpapersViewModel CurrentWallpapers { get; }

    [ObservableProperty] private int _selectedTabIndex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedCommand))]
    private Theme? _selectedTheme;
    [ObservableProperty] private string _themeName = "my-theme";
    [ObservableProperty] private string? _selectedIconTheme;
    [ObservableProperty] private Bitmap? _iconPreview;
    [ObservableProperty] private bool _lightMode;
    [ObservableProperty] private string _statusText = "Ready.";

    public bool OmarchyAvailable => _cli.IsAvailable;
    public bool CanApply => _cli.IsAvailable;

    public MainWindowViewModel(
        ThemeRepository repo, OmarchyCliService cli,
        IconThemeService iconService, ExportService export,
        PaletteExtractionService extractor, PresetService presets,
        WallhavenService wallhaven, WallpaperColorAnalyzer colorAnalyzer,
        ImageEditService imageEditor, SettingsService settings)
    {
        _repo = repo;
        _cli = cli;
        _iconService = iconService;
        _export = export;

        Extract = new ExtractViewModel(extractor, this);
        Presets = new PresetsViewModel(presets, this);
        Wallpaper = new WallpaperViewModel(wallhaven, colorAnalyzer, imageEditor, settings, this, path => Extract.LoadWallpaper(path));
        CurrentWallpapers = new CurrentWallpapersViewModel(_repo, imageEditor, this);

        foreach (string icon in _iconService.ListIconThemes())
            IconThemes.Add(icon);

        RefreshThemes();
        LoadThemeIntoEditor(new Theme()); // start on a fresh default theme

        if (!_cli.IsAvailable)
            StatusText = "Ready. (Omarchy CLI not found — 'Apply' disabled.)";
    }

    /// <summary>Staging folder for downloaded/edited wallpapers before a theme is saved.</summary>
    public string StagingDir { get; } = EnsureStagingDir();

    private static string EnsureStagingDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "omarchy-theme-creator");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void RefreshThemes()
    {
        Themes.Clear();
        foreach (Theme t in _repo.ListThemes())
            Themes.Add(t);
    }

    private void LoadThemeIntoEditor(Theme theme)
    {
        _current = theme;
        _working = theme.Colors.Clone();
        ThemeName = theme.Name;
        LightMode = theme.LightMode;
        SelectedIconTheme = theme.IconTheme;
        UpdateIconPreview(); // in case the new theme's icon pack matches the old one (no change event)

        BuildColorFields();

        CurrentWallpapers.Load(theme);

        Preview.Update(_working);
        Contrast.Update(_working);
        ResetHistory();
    }

    // Fired by the generated setter whenever the icon dropdown selection changes.
    partial void OnSelectedIconThemeChanged(string? value) => UpdateIconPreview();

    /// <summary>Load a representative icon from the selected pack for the little preview swatch.</summary>
    private void UpdateIconPreview()
    {
        string? example = string.IsNullOrEmpty(SelectedIconTheme)
            ? null
            : _iconService.FindExampleIcon(SelectedIconTheme);
        IconPreview = example is null ? null : BitmapInterop.TryLoad(example);
    }

    private void BuildColorFields()
    {
        Colors.Clear();
        foreach (KeyValuePair<string, string> pair in _working.AsPairs())
            Colors.Add(new ColorField(pair.Key, LabelFor(pair.Key), pair.Value, OnColorFieldChanged));
    }

    private void OnColorFieldChanged(ColorField field)
    {
        if (field.NormalizedHex is { } hex)
        {
            // Snapshot the pre-edit palette once per run of manual edits.
            if (!_editBurstActive)
            {
                PushUndo(_working.Clone());
                _redo.Clear();
                _lastCoalesceKey = null;
                _editBurstActive = true;
            }
            _working.Set(field.Key, hex);
            Preview.Update(_working);
            Contrast.Update(_working);
        }
    }

    /// <summary>
    /// Open the modal color picker for a swatch and commit the result through <see cref="ApplyPalette"/>
    /// so it lands as a single undo step and refreshes the preview + contrast panel.
    /// </summary>
    [RelayCommand]
    private async Task EditColor(ColorField field)
    {
        if (PickColorAsync is null) return;
        if (await PickColorAsync(field.Label, field.Color) is not Color c) return;

        string hex = $"#{c.R:x2}{c.G:x2}{c.B:x2}";
        if (field.NormalizedHex == hex) return; // unchanged or cancelled-equal

        ThemeColors next = _working.Clone();
        next.Set(field.Key, hex);
        ApplyPalette(next, $"Set {field.Label} to {hex}");
    }

    private static string LabelFor(string key) => key switch
    {
        "accent" => "Accent",
        "cursor" => "Cursor",
        "foreground" => "Foreground",
        "background" => "Background",
        "selection_foreground" => "Selection FG",
        "selection_background" => "Selection BG",
        _ => key.StartsWith("color", StringComparison.Ordinal) ? "ANSI " + key.Substring(5) : key,
    };

    [RelayCommand]
    private void NewTheme()
    {
        LoadThemeIntoEditor(new Theme());
        StatusText = "Started a new theme.";
    }

    [RelayCommand]
    private void OpenSelected()
    {
        if (SelectedTheme?.Path is not { } path)
        {
            StatusText = "Select a theme to open.";
            return;
        }
        Theme loaded = _repo.Load(path);
        LoadThemeIntoEditor(loaded);
        StatusText = loaded.IsBuiltIn
            ? $"Opened built-in '{loaded.DisplayName}'. Saving will create an editable copy."
            : $"Opened '{loaded.DisplayName}'.";
    }

    [RelayCommand]
    private async Task CloneSelectedAsync()
    {
        if (SelectedTheme is not { Path: not null } src)
        {
            StatusText = "Select a theme to clone.";
            return;
        }

        string suggestion = UniqueName(src.Name + "-copy");

        // Ask for the destination name so the copy is created correctly-named up front (no lossy
        // rename afterwards). Fall back to the auto-name when the view hasn't wired the prompt.
        string newName = suggestion;
        if (PromptForNameAsync is not null)
        {
            string? chosen = await PromptForNameAsync(
                "Clone theme",
                $"Name the copy of '{src.DisplayName}'. Its colors and all backgrounds are copied " +
                "into a new, editable theme.",
                suggestion,
                null);
            if (string.IsNullOrWhiteSpace(chosen)) return; // cancelled
            newName = chosen;
        }

        if (_repo.Exists(newName))
        {
            StatusText = $"A theme named '{Theme.ToSlug(newName)}' already exists. Pick another name.";
            return;
        }

        try
        {
            Theme clone = _repo.Clone(src, newName);
            RefreshThemes();
            LoadThemeIntoEditor(clone);
            StatusText = $"Cloned to '{clone.DisplayName}' (with all assets).";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Clone failed for {Name}", src.Name);
            StatusText = "Clone failed: " + ex.Message;
        }
    }

    private bool CanDeleteSelected() => SelectedTheme is { IsBuiltIn: false, Path: not null };

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedTheme is not { IsBuiltIn: false, Path: not null } target)
        {
            StatusText = "Select a user theme to delete.";
            return;
        }

        bool ok = ConfirmAsync is null
                 || await ConfirmAsync("Delete theme",
                     $"Delete '{target.DisplayName}'? This permanently removes its folder and cannot be undone.");
        if (!ok) return;

        try
        {
            _repo.Delete(target);
            RefreshThemes();
            if (_current?.Path == target.Path)
                LoadThemeIntoEditor(new Theme());
            StatusText = $"Deleted '{target.DisplayName}'.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Delete failed for {Name} at {Path}", target.Name, target.Path);
            StatusText = "Delete failed: " + ex.Message;
        }
    }

    /// <summary>Honest one-line summary distinguishing newly-added backgrounds from duplicates
    /// (same filename already in the theme), so "add 4" that only adds 2 isn't reported as 4.</summary>
    private static string BackgroundSummary(int added, int duplicates)
    {
        if (added == 0 && duplicates == 0) return "No backgrounds added.";
        if (duplicates == 0) return $"Added {added} background(s).";
        if (added == 0) return $"All {duplicates} already in the theme (overwrote files).";
        return $"Added {added} background(s); {duplicates} already in the theme.";
    }

    private string UniqueName(string baseName)
    {
        string name = baseName;
        int n = 2;
        while (_repo.Exists(name))
            name = $"{baseName}-{n++}";
        return name;
    }

    [RelayCommand]
    private void Save()
    {
        Log.Information("Save command invoked (name={Name}, existing={IsExisting})",
            ThemeName, _current?.Path is not null);
        try
        {
            Theme theme = BuildThemeFromEditor();
            string dir = _repo.Save(theme);
            _current = theme;
            ThemeName = theme.Name;
            // Re-point the wallpaper grid at the saved theme (a first save assigns its Path).
            CurrentWallpapers.Load(theme);
            RefreshThemes();
            Log.Information("Save command completed: theme {Name} at {Dir}", theme.Name, dir);
            StatusText = $"Saved to {dir}";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Save failed for theme {Name}", ThemeName);
            StatusText = "Save failed: " + ex.Message;
        }
    }

    private Theme BuildThemeFromEditor()
    {
        Theme theme = new Theme
        {
            Name = ThemeName,
            Path = _current?.Path,
            Colors = _working.Clone(),
            IconTheme = string.IsNullOrWhiteSpace(SelectedIconTheme) ? null : SelectedIconTheme,
            LightMode = LightMode,
        };
        foreach (WallpaperItem bg in CurrentWallpapers.Items) theme.Backgrounds.Add(bg.Path);
        Log.Debug("Built theme from editor: name={Name}, path={Path}, icon={Icon}, light={Light}, {BgCount} background(s)",
            theme.Name, theme.Path ?? "<new>", theme.IconTheme ?? "<none>", theme.LightMode, theme.Backgrounds.Count);
        return theme;
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (!_cli.IsAvailable)
        {
            StatusText = "Omarchy CLI not available.";
            return;
        }

        // Ensure the current edits are on disk before applying.
        Save();
        if (_current?.Name is not { } name)
        {
            StatusText = "Save the theme before applying.";
            return;
        }

        StatusText = $"Applying '{Theme.ToDisplayName(name)}'…";
        OmarchyCliService.CliResult result = await _cli.ApplyThemeAsync(name);
        if (result.Success)
        {
            Log.Information("Applied theme {Name} to the live desktop", name);
            StatusText = $"Applied '{Theme.ToDisplayName(name)}'.";
        }
        else
        {
            Log.Warning("Apply failed for {Name}: {Output}", name, result.Output);
            StatusText = "Apply failed: " + result.Output;
        }
    }

    [RelayCommand]
    private async Task AddBackgroundsAsync()
    {
        Log.Information("AddBackgrounds command invoked for theme {Name}", _current?.Name);
        if (_current?.Path is null)
        {
            Log.Warning("AddBackgrounds aborted: theme not saved yet (no path)");
            StatusText = "Save the theme before adding backgrounds.";
            return;
        }
        if (PickImagesAsync is null)
        {
            Log.Warning("AddBackgrounds aborted: no image picker hook wired");
            return;
        }

        IReadOnlyList<string> files = await PickImagesAsync();
        Log.Information("Image picker returned {Count} file(s) to add", files.Count);
        int added = 0, duplicates = 0;
        foreach (string file in files)
        {
            Log.Debug("Adding picked background {File} ({Index}/{Total})", file, added + duplicates + 1, files.Count);
            // Route through the host add so each new background gets the naming prompt + rename.
            BackgroundAddResult? result = await AddBackground(file);
            if (result is null) continue; // failure already logged and surfaced
            if (result.Value.WasNew) added++; else duplicates++;
        }
        Log.Information("AddBackgrounds finished for theme {Name}: {Added} new, {Duplicates} already present, of {Total}",
            _current.Name, added, duplicates, files.Count);
        StatusText = BackgroundSummary(added, duplicates);
    }

    /// <summary>Show a full-size modal preview of a background. No-op if the View hasn't wired the hook.</summary>
    public Task PreviewImage(string path) => PreviewImageHook?.Invoke(path) ?? Task.CompletedTask;

    [RelayCommand]
    private void GeneratePreview()
    {
        if (_current?.Path is null)
        {
            StatusText = "Save the theme before generating a preview.";
            return;
        }
        Control? control = PreviewControlProvider?.Invoke();
        if (control is null)
        {
            StatusText = "Preview not available to render.";
            return;
        }
        try
        {
            string outPath = Path.Combine(_current.Path, "preview.png");
            PreviewRenderService.RenderPng(control, outPath);
            StatusText = $"Wrote {outPath}";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Preview generation failed for {Path}", _current?.Path);
            StatusText = "Preview generation failed: " + ex.Message;
        }
    }

    /// <summary>Let the user pick a screenshot (or any image) to use as the theme's picker image —
    /// Omarchy shows <c>preview.png</c> in its theme menu. An alternative to the rendered mock from
    /// <see cref="GeneratePreview"/>; both write the same <c>preview.png</c>.</summary>
    [RelayCommand]
    private async Task SetPreviewImageAsync()
    {
        if (_current?.Path is null)
        {
            StatusText = "Save the theme before setting a preview image.";
            return;
        }

        string? picked = await PickImageAsync();
        if (string.IsNullOrEmpty(picked)) return;

        try
        {
            string dest = _repo.SetPreviewImage(_current, picked);
            Log.Information("Set theme picker image for {Name} to {Dest}", _current.Name, dest);
            StatusText = $"Set theme picker image ({Path.GetFileName(dest)}).";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Set preview image failed for {Path}", _current?.Path);
            StatusText = "Set preview image failed: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (_current?.Path is null)
        {
            StatusText = "Save the theme before exporting.";
            return;
        }
        if (PickFolderAsync is null) return;

        string? folder = await PickFolderAsync();
        if (string.IsNullOrEmpty(folder)) return;

        try
        {
            ExportService.ExportResult result = _export.Export(_current, folder);
            StatusText = result.GitInitialized
                ? $"Exported git repo to {result.RepoPath}"
                : $"Exported to {result.RepoPath} (git not initialized)";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Export failed for {Name} to {Folder}", _current?.Name, folder);
            StatusText = "Export failed: " + ex.Message;
        }
    }

    // ===================== IPaletteHost ==============================

    public ThemeColors CurrentPalette => _working;

    /// <summary>
    /// The single choke point every tab uses to replace the palette. Snapshots history, swaps in
    /// the new colors, rebuilds the editable fields, and refreshes the preview + contrast panel.
    /// </summary>
    public void ApplyPalette(ThemeColors next, string status, string? coalesceKey = null)
    {
        // Fold consecutive same-key applies (e.g. a slider drag) into one undo step.
        bool coalesce = coalesceKey is not null && coalesceKey == _lastCoalesceKey;
        if (!coalesce)
        {
            PushUndo(_working.Clone());
            _redo.Clear();
        }
        _lastCoalesceKey = coalesceKey;
        _editBurstActive = false;

        _working = next.Clone();
        BuildColorFields();
        Preview.Update(_working);
        Contrast.Update(_working);
        StatusText = status;
    }

    private string? _lastCoalesceKey;

    public void SetLightMode(bool light) => LightMode = light;

    public void SetStatus(string status) => StatusText = status;

    public Task<string?> PickImageAsync() =>
        PickSingleImageHook?.Invoke() ?? Task.FromResult<string?>(null);

    public Task<string?> PickFileAsync() =>
        PickFileForImportHook?.Invoke() ?? Task.FromResult<string?>(null);

    public async Task<BackgroundAddResult?> AddBackground(string sourcePath)
    {
        Log.Information("AddBackground (host) requested for {Source} on theme {Name}",
            sourcePath, _current?.Name);
        if (_current?.Path is null)
        {
            Log.Warning("AddBackground (host) refused: theme not saved yet (no path)");
            SetStatus("Save the theme first, then add backgrounds.");
            return null;
        }
        try
        {
            BackgroundAddResult result = _repo.AddBackground(_current, sourcePath);
            string finalPath = await PromptAndRenameAsync(result);

            CurrentWallpapers.AddExisting(finalPath);
            Log.Debug("AddBackground (host) succeeded: {Dest} (new={New})", finalPath, result.WasNew);
            return result with { Path = finalPath };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Add background failed for {Source}", sourcePath);
            SetStatus("Add background failed: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// For a genuinely-new background, ask the user to name it and rename the file to the Omarchy
    /// switcher convention (<c>NN-slug.ext</c>). Duplicates (already a background) and the case
    /// where no prompt hook is wired or the user skips are left with their copied-in name.
    /// Returns the path the background ended up at.
    /// </summary>
    private async Task<string> PromptAndRenameAsync(BackgroundAddResult result)
    {
        if (!result.WasNew || PromptForNameAsync is null || _current is null)
            return result.Path;

        string suggestion = Theme.ToDisplayName(
            Path.GetFileNameWithoutExtension(result.Path));
        string? chosen = await PromptForNameAsync(
            "Name this background",
            "Give this wallpaper a name. It's saved into the theme's backgrounds/ folder with an " +
            "ordering prefix so the Omarchy background switcher cycles them in order.",
            suggestion,
            result.Path);

        if (string.IsNullOrWhiteSpace(chosen))
        {
            Log.Debug("Background naming skipped for {Path}; keeping copied-in name", result.Path);
            return result.Path;
        }

        return _repo.RenameBackground(_current, result.Path, chosen);
    }

    // ===================== Undo / redo ===============================

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    [RelayCommand]
    private void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Push(_working.Clone());
        _working = _undo.Last!.Value;
        _undo.RemoveLast();
        AfterHistoryMove("Undid last change.");
    }

    [RelayCommand]
    private void Redo()
    {
        if (_redo.Count == 0) return;
        PushUndo(_working.Clone(), notify: false);
        _working = _redo.Pop();
        AfterHistoryMove("Redid change.");
    }

    private void AfterHistoryMove(string status)
    {
        _editBurstActive = false;
        _lastCoalesceKey = null;
        BuildColorFields();
        Preview.Update(_working);
        Contrast.Update(_working);
        StatusText = status;
        NotifyHistory();
    }

    private void PushUndo(ThemeColors snapshot, bool notify = true)
    {
        _undo.AddLast(snapshot);
        while (_undo.Count > HistoryLimit) _undo.RemoveFirst();
        if (notify) NotifyHistory();
    }

    private void ResetHistory()
    {
        _undo.Clear();
        _redo.Clear();
        _editBurstActive = false;
        _lastCoalesceKey = null;
        NotifyHistory();
    }

    private void NotifyHistory()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }
}
