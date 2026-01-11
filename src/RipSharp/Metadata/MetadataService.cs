using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RipSharp.Metadata;

public class MetadataService : IMetadataService
{
    private readonly List<IMetadataProvider> _providers;
    private readonly IConsoleWriter _notifier;

    public MetadataService(IEnumerable<IMetadataProvider> providers, IConsoleWriter notifier)
    {
        _notifier = notifier;
        _providers = providers.ToList();
    }

    public async Task<ContentMetadata?> LookupAsync(string title, bool isTv, int? year)
    {
        var titleVariations = TitleVariationGenerator.Generate(title);

        foreach (var titleVariation in titleVariations)
        {
            foreach (var provider in _providers)
            {
                var result = await provider.LookupAsync(titleVariation, isTv, year);
                if (result != null)
                {
                    if (titleVariation != title)
                        _notifier.Success($"✓ {provider.Name} {(isTv ? "TV" : "Movie")} lookup found using simplified title '{titleVariation}': '{result.Title}'" + (result.Year.HasValue ? $" ({result.Year.Value})" : ""));
                    else
                        _notifier.Success($"✓ {provider.Name} {(isTv ? "TV" : "Movie")} lookup found: '{result.Title}'" + (result.Year.HasValue ? $" ({result.Year.Value})" : ""));
                    return result;
                }
            }
        }

        _notifier.Warning($"⚠️ No metadata found from available providers for '{title}'. Using disc title as fallback.");
        return new ContentMetadata { Title = title, Year = year, Type = isTv ? "tv" : "movie" };
    }
}
