using System;
using System.IO;
using System.Threading;
using OmarchyThemeCreator.Models;
using Serilog;

namespace OmarchyThemeCreator.Services;

/// <summary>
/// Tracks the *currently applied* Omarchy desktop theme (distinct from the theme being edited)
/// and raises <see cref="Changed"/> whenever the user switches themes, so the app can recolor its
/// own chrome to match the live desktop.
///
/// Reads <c>~/.config/omarchy/current/theme/colors.toml</c> and watches the enclosing
/// <c>current/</c> directory: <c>omarchy-theme-set</c> atomically swaps that <c>theme</c> folder
/// (rm + mv) and rewrites <c>theme.name</c>, so a single watcher on the parent catches every
/// switch. Degrades to <see cref="IsAvailable"/> == false when Omarchy isn't installed, matching
/// the rest of the app's graceful-degradation contract.
/// </summary>
public sealed class LiveOmarchyThemeService : IDisposable
{
    private static readonly ILogger Log = Serilog.Log.ForContext<LiveOmarchyThemeService>();

    private readonly string _currentDir; // ~/.config/omarchy/current
    private readonly string _colorsPath; // .../current/theme/colors.toml
    private readonly string _namePath;   // .../current/theme.name
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    /// <summary>True when an Omarchy install with a current theme is present to read.</summary>
    public bool IsAvailable { get; }

    /// <summary>Raised (on a thread-pool thread) after the live theme changes, debounced.</summary>
    public event EventHandler? Changed;

    public LiveOmarchyThemeService()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _currentDir = Path.Combine(home, ".config", "omarchy", "current");
        _colorsPath = Path.Combine(_currentDir, "theme", "colors.toml");
        _namePath = Path.Combine(_currentDir, "theme.name");
        IsAvailable = File.Exists(_colorsPath);
    }

    /// <summary>Load the live palette, or null if unavailable / mid-swap / unreadable.</summary>
    public ThemeColors? TryLoadColors()
    {
        try { return File.Exists(_colorsPath) ? ColorsTomlService.Load(_colorsPath) : null; }
        catch { return null; }
    }

    /// <summary>The current theme's name (from theme.name), or null.</summary>
    public string? TryLoadName()
    {
        try { return File.Exists(_namePath) ? File.ReadAllText(_namePath).Trim() : null; }
        catch { return null; }
    }

    /// <summary>Begin watching for live theme switches. No-op when Omarchy isn't present.</summary>
    public void Start()
    {
        if (!IsAvailable || _watcher is not null) return;
        try
        {
            _watcher = new FileSystemWatcher(_currentDir)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnFsEvent;
            _watcher.Created += OnFsEvent;
            _watcher.Renamed += OnFsEvent;
            Log.Debug("Watching live theme directory {Dir}", _currentDir);
        }
        catch (Exception ex)
        {
            // Watching is best-effort; the initial colors were still read at construction.
            Log.Warning(ex, "Failed to watch live theme directory {Dir}", _currentDir);
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        // A single theme switch emits a burst (dir swap + theme.name write); coalesce them and
        // let the atomic mv settle before we re-read.
        _debounce?.Dispose();
        _debounce = new Timer(_ =>
        {
            Log.Debug("Live Omarchy theme changed; re-reading colors");
            Changed?.Invoke(this, EventArgs.Empty);
        }, null, 200, Timeout.Infinite);
    }

    public void Dispose()
    {
        _debounce?.Dispose();
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }
}
