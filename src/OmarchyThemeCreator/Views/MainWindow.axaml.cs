using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using OmarchyThemeCreator.ViewModels;

namespace OmarchyThemeCreator.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // Give the VM ways to drive file pickers and to grab the preview control for rendering.
        vm.PickImagesAsync = PickImagesAsync;
        vm.PickSingleImageHook = () => PickSingleFileAsync("Choose a wallpaper",
            new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif", "*.bmp" }, "Images");
        vm.PickFileForImportHook = () => PickSingleFileAsync("Import a Base16 scheme",
            new[] { "*.yaml", "*.yml" }, "Base16 YAML");
        vm.PickFolderAsync = PickFolderAsync;
        vm.PreviewControlProvider = () => this.FindControl<Control>("PreviewRoot");
        vm.ConfirmAsync = (title, message) =>
            new ConfirmDialog(title, message).ShowDialog<bool>(this);
        vm.PromptForNameAsync = (title, message, defaultText, imagePath) =>
            new TextPromptDialog(title, message, defaultText, imagePath).ShowDialog<string?>(this);
        vm.PreviewImageHook = path => new ImagePreviewDialog(path).ShowDialog(this);
        vm.PickColorAsync = (label, color) =>
            new ColorPickerDialog(label, color).ShowDialog<Color?>(this);
        vm.ShowImageEditorAsync = editorVm =>
            new ImageEditorDialog(editorVm).ShowDialog<bool>(this);
    }

    private async Task<string?> PickSingleFileAsync(string title, string[] patterns, string typeName)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(typeName) { Patterns = patterns } },
        });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<IReadOnlyList<string>> PickImagesAsync()
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add background images",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif", "*.bmp" },
                },
            },
        });

        return files.Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();
    }

    private async Task<string?> PickFolderAsync()
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder to export the theme repo into",
            AllowMultiple = false,
        });
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    private void OnThemeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.OpenSelectedCommand.CanExecute(null))
            vm.OpenSelectedCommand.Execute(null);
    }

    private void OnPresetDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.Presets.ApplyCommand.CanExecute(null))
            vm.Presets.ApplyCommand.Execute(null);
    }
}
