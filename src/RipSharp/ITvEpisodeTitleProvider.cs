using System.Threading.Tasks;

namespace RipSharp;

public interface ITvEpisodeTitleProvider
{
    Task<string?> GetEpisodeTitleAsync(string seriesTitle, int season, int episode, int? year);
}
