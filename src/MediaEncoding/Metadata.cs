using System.Collections.Generic;
using System.Threading.Tasks;

namespace MediaEncoding;

public class Metadata
{
    public string Title { get; set; } = "Unknown";
    public int? Year { get; set; }
    public string Type { get; set; } = "movie"; // movie|tv
}
