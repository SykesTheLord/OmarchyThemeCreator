using System.IO;
using System.Text;
using OmarchyThemeCreator.Models;
using Serilog;

namespace OmarchyThemeCreator.Services;

/// <summary>
/// Reads and writes Omarchy <c>colors.toml</c> files. The format is a flat list of
/// <c>key = "#value"</c> lines; we parse tolerantly (mirroring the sed-style extraction
/// in <c>omarchy-theme-set-templates</c>) and re-serialize in Omarchy's canonical layout
/// so diffs against hand-authored themes stay clean.
/// </summary>
public static class ColorsTomlService
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(ColorsTomlService));

    public static ThemeColors Parse(string content)
    {
        ThemeColors colors = new ThemeColors();
        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            int eq = line.IndexOf('=');
            if (eq < 0) continue;

            string key = line.Substring(0, eq).Trim().Trim('"', '\'', ' ');
            string value = ExtractValue(line.Substring(eq + 1));
            if (key.Length == 0 || value.Length == 0) continue;

            colors.Set(key, value);
        }
        return colors;
    }

    public static ThemeColors Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Extract the value between the first pair of quotes, ignoring inline comments.</summary>
    private static string ExtractValue(string rest)
    {
        int first = rest.IndexOfAny(new[] { '"', '\'' });
        if (first < 0)
        {
            // Unquoted value (e.g. "background = #1a1b26"): take up to a comment.
            int hashComment = rest.IndexOf('#', 1);
            string v = (hashComment > 0 ? rest.Substring(0, hashComment) : rest).Trim();
            return v;
        }
        char quote = rest[first];
        int second = rest.IndexOf(quote, first + 1);
        if (second < 0) return rest.Substring(first + 1).Trim();
        return rest.Substring(first + 1, second - first - 1);
    }

    public static string Serialize(ThemeColors c)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("accent = \"").Append(c.Accent).Append("\"\n");
        sb.Append("cursor = \"").Append(c.Cursor).Append("\"\n");
        sb.Append("foreground = \"").Append(c.Foreground).Append("\"\n");
        sb.Append("background = \"").Append(c.Background).Append("\"\n");
        sb.Append("selection_foreground = \"").Append(c.SelectionForeground).Append("\"\n");
        sb.Append("selection_background = \"").Append(c.SelectionBackground).Append("\"\n");
        sb.Append('\n');
        for (int i = 0; i < c.Ansi.Length; i++)
            sb.Append("color").Append(i).Append(" = \"").Append(c.Ansi[i]).Append("\"\n");
        return sb.ToString();
    }

    public static void Save(string path, ThemeColors colors)
    {
        string toml = Serialize(colors);
        Log.Debug("Writing colors.toml to {Path}: accent={Accent}, background={Background}, foreground={Foreground}",
            path, colors.Accent, colors.Background, colors.Foreground);
        File.WriteAllText(path, toml);
        Log.Debug("Wrote {Bytes} bytes of colors.toml to {Path}", toml.Length, path);
    }
}
