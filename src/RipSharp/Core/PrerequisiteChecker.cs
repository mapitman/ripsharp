namespace BugZapperLabs.RipSharp.Core;

internal static class PrerequisiteChecker
{
    internal static readonly string[] RequiredTools =
    {
        "makemkvcon",
        "ffmpeg"
    };

    internal static IReadOnlyList<string> GetMissingTools(string? pathValue, bool isWindows, Func<string, bool> fileExists)
    {
        var missing = new List<string>();

        foreach (var tool in RequiredTools)
        {
            if (!IsToolAvailable(tool, pathValue, isWindows, fileExists))
            {
                missing.Add(tool);
            }
        }

        return missing;
    }

    internal static bool IsToolAvailable(string tool, string? pathValue, bool isWindows, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return false;
        }

        foreach (var dir in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = dir.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            foreach (var candidate in GetExecutableCandidates(tool, isWindows))
            {
                var fullPath = Path.Combine(trimmed, candidate);
                if (fileExists(fullPath))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<string> GetExecutableCandidates(string tool, bool isWindows)
    {
        if (!isWindows)
        {
            yield return tool;
            yield break;
        }

        if (Path.HasExtension(tool))
        {
            yield return tool;
            yield break;
        }

        yield return tool + ".exe";
        yield return tool + ".cmd";
        yield return tool + ".bat";
    }
}
