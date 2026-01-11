using Microsoft.Extensions.Configuration;

namespace RipSharp;

public class AppConfig
{
    public DiscConfig Disc { get; set; } = new();
    public OutputConfig Output { get; set; } = new();
    public EncodingConfig Encoding { get; set; } = new();
    public MetadataConfig Metadata { get; set; } = new();
}

public class DiscConfig
{
    public string? Default_Path { get; set; }
    public string? Default_Temp_Dir { get; set; }
}

public class OutputConfig
{
    public string? Movies_Dir { get; set; }
    public string? Tv_Dir { get; set; }
}

public class EncodingConfig
{
    public bool Include_English_Subtitles { get; set; } = true;
    public bool Include_Stereo_Audio { get; set; } = true;
    public bool Include_Surround_Audio { get; set; } = true;
}

public class MetadataConfig
{
    public bool Lookup_Enabled { get; set; } = true;
    public string? Omdb_Api_Key { get; set; }
    public string? Tmdb_Api_Key { get; set; }
    public string? Tvdb_Api_Key { get; set; }
}
