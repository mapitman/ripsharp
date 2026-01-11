using System.Threading.Tasks;

namespace RipSharp;

public class NullEpisodeTitleProvider : ITvEpisodeTitleProvider
{
    public Task<string?> GetEpisodeTitleAsync(string seriesTitle, int season, int episode, int? year)
    {
        return Task.FromResult<string?>(null);
    }
}
