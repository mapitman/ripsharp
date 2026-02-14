namespace RipSharp.Tests.Core;

public class PrerequisiteCheckerTests
{
    [Fact]
    public void GetMissingTools_WhenPathIsEmpty_ReturnsAllRequiredTools()
    {
        var missing = PrerequisiteChecker.GetMissingTools(null, isWindows: false, _ => false);

        missing.Should().Contain(PrerequisiteChecker.RequiredTools);
    }

    [Theory]
    [MemberData(nameof(NonWindowsAllPresentCases))]
    public void GetMissingTools_WhenNonWindowsPathsContainTools_ReturnsNoneMissing(string pathValue, string[] existingFiles)
    {
        var missing = PrerequisiteChecker.GetMissingTools(pathValue, isWindows: false, CreateSet(existingFiles).Contains);

        missing.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(NonWindowsMissingCases))]
    public void GetMissingTools_WhenNonWindowsMissingTool_ReturnsMissingTool(string pathValue, string[] existingFiles, string expectedMissing)
    {
        var missing = PrerequisiteChecker.GetMissingTools(pathValue, isWindows: false, CreateSet(existingFiles).Contains);

        missing.Should().ContainSingle().Which.Should().Be(expectedMissing);
    }

    [Theory]
    [MemberData(nameof(WindowsAllPresentCases))]
    public void GetMissingTools_WhenWindowsPathsContainExecutableExtensions_ReturnsNoneMissing(string pathValue, string[] existingFiles)
    {
        var missing = PrerequisiteChecker.GetMissingTools(pathValue, isWindows: true, CreateSet(existingFiles).Contains);

        missing.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(WindowsMissingCases))]
    public void GetMissingTools_WhenWindowsMissingTool_ReturnsMissingTool(string pathValue, string[] existingFiles, string expectedMissing)
    {
        var missing = PrerequisiteChecker.GetMissingTools(pathValue, isWindows: true, CreateSet(existingFiles).Contains);

        missing.Should().ContainSingle().Which.Should().Be(expectedMissing);
    }

    public static IEnumerable<object[]> NonWindowsAllPresentCases()
    {
        yield return new object[]
        {
            BuildPath("/usr/local/bin", "/usr/bin"),
            new[] { Path.Combine("/usr/bin", "makemkvcon"), Path.Combine("/usr/local/bin", "ffmpeg") }
        };

        yield return new object[]
        {
            BuildPath("/usr/bin", "/opt/bin"),
            new[] { Path.Combine("/usr/bin", "makemkvcon"), Path.Combine("/opt/bin", "ffmpeg") }
        };

        yield return new object[]
        {
            BuildPath("/opt/homebrew/bin", "/usr/local/bin"),
            new[] { Path.Combine("/opt/homebrew/bin", "ffmpeg"), Path.Combine("/usr/local/bin", "makemkvcon") }
        };
    }

    public static IEnumerable<object[]> NonWindowsMissingCases()
    {
        yield return new object[]
        {
            BuildPath("/usr/bin", "/opt/bin"),
            new[] { Path.Combine("/opt/bin", "ffmpeg") },
            "makemkvcon"
        };

        yield return new object[]
        {
            BuildPath("/usr/bin", "/opt/bin"),
            new[] { Path.Combine("/usr/bin", "makemkvcon") },
            "ffmpeg"
        };

        yield return new object[]
        {
            BuildPath("/opt/homebrew/bin", "/usr/local/bin"),
            new[] { Path.Combine("/opt/homebrew/bin", "ffmpeg") },
            "makemkvcon"
        };
    }

    public static IEnumerable<object[]> WindowsAllPresentCases()
    {
        var baseDir = Path.Combine("C_Tools");
        yield return new object[]
        {
            BuildPath(baseDir),
            new[] { Path.Combine(baseDir, "makemkvcon.exe"), Path.Combine(baseDir, "ffmpeg.cmd") }
        };
    }

    public static IEnumerable<object[]> WindowsMissingCases()
    {
        var baseDir = Path.Combine("C_Tools");
        yield return new object[]
        {
            BuildPath(baseDir),
            new[] { Path.Combine(baseDir, "makemkvcon.exe") },
            "ffmpeg"
        };

        yield return new object[]
        {
            BuildPath(baseDir),
            new[] { Path.Combine(baseDir, "ffmpeg.exe") },
            "makemkvcon"
        };
    }

    private static string BuildPath(params string[] parts)
    {
        return string.Join(Path.PathSeparator, parts);
    }

    private static HashSet<string> CreateSet(IEnumerable<string> paths)
    {
        return new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
    }
}
