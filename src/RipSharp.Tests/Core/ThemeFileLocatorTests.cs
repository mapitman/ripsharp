using System.Collections.Generic;
using System.IO;

using AwesomeAssertions;

using Xunit;

namespace BugZapperLabs.RipSharp.Tests.Core;

public class ThemeFileLocatorTests
{
    [Fact]
    public void ResolveThemePath_WhenRelative_ReturnsThemePathUnderConfigThemes()
    {
        var configPath = Path.Combine("/home/tester", ".config", "ripsharp", "config.yaml");

        var result = ThemeFileLocator.ResolveThemePath("custom.yaml", configPath);

        result.Should().Be(Path.Combine("/home/tester", ".config", "ripsharp", "themes", "custom.yaml"));
    }

    [Theory]
    [InlineData("catppuccin mocha", "catppuccin-mocha.yaml")]
    [InlineData("catppuccin-mocha", "catppuccin-mocha.yaml")]
    [InlineData("catppuccin_mocha", "catppuccin-mocha.yaml")]
    public void ResolveThemePath_WhenNameProvided_NormalizesToFileName(string themeName, string expectedFileName)
    {
        var configPath = Path.Combine("/home/tester", ".config", "ripsharp", "config.yaml");

        var result = ThemeFileLocator.ResolveThemePath(themeName, configPath);

        result.Should().Be(Path.Combine("/home/tester", ".config", "ripsharp", "themes", expectedFileName));
    }

    [Fact]
    public void ResolveThemePath_WhenMissing_UsesDefaultThemeFile()
    {
        var configPath = Path.Combine("/home/tester", ".config", "ripsharp", "config.yaml");

        var result = ThemeFileLocator.ResolveThemePath(null, configPath);

        result.Should().Be(Path.Combine("/home/tester", ".config", "ripsharp", "themes", "catppuccin-mocha.yaml"));
    }

    [Fact]
    public void EnsureBundledThemeFiles_WritesThemesIntoConfigThemesDirectory()
    {
        var configPath = Path.Combine("/home/tester", ".config", "ripsharp", "config.yaml");
        var files = new Dictionary<string, string>();
        var directories = new HashSet<string>();
        var bundledThemes = new Dictionary<string, string>
        {
            ["catppuccin-mocha.yaml"] = "theme:\n  colors:\n",
            ["catppuccin-latte.yaml"] = "theme:\n  colors:\n"
        };

        ThemeFileLocator.EnsureBundledThemeFiles(
            configPath,
            path => files.ContainsKey(path),
            (path, content) => files[path] = content,
            path => directories.Add(path),
            bundledThemes);

        var expectedThemeDir = Path.Combine("/home/tester", ".config", "ripsharp", "themes");
        var expectedMochaPath = Path.Combine(expectedThemeDir, "catppuccin-mocha.yaml");
        var expectedLattePath = Path.Combine(expectedThemeDir, "catppuccin-latte.yaml");

        directories.Should().Contain(expectedThemeDir);
        files.Should().ContainKey(expectedMochaPath);
        files.Should().ContainKey(expectedLattePath);
        files[expectedMochaPath].Should().NotBeNullOrWhiteSpace();
        files[expectedLattePath].Should().NotBeNullOrWhiteSpace();
    }
}
