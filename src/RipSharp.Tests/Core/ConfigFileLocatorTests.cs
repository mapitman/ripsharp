namespace RipSharp.Tests.Core;

public class ConfigFileLocatorTests
{
    [Fact]
    public void GetCandidatePaths_Linux_WithXdgConfigHome_UsesExpectedOrder()
    {
        var context = new ConfigSearchContext(
            "/xdg",
            "/home/tester",
            "/appdata",
            "/work",
            IsWindows: false,
            IsMac: false,
            IsLinux: true);

        var candidates = ConfigFileLocator.GetCandidatePaths(context);

        candidates.Should().ContainInOrder(
            Path.Combine("/xdg", "ripsharp", "config.yaml"),
            Path.Combine("/home/tester", ".config", "ripsharp", "config.yaml"),
            Path.Combine("/home/tester", ".ripsharp.yaml"),
            Path.Combine("/work", "ripsharp.yaml"),
            Path.Combine("/work", "appsettings.yaml"));
    }

    [Fact]
    public void GetCandidatePaths_Linux_WithoutXdgConfigHome_UsesHomeConfigFirst()
    {
        var context = new ConfigSearchContext(
            null,
            "/home/tester",
            "/appdata",
            "/work",
            IsWindows: false,
            IsMac: false,
            IsLinux: true);

        var candidates = ConfigFileLocator.GetCandidatePaths(context);

        candidates.Should().ContainInOrder(
            Path.Combine("/home/tester", ".config", "ripsharp", "config.yaml"),
            Path.Combine("/home/tester", ".ripsharp.yaml"),
            Path.Combine("/work", "ripsharp.yaml"),
            Path.Combine("/work", "appsettings.yaml"));
    }

    [Fact]
    public void ResolveConfigPath_CreatesConfigWhenMissingAndNotDevelopment()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"ripsharp-config-{Path.GetRandomFileName()}");
        var xdgDir = Path.Combine(tempRoot, "xdg");
        Directory.CreateDirectory(xdgDir);

        try
        {
            var context = new ConfigSearchContext(
                xdgDir,
                Path.Combine(tempRoot, "home"),
                Path.Combine(tempRoot, "appdata"),
                Path.Combine(tempRoot, "work"),
                IsWindows: false,
                IsMac: false,
                IsLinux: true);

            var configPath = ConfigFileLocator.ResolveConfigPath(
                context,
                isDevelopment: false,
                File.Exists,
                File.WriteAllText,
                path => Directory.CreateDirectory(path));

            configPath.Should().Be(Path.Combine(xdgDir, "ripsharp", "config.yaml"));
            File.Exists(configPath).Should().BeTrue();
            var contents = File.ReadAllText(configPath!);
            contents.Should().Contain("disc:");
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolveConfigPath_DoesNotCreateConfigInDevelopment()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"ripsharp-config-{Path.GetRandomFileName()}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var context = new ConfigSearchContext(
                tempRoot,
                Path.Combine(tempRoot, "home"),
                Path.Combine(tempRoot, "appdata"),
                Path.Combine(tempRoot, "work"),
                IsWindows: false,
                IsMac: false,
                IsLinux: true);

            var configPath = ConfigFileLocator.ResolveConfigPath(
                context,
                isDevelopment: true,
                File.Exists,
                File.WriteAllText,
                path => Directory.CreateDirectory(path));

            configPath.Should().BeNull();
            File.Exists(Path.Combine(tempRoot, "ripsharp", "config.yaml")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolveConfigPath_NonLinux_UsesApplicationDataDirectory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"ripsharp-config-{Path.GetRandomFileName()}");
        var appDataDir = Path.Combine(tempRoot, "appdata");
        Directory.CreateDirectory(appDataDir);

        try
        {
            var context = new ConfigSearchContext(
                null,
                Path.Combine(tempRoot, "home"),
                appDataDir,
                Path.Combine(tempRoot, "work"),
                IsWindows: true,
                IsMac: false,
                IsLinux: false);

            var configPath = ConfigFileLocator.ResolveConfigPath(
                context,
                isDevelopment: false,
                File.Exists,
                File.WriteAllText,
                path => Directory.CreateDirectory(path));

            configPath.Should().Be(Path.Combine(appDataDir, "ripsharp", "config.yaml"));
            File.Exists(configPath).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void GetDefaultConfigContents_UsesEmbeddedAppSettings()
    {
        var contents = ConfigFileLocator.GetDefaultConfigContents();

        contents.Should().Contain("disc:");
        contents.Should().Contain("output:");
        contents.Should().Contain("encoding:");
        contents.Should().Contain("metadata:");
    }
}
