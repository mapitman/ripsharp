namespace BugZapperLabs.RipSharp.Abstractions;

public interface IMetadataService
{
    Task<ContentMetadata?> LookupAsync(string title, bool isTv, int? year);
}
