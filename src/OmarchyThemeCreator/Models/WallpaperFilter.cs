namespace OmarchyThemeCreator.Models;

/// <summary>
/// Browse parameters for a wallhaven.cc search. Booleans map onto wallhaven's 3-bit
/// <c>purity</c> and <c>categories</c> masks; the rest map to their like-named query params.
/// </summary>
public sealed class WallpaperFilter
{
    public string Query { get; set; } = "";

    // Purity (wallhaven order is: sfw sketchy nsfw). NSFW requires a valid API key.
    public bool Sfw { get; set; } = true;
    public bool Sketchy { get; set; }
    public bool Nsfw { get; set; }

    // Categories (wallhaven order is: general anime people).
    public bool General { get; set; } = true;
    public bool Anime { get; set; } = true;
    public bool People { get; set; } = true;

    // relevance | random | date_added | views | favorites | toplist
    public string Sorting { get; set; } = "relevance";
    public string Order { get; set; } = "desc"; // asc | desc

    public string? AtLeastResolution { get; set; } // e.g. "1920x1080"
    public string? Ratio { get; set; }             // e.g. "16x9"
    public string? Color { get; set; }             // 6-hex, no '#'

    /// <summary>
    /// Build the "sfw sketchy nsfw" purity mask. When no API key is available the NSFW bit is
    /// dropped (wallhaven ignores it anonymously anyway). Falls back to SFW if nothing is set.
    /// </summary>
    public string PurityMask(bool hasApiKey)
    {
        bool sfw = Sfw;
        bool sketchy = Sketchy;
        bool nsfw = Nsfw && hasApiKey;
        if (!sfw && !sketchy && !nsfw) sfw = true;
        return (sfw ? "1" : "0") + (sketchy ? "1" : "0") + (nsfw ? "1" : "0");
    }

    /// <summary>Build the "general anime people" categories mask, defaulting to all if empty.</summary>
    public string CategoriesMask()
    {
        bool g = General;
        bool a = Anime;
        bool p = People;
        if (!g && !a && !p) { g = a = p = true; }
        return (g ? "1" : "0") + (a ? "1" : "0") + (p ? "1" : "0");
    }
}
