using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using OmarchyThemeCreator.Services;
using OmarchyThemeCreator.ViewModels;
using OmarchyThemeCreator.Views;
using Serilog;

namespace OmarchyThemeCreator;

public partial class App : Application
{
    private static readonly ILogger Log = Serilog.Log.ForContext<App>();

    // Kept alive for the app's lifetime so its FileSystemWatcher keeps firing.
    private LiveOmarchyThemeService? _liveTheme;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ThemeRepository repository = new ThemeRepository();
            OmarchyCliService cli = new OmarchyCliService();
            IconThemeService icons = new IconThemeService();
            ExportService export = new ExportService();
            PaletteExtractionService extractor = new PaletteExtractionService();
            PresetService presets = new PresetService();
            WallhavenService wallhaven = new WallhavenService();
            WallpaperColorAnalyzer colorAnalyzer = new WallpaperColorAnalyzer();
            ImageEditService imageEditor = new ImageEditService();
            SettingsService settings = new SettingsService();

            // Match the app's own chrome to the live Omarchy desktop theme, and keep tracking it.
            _liveTheme = new LiveOmarchyThemeService();
            Log.Information("Live Omarchy theme {Availability}",
                _liveTheme.IsAvailable ? "detected" : "not present");
            ApplyLiveTheme();
            _liveTheme.Changed += (_, _) => Dispatcher.UIThread.Post(ApplyLiveTheme);
            _liveTheme.Start();

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(
                    repository, cli, icons, export,
                    extractor, presets, wallhaven, colorAnalyzer, imageEditor, settings)
            };

            desktop.Exit += (_, _) => _liveTheme?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ApplyLiveTheme()
    {
        if (_liveTheme?.TryLoadColors() is { } colors)
            AppThemeSync.Apply(this, colors);
    }
}
