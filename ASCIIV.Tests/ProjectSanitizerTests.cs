using ASCIIV.Core;

using Xunit;

namespace ASCIIV.Tests;

public class ProjectSanitizerTests {
    [Theory]
    [InlineData("MyVideo", "MyVideo")]
    [InlineData("My:Video?*Test", "My_Video_Test")]
    [InlineData("../../SensitiveFile", "SensitiveFile")]
    [InlineData("   ", "OutputVideo")]
    [InlineData(null, "OutputVideo")]
    [InlineData("con", "con")]
    public void SanitizeProjectName_StripsIllegalCharacters(string? input, string expected) {
        string result = ProjectSanitizer.SanitizeProjectName(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void SanitizeProjectName_RespectsCustomDefaultName() {
        string result = ProjectSanitizer.SanitizeProjectName(null, "FallbackName");
        Assert.Equal("FallbackName", result);
    }
}