using AutoSaveGame.Core.Services;

namespace AutoSaveGame.Core.Tests.Services;

public sealed class PathTemplateServiceTests
{
    private static readonly IReadOnlyDictionary<string, string> EnvironmentPaths =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["USERPROFILE"] = @"C:\Users\Cafe",
            ["APPDATA"] = @"C:\Users\Cafe\AppData\Roaming",
            ["LOCALAPPDATA"] = @"C:\Users\Cafe\AppData\Local",
            ["PROGRAMDATA"] = @"C:\ProgramData",
        };

    [Fact]
    public void Collapse_ReplacesTheLongestKnownRoot()
    {
        var sut = new PathTemplateService(EnvironmentPaths);

        var result = sut.Collapse(@"C:\Users\Cafe\AppData\Roaming\Game\save");

        Assert.Equal(@"%APPDATA%\Game\save", result);
    }

    [Fact]
    public void Collapse_DoesNotReplaceAPartialDirectoryName()
    {
        var sut = new PathTemplateService(EnvironmentPaths);

        var result = sut.Collapse(@"C:\Users\Cafeteria\Game\save");

        Assert.Equal(@"C:\Users\Cafeteria\Game\save", result);
    }

    [Fact]
    public void Collapse_PreservesADriveRoot()
    {
        var sut = new PathTemplateService(EnvironmentPaths);

        var result = sut.Collapse(@"C:\");

        Assert.Equal(@"C:\", result);
    }

    [Theory]
    [InlineData(@"%USERPROFILE%\Documents\Game", @"C:\Users\Cafe\Documents\Game")]
    [InlineData(@"%LOCALAPPDATA%\Game", @"C:\Users\Cafe\AppData\Local\Game")]
    [InlineData(@"%PROGRAMDATA%\Game", @"C:\ProgramData\Game")]
    public void Expand_ResolvesSupportedVariables(string template, string expected)
    {
        var sut = new PathTemplateService(EnvironmentPaths);

        var result = sut.Expand(template);

        Assert.Equal(expected, result, ignoreCase: true);
    }

    [Fact]
    public void Expand_RejectsUnknownVariables()
    {
        var sut = new PathTemplateService(EnvironmentPaths);

        var error = Assert.Throws<InvalidOperationException>(
            () => sut.Expand(@"%SYSTEMROOT%\Game"));

        Assert.Contains("SYSTEMROOT", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Expand_RejectsTraversalOutsideTheConfiguredRoot()
    {
        var sut = new PathTemplateService(EnvironmentPaths);

        Assert.Throws<InvalidOperationException>(
            () => sut.Expand(@"%APPDATA%\..\..\..\Windows"));
    }
}
