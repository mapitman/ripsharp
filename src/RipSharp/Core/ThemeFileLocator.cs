using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BugZapperLabs.RipSharp.Core;

internal static class ThemeFileLocator
{
    internal const string ThemeDirectoryName = "themes";
    internal const string DefaultThemeFileName = "catppuccin-mocha.yaml";
    internal const string EmbeddedThemeResourcePrefix = "BugZapperLabs.RipSharp.themes.";

    internal static string? ResolveThemePath(string? themePath, string? configPath)
    {
        var baseDir = GetConfigDirectory(configPath) ?? AppContext.BaseDirectory;

        if (string.IsNullOrWhiteSpace(themePath))
        {
            return Path.Combine(baseDir, ThemeDirectoryName, DefaultThemeFileName);
        }

        if (Path.IsPathRooted(themePath))
        {
            return themePath;
        }

        var fileName = NormalizeThemeFileName(themePath);
        return Path.Combine(baseDir, ThemeDirectoryName, fileName);
    }

    internal static void EnsureBundledThemeFiles(
        string? configPath,
        Func<string, bool> fileExists,
        Action<string, string> writeFile,
        Action<string> ensureDirectory,
        IReadOnlyDictionary<string, string>? bundledThemes = null)
    {
        var baseDir = GetConfigDirectory(configPath);
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            return;
        }

        var themes = bundledThemes ?? ReadEmbeddedThemes();
        if (themes.Count == 0)
        {
            return;
        }

        var themeDir = Path.Combine(baseDir, ThemeDirectoryName);
        ensureDirectory(themeDir);

        foreach (var theme in themes)
        {
            if (string.IsNullOrWhiteSpace(theme.Key) || string.IsNullOrWhiteSpace(theme.Value))
            {
                continue;
            }

            var themePath = Path.Combine(themeDir, theme.Key);
            if (fileExists(themePath))
            {
                continue;
            }

            writeFile(themePath, theme.Value);
        }
    }

    internal static string? GetConfigDirectory(string? configPath)
    {
        return string.IsNullOrWhiteSpace(configPath)
            ? null
            : Path.GetDirectoryName(configPath);
    }

    private static string NormalizeThemeFileName(string themeName)
    {
        var trimmed = themeName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return DefaultThemeFileName;
        }

        var normalized = trimmed
            .Replace('_', '-')
            .Replace(' ', '-')
            .ToLowerInvariant();

        if (!normalized.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
        {
            normalized += ".yaml";
        }

        return normalized;
    }

    private static IReadOnlyDictionary<string, string> ReadEmbeddedThemes()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(EmbeddedThemeResourcePrefix, StringComparison.Ordinal))
            .Where(name => name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var themes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resourceName in resources)
        {
            var fileName = resourceName.Substring(EmbeddedThemeResourcePrefix.Length);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                continue;
            }

            using var reader = new StreamReader(stream);
            var contents = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(contents))
            {
                continue;
            }

            themes[fileName] = contents;
        }

        return themes;
    }
}
