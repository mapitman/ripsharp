using System.Threading.Tasks;

namespace BugZapperLabs.RipSharp.Metadata;

public class NullEpisodeTitleProvider : ITvEpisodeTitleProvider
{
    public Task<string?> GetEpisodeTitleAsync(string seriesTitle, int season, int episode, int? year)
    {
        return Task.FromResult<string?>(null);
    }
}
