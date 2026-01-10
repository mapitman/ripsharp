using System.IO;

namespace MediaEncoding;

public static class FileNaming
{
    public static string SanitizeFileName(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var ch in invalid)
            s = s.Replace(ch.ToString(), "");
        return s.Trim();
    }

    public static string RenameFile(string filePath, Metadata metadata, int? episodeNum, int seasonNum, string? versionSuffix = null)
    {
        var title = metadata.Title.Trim();
        var year = metadata.Year;
        var mediaType = metadata.Type;
        var safeTitle = SanitizeFileName(title);
        var safeSuffix = string.IsNullOrWhiteSpace(versionSuffix) ? "" : SanitizeFileName(versionSuffix);
        string filename;
        if (mediaType == "tv" && episodeNum.HasValue)
        {
            filename = $"{safeTitle} - S{seasonNum:00}E{episodeNum.Value:00}.mkv";
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
