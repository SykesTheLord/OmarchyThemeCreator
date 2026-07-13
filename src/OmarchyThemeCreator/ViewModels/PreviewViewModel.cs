using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using OmarchyThemeCreator.Models;

namespace OmarchyThemeCreator.ViewModels;

/// <summary>
/// Derives the brushes used by the live preview panel (mock waybar, terminal, desktop)
/// from the current palette. <see cref="Update"/> recomputes everything at once.
/// </summary>
public sealed partial class PreviewViewModel : ObservableObject
{
    [ObservableProperty] private IBrush _background = Brushes.Black;
    [ObservableProperty] private IBrush _foreground = Brushes.White;
    [ObservableProperty] private IBrush _accent = Brushes.RoyalBlue;
    [ObservableProperty] private IBrush _cursor = Brushes.White;
    [ObservableProperty] private IBrush _selectionBackground = Brushes.RoyalBlue;
    [ObservableProperty] private IBrush _selectionForeground = Brushes.White;

    // The 16 ANSI colors as brushes, for the terminal swatch grid.
    public List<IBrush> Ansi { get; } = NewAnsiList();

    private static List<IBrush> NewAnsiList()
    {
        List<IBrush> list = new List<IBrush>(16);
        for (int i = 0; i < 16; i++) list.Add(Brushes.Gray);
        return list;
    }

    public void Update(ThemeColors c)
    {
        Background = Brush(c.Background);
        Foreground = Brush(c.Foreground);
        Accent = Brush(c.Accent);
        Cursor = Brush(c.Cursor);
        SelectionBackground = Brush(c.SelectionBackground);
        SelectionForeground = Brush(c.SelectionForeground);

        for (int i = 0; i < 16; i++)
            Ansi[i] = Brush(c.Ansi[i]);
        OnPropertyChanged(nameof(Ansi));
    }

    private static IBrush Brush(string hex)
    {
        string? norm = ThemeColors.NormalizeHex(hex);
        return norm is null ? Brushes.Transparent : new SolidColorBrush(Color.Parse(norm));
    }
}
