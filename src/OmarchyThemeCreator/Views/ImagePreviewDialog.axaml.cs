using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using OmarchyThemeCreator.Services;

namespace OmarchyThemeCreator.Views;

/// <summary>A modal that shows a wallpaper at full size. Click the image or press Esc to close.</summary>
public partial class ImagePreviewDialog : Window
{
    public ImagePreviewDialog()
    {
        InitializeComponent();
    }

    public ImagePreviewDialog(string path) : this()
    {
        string name = Path.GetFileName(path);
        Title = name;
        Caption.Text = name + "  ·  click or press Esc to close";
        PreviewImage.Source = BitmapInterop.TryLoad(path);
    }

    private void OnClose(object? sender, PointerPressedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        base.OnKeyDown(e);
    }
}
