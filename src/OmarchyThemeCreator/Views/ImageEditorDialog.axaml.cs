using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OmarchyThemeCreator.ViewModels;

namespace OmarchyThemeCreator.Views;

/// <summary>
/// A modal wallpaper editor: live preview plus adjustment sliders and presets, driven by an
/// <see cref="ImageEditorViewModel"/> set as the DataContext. Returns <c>true</c> via
/// <c>ShowDialog&lt;bool&gt;</c> when the user saves; the caller then applies the VM's
/// <see cref="ImageEditorViewModel.Options"/> at full resolution. Disposes the VM's cached preview
/// bitmap on close.
/// </summary>
public partial class ImageEditorDialog : Window
{
    public ImageEditorDialog()
    {
        InitializeComponent();
    }

    public ImageEditorDialog(ImageEditorViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Title = $"Edit — {System.IO.Path.GetFileName(viewModel.ImagePath)}";
        KeyDown += OnKeyDown;
        Closed += (_, _) => viewModel.Dispose();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close(false);
    }

    private void OnSave(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
