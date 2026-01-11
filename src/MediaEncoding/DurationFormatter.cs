namespace MediaEncoding;

public static class DurationFormatter
{
    public static string Format(int seconds)
    {
        if (seconds <= 0)
            return "Unknown duration";

        int hours = seconds / 3600;
        int minutes = (seconds % 3600) / 60;
        int secs = seconds % 60;

        if (hours > 0)
            return $"{hours}h {minutes}m {secs}s";
        else if (minutes > 0)
            return $"{minutes}m {secs}s";
        else
            return $"{secs}s";
    }
}
