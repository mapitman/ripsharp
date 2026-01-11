namespace RipSharp;

/// <summary>
/// Interface for prompting users for input during interactive operations.
/// </summary>
public interface IUserPrompt
{
    /// <summary>
    /// Prompts the user to select between movie or TV series mode.
    /// </summary>
    /// <param name="detectionHint">Optional hint about what was detected (e.g., "detected as movie with 65% confidence")</param>
    /// <returns>True for TV series, false for movie</returns>
    bool PromptForContentType(string? detectionHint = null);
}
