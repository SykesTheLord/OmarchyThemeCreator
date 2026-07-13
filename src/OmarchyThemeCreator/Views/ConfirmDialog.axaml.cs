using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OmarchyThemeCreator.Views;

/// <summary>A small modal yes/cancel confirmation. Returns true via ShowDialog&lt;bool&gt;.</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(string title, string message, string confirmLabel = "Delete") : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmLabel;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
