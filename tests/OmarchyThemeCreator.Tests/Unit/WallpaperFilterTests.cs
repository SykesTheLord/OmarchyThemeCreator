using OmarchyThemeCreator.Models;
using Xunit;

namespace OmarchyThemeCreator.Tests.Unit;

public sealed class WallpaperFilterTests
{
    [Fact]
    public void PurityMask_default_is_sfw_only()
    {
        WallpaperFilter filter = new WallpaperFilter();
        Assert.Equal("100", filter.PurityMask(hasApiKey: false));
    }

    [Fact]
    public void PurityMask_drops_nsfw_bit_without_api_key()
    {
        WallpaperFilter filter = new WallpaperFilter { Sfw = true, Sketchy = true, Nsfw = true };
        Assert.Equal("110", filter.PurityMask(hasApiKey: false));
    }

    [Fact]
    public void PurityMask_keeps_nsfw_bit_with_api_key()
    {
        WallpaperFilter filter = new WallpaperFilter { Sfw = true, Sketchy = true, Nsfw = true };
        Assert.Equal("111", filter.PurityMask(hasApiKey: true));
    }

    [Fact]
    public void PurityMask_falls_back_to_sfw_when_nothing_is_set()
    {
        WallpaperFilter filter = new WallpaperFilter { Sfw = false, Sketchy = false, Nsfw = false };
        Assert.Equal("100", filter.PurityMask(hasApiKey: true));
    }

    [Fact]
    public void CategoriesMask_default_is_all_on()
    {
        Assert.Equal("111", new WallpaperFilter().CategoriesMask());
    }

    [Fact]
    public void CategoriesMask_reflects_individual_flags()
    {
        WallpaperFilter filter = new WallpaperFilter { General = true, Anime = false, People = false };
        Assert.Equal("100", filter.CategoriesMask());
    }

    [Fact]
    public void CategoriesMask_falls_back_to_all_when_empty()
    {
        WallpaperFilter filter = new WallpaperFilter { General = false, Anime = false, People = false };
        Assert.Equal("111", filter.CategoriesMask());
    }
}
