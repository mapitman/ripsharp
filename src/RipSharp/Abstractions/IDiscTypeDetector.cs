namespace BugZapperLabs.RipSharp.Abstractions;

/// <summary>
/// Interface for detecting the type of content on a disc (movie vs TV series).
/// </summary>
public interface IDiscTypeDetector
{
    /// <summary>
    /// Detects whether a disc contains a movie or TV series based on its structure.
    /// </summary>
    /// <param name="discInfo">The disc information containing titles and metadata.</param>
    /// <returns>Tuple of (isTV, confidence): True if detected as TV series, false if detected as movie, null if detection is uncertain, along with confidence score (0.0-1.0).</returns>
    (bool? isTV, double confidence) DetectContentType(DiscInfo discInfo);
}
