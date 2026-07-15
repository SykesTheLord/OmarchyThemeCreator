using OmarchyThemeCreator.Services;
using Xunit;

namespace OmarchyThemeCreator.Tests.Unit;

/// <summary>
/// Preset selection is pure and public; the actual SkiaSharp rendering (matrix maths) is verified
/// behaviourally in the integration pipeline test.
/// </summary>
public sealed class ImageEditServiceTests
{
    private readonly ImageEditService _service = new();

    [Fact]
    public void PresetNames_is_non_empty()
    {
        Assert.NotEmpty(_service.PresetNames);
    }

    [Fact]
    public void Every_listed_preset_returns_options()
    {
        foreach (string name in _service.PresetNames)
            Assert.NotNull(_service.Preset(name));
    }

    [Fact]
    public void Unknown_preset_returns_a_neutral_passthrough()
    {
        // A fresh ImageEditOptions is a no-op; record value-equality confirms the fallback.
        Assert.Equal(new ImageEditOptions(), _service.Preset("does-not-exist"));
    }

    [Fact]
    public void Original_preset_is_a_neutral_passthrough()
    {
        Assert.Equal(new ImageEditOptions(), _service.Preset("Original"));
    }

    [Fact]
    public void Noir_preset_desaturates()
    {
        Assert.Equal(0f, _service.Preset("Noir").Saturation);
    }

    [Fact]
    public void Vivid_preset_boosts_saturation()
    {
        Assert.True(_service.Preset("Vivid").Saturation > 1f);
    }
}
