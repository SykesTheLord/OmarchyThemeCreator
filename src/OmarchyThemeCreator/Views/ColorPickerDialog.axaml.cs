using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using OmarchyThemeCreator.Services;
using OmarchyThemeCreator.ViewModels;

namespace OmarchyThemeCreator.Views;

/// <summary>
/// A custom "Color Disc" modal picker (hue ring + SV field, numeric readouts, eyedropper). Returns the
/// chosen <see cref="Color"/> via ShowDialog&lt;Color?&gt;, or null when cancelled. Alpha is dropped —
/// Omarchy palettes are opaque #rrggbb.
/// </summary>
public partial class ColorPickerDialog : Window
{
    private ColorPickerViewModel? _vm;

    public ColorPickerDialog()
    {
        InitializeComponent();
    }

    public ColorPickerDialog(string label, Color initial) : this()
    {
        Title = $"Edit {label}";
        _vm = new ColorPickerViewModel(initial, new ScreenColorPicker());
        DataContext = _vm;
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e) => BeginMoveDrag(e);

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(_vm?.CurrentColor);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
