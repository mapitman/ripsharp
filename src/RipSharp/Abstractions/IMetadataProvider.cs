namespace BugZapperLabs.RipSharp.Abstractions;

public interface IMetadataProvider
{
    string Name { get; }
    Task<ContentMetadata?> LookupAsync(string title, bool isTv, int? year);
}
