using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using OmarchyThemeCreator.Models;

namespace OmarchyThemeCreator.ViewModels;

/// <summary>One foreground/background pairing and its WCAG contrast result.</summary>
public sealed partial class ContrastRow : ObservableObject
{
    public string Label { get; }

    [ObservableProperty] private string _ratio = "";
    [ObservableProperty] private string _grade = "";
    [ObservableProperty] private IBrush _gradeBrush = Brushes.Gray;
    [ObservableProperty] private IBrush _fg = Brushes.White;
    [ObservableProperty] private IBrush _bg = Brushes.Black;

    public ContrastRow(string label) => Label = label;

    public void Set(string fgHex, string bgHex)
    {
        double ratio = ColorMath.ContrastRatio(fgHex, bgHex);
        Ratio = ratio.ToString("0.0") + ":1";
        Grade = ColorMath.Grade(ratio);
        GradeBrush = Grade switch
        {
            "AAA" => new SolidColorBrush(Color.Parse("#4caf50")),
            "AA" => new SolidColorBrush(Color.Parse("#8bc34a")),
            "AA Large" => new SolidColorBrush(Color.Parse("#ff9800")),
            _ => new SolidColorBrush(Color.Parse("#f44336")),
        };
        Fg = new SolidColorBrush(Color.Parse(ColorMath.FromHsl(ColorMath.ToHsl(fgHex))));
        Bg = new SolidColorBrush(Color.Parse(ColorMath.FromHsl(ColorMath.ToHsl(bgHex))));
    }
}

/// <summary>
/// Live WCAG contrast readout for the key color pairings, recomputed whenever the working
/// palette changes.
/// </summary>
public sealed class ContrastViewModel
{
    public ObservableCollection<ContrastRow> Rows { get; } = new()
    {
        new ContrastRow("Foreground / Background"),
        new ContrastRow("Selection FG / Selection BG"),
        new ContrastRow("Accent / Background"),
        new ContrastRow("Cursor / Background"),
    };

    public void Update(ThemeColors c)
    {
        Rows[0].Set(c.Foreground, c.Background);
        Rows[1].Set(c.SelectionForeground, c.SelectionBackground);
        Rows[2].Set(c.Accent, c.Background);
        Rows[3].Set(c.Cursor, c.Background);
    }
}
