using System.IO;

namespace RipSharp;

public static class FileNaming
{
    public static string SanitizeFileName(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var ch in invalid)
            s = s.Replace(ch.ToString(), "");
        return s.Trim();
    }

    public static string RenameFile(string filePath, Metadata metadata, int? episodeNum, int seasonNum, string? versionSuffix = null, string? episodeTitle = null)
    {
        var title = metadata.Title.Trim();
        var year = metadata.Year;
        var mediaType = metadata.Type;
        var safeTitle = SanitizeFileName(title);
        var safeSuffix = string.IsNullOrWhiteSpace(versionSuffix) ? "" : $" {SanitizeFileName(versionSuffix.Trim())}";
        string filename;
        if (mediaType == "tv" && episodeNum.HasValue)
        {
            var safeEpisodeTitle = string.IsNullOrWhiteSpace(episodeTitle) ? "" : $" - {SanitizeFileName(episodeTitle)}";
            filename = $"{safeTitle} - S{seasonNum:00}E{episodeNum.Value:00}{safeEpisodeTitle}.mkv";
        }
        else
        {
            var yearPart = year.HasValue ? $" ({year.Value})" : "";
            filename = $"{safeTitle}{yearPart}{safeSuffix}.mkv";
        }
        var newPath = Path.Combine(Path.GetDirectoryName(filePath)!, filename);
        if (File.Exists(newPath)) File.Delete(newPath);
        File.Move(filePath, newPath);
        return newPath;
    }
}
