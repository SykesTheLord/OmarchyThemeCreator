using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OmarchyThemeCreator.Models;
using Serilog;

namespace OmarchyThemeCreator.Services;

/// <summary>One search result from wallhaven.cc.</summary>
public sealed record WallhavenResult(string Id, string ThumbUrl, string FullUrl, string Resolution);

/// <summary>A page of results plus the total page count reported by wallhaven.</summary>
public sealed record WallhavenPage(IReadOnlyList<WallhavenResult> Items, int LastPage);

/// <summary>
/// Thin client over the public wallhaven.cc search API. SFW works anonymously; Sketchy/NSFW
/// need a personal API key (NSFW especially). All calls degrade gracefully: network/parse
/// failures throw a clean message the caller surfaces in the status bar rather than crashing.
/// </summary>
public sealed class WallhavenService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<WallhavenService>();

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    /// <summary>
    /// wallhaven's fixed set of filterable colors (hex, no '#'). The API only accepts one of
    /// these values for the <c>colors</c> parameter.
    /// </summary>
    public static readonly IReadOnlyList<string> Colors = new[]
    {
        "660000", "990000", "cc0000", "cc3333", "ea4c88", "993399", "663399", "333399",
        "0066cc", "0099cc", "66cccc", "77cc33", "669900", "336600", "666600", "999900",
        "cccc33", "ffff00", "ffcc33", "ff9900", "ff6600", "cc6633", "996633", "663300",
        "000000", "999999", "cccccc", "ffffff", "424153",
    };

    /// <summary>Snap an arbitrary <c>#rrggbb</c> (or bare hex) color to the nearest wallhaven color.</summary>
    public static string NearestColor(string hex)
    {
        string s = hex.TrimStart('#');
        if (s.Length != 6) return Colors[0];
        int r = Convert.ToInt32(s.Substring(0, 2), 16);
        int g = Convert.ToInt32(s.Substring(2, 2), 16);
        int b = Convert.ToInt32(s.Substring(4, 2), 16);

        string best = Colors[0];
        int bestDist = int.MaxValue;
        foreach (string c in Colors)
        {
            int cr = Convert.ToInt32(c.Substring(0, 2), 16);
            int cg = Convert.ToInt32(c.Substring(2, 2), 16);
            int cb = Convert.ToInt32(c.Substring(4, 2), 16);
            int dr = r - cr, dg = g - cg, db = b - cb;
            int dist = dr * dr + dg * dg + db * db;
            if (dist < bestDist) { bestDist = dist; best = c; }
        }
        return best;
    }

    /// <summary>Search wallpapers with the given filter. <paramref name="page"/> is 1-based.</summary>
    public async Task<WallhavenPage> SearchAsync(WallpaperFilter filter, string? apiKey = null, int page = 1)
    {
        bool hasKey = !string.IsNullOrWhiteSpace(apiKey);

        StringBuilder sb = new StringBuilder("https://wallhaven.cc/api/v1/search");
        sb.Append("?q=").Append(Uri.EscapeDataString(filter.Query ?? string.Empty));
        sb.Append("&categories=").Append(filter.CategoriesMask());
        sb.Append("&purity=").Append(filter.PurityMask(hasKey));
        sb.Append("&sorting=").Append(Uri.EscapeDataString(filter.Sorting));
        sb.Append("&order=").Append(Uri.EscapeDataString(filter.Order));
        if (!string.IsNullOrWhiteSpace(filter.AtLeastResolution))
            sb.Append("&atleast=").Append(Uri.EscapeDataString(filter.AtLeastResolution));
        if (!string.IsNullOrWhiteSpace(filter.Ratio))
            sb.Append("&ratios=").Append(Uri.EscapeDataString(filter.Ratio));
        if (!string.IsNullOrWhiteSpace(filter.Color))
            sb.Append("&colors=").Append(Uri.EscapeDataString(filter.Color.TrimStart('#')));
        if (hasKey)
            sb.Append("&apikey=").Append(Uri.EscapeDataString(apiKey!));
        sb.Append("&page=").Append(Math.Max(1, page));

        // Note: the query string carries the API key, so log the meaningful parameters only.
        Log.Information("wallhaven search q={Query} page={Page} withKey={HasKey}",
            filter.Query, page, hasKey);

        string json;
        try
        {
            json = await Http.GetStringAsync(sb.ToString());
        }
        catch (Exception ex)
        {
            // The caller (WallpaperViewModel) logs the user-facing failure; this wraps the raw
            // HTTP error into the clean message shown in the status bar.
            throw new InvalidOperationException($"wallhaven search failed: {ex.Message}");
        }

        List<WallhavenResult> results = new List<WallhavenResult>();
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        if (root.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in data.EnumerateArray())
            {
                string id = item.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() ?? "" : "";
                string full = item.TryGetProperty("path", out JsonElement pathEl) ? pathEl.GetString() ?? "" : "";
                string thumb = full;
                if (item.TryGetProperty("thumbs", out JsonElement thumbs) &&
                    thumbs.TryGetProperty("small", out JsonElement small))
                    thumb = small.GetString() ?? full;
                string res = item.TryGetProperty("resolution", out JsonElement resEl) ? resEl.GetString() ?? "" : "";

                if (!string.IsNullOrEmpty(full))
                    results.Add(new WallhavenResult(id, thumb, full, res));
            }
        }

        int lastPage = 1;
        if (root.TryGetProperty("meta", out JsonElement meta) &&
            meta.TryGetProperty("last_page", out JsonElement lastEl))
        {
            if (lastEl.ValueKind == JsonValueKind.Number && lastEl.TryGetInt32(out int lp))
                lastPage = lp;
            else if (lastEl.ValueKind == JsonValueKind.String &&
                     int.TryParse(lastEl.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int lps))
                lastPage = lps;
        }

        return new WallhavenPage(results, Math.Max(1, lastPage));
    }

    /// <summary>Fetch raw bytes of a URL (used to load result thumbnails), or null on failure.</summary>
    public async Task<byte[]?> TryGetBytesAsync(string url)
    {
        try
        {
            return await Http.GetByteArrayAsync(url);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Download an image URL into <paramref name="destDir"/>; returns the local path.</summary>
    public async Task<string> DownloadAsync(string url, string destDir)
    {
        Directory.CreateDirectory(destDir);
        string name = Path.GetFileName(new Uri(url).AbsolutePath);
        if (string.IsNullOrEmpty(name)) name = $"wallhaven-{Guid.NewGuid():N}.jpg";
        string dest = Path.Combine(destDir, name);

        try
        {
            byte[] bytes = await Http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(dest, bytes);
            Log.Information("Downloaded {Url} ({Bytes} bytes) to {Dest}", url, bytes.Length, dest);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Download failed for {Url}", url);
            throw new InvalidOperationException($"download failed: {ex.Message}");
        }
        return dest;
    }
}
