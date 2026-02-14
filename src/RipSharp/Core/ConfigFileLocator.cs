using System.Reflection;

namespace BugZapperLabs.RipSharp.Core;

internal sealed record ConfigSearchContext(
    string? XdgConfigHome,
    string? HomeDirectory,
    string? ApplicationDataDirectory,
    string CurrentDirectory,
    bool IsWindows,
    bool IsMac,
    bool IsLinux);

internal static class ConfigFileLocator
{
    internal const string AppName = "ripsharp";
    internal const string DefaultConfigFileName = "config.yaml";
    internal const string LocalConfigFileName = "ripsharp.yaml";
    internal const string LocalAppSettingsFileName = "appsettings.yaml";

    internal const string DefaultConfigResourceName = "BugZapperLabs.RipSharp.DefaultConfig";

    internal const string DefaultConfigFallbackContents = "disc:\n" +
                                                         "  default_path: \"disc:0\"\n" +
                                                         "  default_temp_dir: \"/tmp/makemkv\"\n\n" +
                                                         "output:\n" +
                                                         "  movies_dir: \"~/Movies\"\n" +
                                                         "  tv_dir: \"~/TV\"\n\n" +
                                                         "encoding:\n" +
                                                         "  include_english_subtitles: true\n" +
                                                         "  include_stereo_audio: true\n" +
                                                         "  include_surround_audio: true\n\n" +
                                                         "metadata:\n" +
                                                         "  lookup_enabled: true\n";

    internal static ConfigSearchContext CreateContext()
    {
        return new ConfigSearchContext(
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Directory.GetCurrentDirectory(),
            OperatingSystem.IsWindows(),
            OperatingSystem.IsMacOS(),
            OperatingSystem.IsLinux());
    }

    internal static IReadOnlyList<string> GetCandidatePaths(ConfigSearchContext context)
    {
        var candidates = new List<string>();

        if (context.IsLinux)
        {
            var xdgPath = GetLinuxXdgConfigPath(context);
            if (!string.IsNullOrWhiteSpace(xdgPath))
            {
                candidates.Add(xdgPath);
            }

            var homeConfigPath = GetLinuxHomeConfigPath(context);
            if (!string.IsNullOrWhiteSpace(homeConfigPath) && !PathsEqual(homeConfigPath, xdgPath))
            {
                candidates.Add(homeConfigPath);
            }

            var homeDotFile = GetHomeDotFilePath(context);
            if (!string.IsNullOrWhiteSpace(homeDotFile))
            {
                candidates.Add(homeDotFile);
            }
        }
        else
        {
            var preferred = GetPreferredConfigPath(context);
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                candidates.Add(preferred);
            }

            var homeDotFile = GetHomeDotFilePath(context);
            if (!string.IsNullOrWhiteSpace(homeDotFile) && !PathsEqual(homeDotFile, preferred))
            {
                candidates.Add(homeDotFile);
            }
        }

        AddCurrentDirectoryCandidates(context, candidates);
        return candidates;
    }

    internal static string? ResolveConfigPath(
        ConfigSearchContext context,
        bool isDevelopment,
        Func<string, bool> fileExists,
        Action<string, string> writeFile,
        Action<string> ensureDirectory)
    {
        var candidates = GetCandidatePaths(context);
        var existing = FindExistingConfigPath(candidates, fileExists);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        if (isDevelopment)
        {
            return null;
        }

        var preferred = GetFirstPersonalConfigPath(context);
        if (string.IsNullOrWhiteSpace(preferred))
        {
            return null;
        }

        var preferredDir = Path.GetDirectoryName(preferred);
        if (!string.IsNullOrWhiteSpace(preferredDir))
        {
            ensureDirectory(preferredDir);
        }

        if (!fileExists(preferred))
        {
            writeFile(preferred, GetDefaultConfigContents());
        }

        return preferred;
    }

    internal static string? FindExistingConfigPath(IEnumerable<string> candidates, Func<string, bool> fileExists)
    {
        foreach (var candidate in candidates)
        {
            if (fileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    internal static string? GetPreferredConfigPath(ConfigSearchContext context)
    {
        if (context.IsLinux)
        {
            return GetLinuxXdgConfigPath(context) ?? GetLinuxHomeConfigPath(context);
        }

        if (!string.IsNullOrWhiteSpace(context.ApplicationDataDirectory))
        {
            return Path.Combine(context.ApplicationDataDirectory, AppName, DefaultConfigFileName);
        }

        return GetHomeDotFilePath(context);
    }

    internal static string? GetFirstPersonalConfigPath(ConfigSearchContext context)
    {
        foreach (var candidate in GetPersonalConfigPaths(context))
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    internal static IReadOnlyList<string> GetPersonalConfigPaths(ConfigSearchContext context)
    {
        var candidates = new List<string>();

        if (context.IsLinux)
        {
            var xdgPath = GetLinuxXdgConfigPath(context);
            if (!string.IsNullOrWhiteSpace(xdgPath))
            {
                candidates.Add(xdgPath);
            }

            var homeConfigPath = GetLinuxHomeConfigPath(context);
            if (!string.IsNullOrWhiteSpace(homeConfigPath) && !PathsEqual(homeConfigPath, xdgPath))
            {
                candidates.Add(homeConfigPath);
            }

            var homeDotFile = GetHomeDotFilePath(context);
            if (!string.IsNullOrWhiteSpace(homeDotFile))
            {
                candidates.Add(homeDotFile);
            }

            return candidates;
        }

        if (!string.IsNullOrWhiteSpace(context.ApplicationDataDirectory))
        {
            candidates.Add(Path.Combine(context.ApplicationDataDirectory, AppName, DefaultConfigFileName));
        }

        var homeDot = GetHomeDotFilePath(context);
        if (!string.IsNullOrWhiteSpace(homeDot))
        {
            candidates.Add(homeDot);
        }

        return candidates;
    }

    internal static string GetDefaultConfigContents()
    {
        var embedded = ReadEmbeddedDefaultConfig();
        return string.IsNullOrWhiteSpace(embedded) ? DefaultConfigFallbackContents : embedded;
    }

    private static string? ReadEmbeddedDefaultConfig()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(DefaultConfigResourceName);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string? GetLinuxXdgConfigPath(ConfigSearchContext context)
    {
        if (string.IsNullOrWhiteSpace(context.XdgConfigHome))
        {
            return null;
        }

        return Path.Combine(context.XdgConfigHome, AppName, DefaultConfigFileName);
    }

    private static string? GetLinuxHomeConfigPath(ConfigSearchContext context)
    {
        if (string.IsNullOrWhiteSpace(context.HomeDirectory))
        {
            return null;
        }

        return Path.Combine(context.HomeDirectory, ".config", AppName, DefaultConfigFileName);
    }

    private static string? GetHomeDotFilePath(ConfigSearchContext context)
    {
        if (string.IsNullOrWhiteSpace(context.HomeDirectory))
        {
            return null;
        }

        return Path.Combine(context.HomeDirectory, ".ripsharp.yaml");
    }

    private static void AddCurrentDirectoryCandidates(ConfigSearchContext context, List<string> candidates)
    {
        if (string.IsNullOrWhiteSpace(context.CurrentDirectory))
        {
            return;
        }

        candidates.Add(Path.Combine(context.CurrentDirectory, LocalConfigFileName));
        candidates.Add(Path.Combine(context.CurrentDirectory, LocalAppSettingsFileName));
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
