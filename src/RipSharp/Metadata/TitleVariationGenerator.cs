namespace BugZapperLabs.RipSharp.Metadata;

public static class TitleVariationGenerator
{
    public static List<string> Generate(string title)
    {
        var variations = new List<string> { title };
        var current = title;

        while (true)
        {
            // Find the last occurrence of a non-alphanumeric character (excluding leading/trailing spaces)
            current = current.TrimEnd();
            int lastSeparator = -1;

            // Look for the last non-alphanumeric or space separator
            for (int i = current.Length - 1; i >= 0; i--)
            {
                if (!char.IsLetterOrDigit(current[i]))
                {
                    lastSeparator = i;
                    break;
                }
            }

            if (lastSeparator == -1)
                break;

            current = current.Substring(0, lastSeparator);
            if (string.IsNullOrWhiteSpace(current))
                break;

            variations.Add(current.TrimEnd());
        }

        return variations;
    }
}
