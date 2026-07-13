using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OmarchyThemeCreator.Services;

namespace OmarchyThemeCreator.Views;

/// <summary>
/// A small modal text-input prompt. Returns the trimmed text via <c>ShowDialog&lt;string?&gt;</c>,
/// or null if the user skips/cancels (or leaves it blank). Like <see cref="ConfirmDialog"/>, it
/// relies on the Avalonia source-generated <c>InitializeComponent</c> to wire the named controls —
/// do not add a hand-written one, or the fields stay null.
/// </summary>
public partial class TextPromptDialog : Window
{
    public TextPromptDialog()
    {
        InitializeComponent();
    }

    public TextPromptDialog(string title, string message, string defaultText = "",
        string? imagePath = null) : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        InputBox.Text = defaultText;

        // Show a thumbnail of the wallpaper being named so the user can see what they're naming.
        // TryLoadThumbnail decodes at a reduced size — cheap even for large wallpapers, and the
        // preview area is only ~400px wide. When no path is given (e.g. cloning a theme) the border
        // stays collapsed and the dialog is a plain text prompt.
        if (!string.IsNullOrEmpty(imagePath))
        {
            var thumb = BitmapInterop.TryLoadThumbnail(imagePath, 400);
            if (thumb is not null)
            {
                PreviewImage.Source = thumb;
                PreviewBorder.IsVisible = true;
            }
        }

        // Preselect the suggested text and focus the box so the user can type over it immediately.
        Loaded += (_, _) =>
        {
            InputBox.SelectAll();
            InputBox.Focus();
        };
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Confirm();
        else if (e.Key == Key.Escape) Close(null);
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Confirm();
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void Confirm()
    {
        string? text = InputBox.Text?.Trim();
        Close(string.IsNullOrWhiteSpace(text) ? null : text);
    }
}
