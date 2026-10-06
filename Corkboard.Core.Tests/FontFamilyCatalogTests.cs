using Corkboard.Core;
using Corkboard.Core.Services.Fonts;

namespace Corkboard.Core.Tests;

public class FontFamilyCatalogTests
{
    [Fact]
    public void NormalizeFamilyNames_FiltersCompositeBlankAndVerticalVariants()
    {
        var result = FontFamilyCatalog.NormalizeFamilyNames(
            ["compositefont:Arial", "@微软雅黑", "Arial", "ariaL", "   ", null, "Bahnschrift"]);

        Assert.Equal(["Arial", "Bahnschrift"], result);
    }

    [Fact]
    public void NormalizeFamilyNames_SortsByCurrentCulture()
    {
        var result = FontFamilyCatalog.NormalizeFamilyNames(["Zebra", "Alpha", "Middle"]);

        Assert.Equal(["Alpha", "Middle", "Zebra"], result);
    }

    [Fact]
    public void Resolve_FallsBackToBundledDefault()
    {
        Assert.Equal(GlobalConstants.DefaultAvaFontFamily,
            FontFamilyCatalog.Resolve(FontFamilyCatalog.DefaultFontSentinel));
        Assert.Equal(GlobalConstants.DefaultAvaFontFamily, FontFamilyCatalog.Resolve(null));
        Assert.Equal(GlobalConstants.DefaultAvaFontFamily, FontFamilyCatalog.Resolve("   "));
    }

    [Fact]
    public void Resolve_UsesExplicitFamilyName()
    {
        var resolved = FontFamilyCatalog.Resolve("Arial");

        Assert.Equal("Arial", resolved.Name);
        Assert.NotEqual(GlobalConstants.DefaultAvaFontFamily, resolved);
    }
}
