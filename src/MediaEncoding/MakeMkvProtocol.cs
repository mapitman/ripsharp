using System.Text.RegularExpressions;

namespace MediaEncoding;

public static class MakeMkvProtocol
{
    // Extract the first quoted string from a line like: MSG:1005,0,0,"Some message"
    public static string? ExtractQuoted(string line)
    {
        var m = Regex.Match(line, "\"([^\"]+)\"");
        return m.Success ? m.Groups[1].Value : null;
    }
}
