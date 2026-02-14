using System.Threading.Tasks;

namespace BugZapperLabs.RipSharp.Abstractions;

public interface ITvEpisodeTitleProvider
{
    Task<string?> GetEpisodeTitleAsync(string seriesTitle, int season, int episode, int? year);
}
