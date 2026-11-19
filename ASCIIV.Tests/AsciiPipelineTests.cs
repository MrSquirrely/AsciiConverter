using ASCIIV.Core;

using Xunit;

namespace ASCIIV.Tests;

public class AsciiPipelineTests {
    private static readonly char[] Ramp = "@%#*+=-:. ".ToCharArray();

    [Fact]
    public void BuildLookup_MapsDarkestAndLightestPixelsToRampEnds() {
        char[] lookup = AsciiRamp.BuildLookup(Ramp);

        Assert.Equal(256, lookup.Length);
        Assert.Equal('@', lookup[0]);
        Assert.Equal(' ', lookup[255]);
    }

    [Fact]
    public void BuildLookup_OnlyProducesRampCharacters() {
        char[] lookup = AsciiRamp.BuildLookup(Ramp, contrast: 2.5);

        Assert.All(lookup, static c => Assert.Contains(c, Ramp));
    }

    [Fact]
    public void BuildLookup_InvertSwapsRampEnds() {
        char[] lookup = AsciiRamp.BuildLookup(Ramp, invert: true);

        Assert.Equal(' ', lookup[0]);
        Assert.Equal('@', lookup[255]);
    }

    [Fact]
    public void BuildLookup_HighContrastPushesPixelsTowardsExtremes() {
        char[] normal = AsciiRamp.BuildLookup(Ramp);
        char[] highContrast = AsciiRamp.BuildLookup(Ramp, contrast: 2.0);

        // A dark pixel gets darker and a bright pixel gets brighter
        Assert.True(Array.IndexOf(Ramp, highContrast[50]) < Array.IndexOf(Ramp, normal[50]));
        Assert.True(Array.IndexOf(Ramp, highContrast[200]) > Array.IndexOf(Ramp, normal[200]));
    }

    [Fact]
    public void BuildLookup_SingleCharacterRampMapsEverythingToIt() {
        char[] lookup = AsciiRamp.BuildLookup("#");

        Assert.All(lookup, static c => Assert.Equal('#', c));
    }

    [Fact]
    public void BuildLookup_EmptyRampThrows() {
        Assert.Throws<ArgumentException>(static () => AsciiRamp.BuildLookup(""));
    }
}