using System;
using System.Collections.Generic;
using System.Linq;

namespace BugZapperLabs.RipSharp.Services;

/// <summary>
/// Detects the type of content on a disc (movie vs TV series) based on structural analysis.
/// </summary>
public class DiscTypeDetector : IDiscTypeDetector
{
    /// <summary>
    /// Detects whether a disc contains a movie or TV series based on its structure.
    /// Movies are characterized by:
    /// - Fewer, longer titles (typically 1-3 main titles)
    /// - Significant duration differences between titles
    /// - Clear separation (main feature vs bonus content)
    /// </summary>
    /// <param name="discInfo">The disc information containing titles and metadata.</param>
    /// <returns>Tuple of (isTV, confidence): True if detected as TV series, false if detected as movie, null if detection is uncertain, along with confidence score (0.0-1.0).</returns>
    public (bool? isTV, double confidence) DetectContentType(DiscInfo discInfo)
    {
        if (discInfo.Titles == null || discInfo.Titles.Count == 0)
            return (null, 0.0);

        // Single title is almost always a movie
        if (discInfo.Titles.Count == 1)
        {
            return (false, 0.95);
        }

        // Two titles are likely a movie (main feature + bonus)
        if (discInfo.Titles.Count == 2)
        {
            var (isMovie, confidence) = AnalyzeTwoTitles(discInfo.Titles);
            return (isMovie ? false : null, confidence); // Return false for movie, null for uncertain
        }

        // For 3+ titles, analyze duration consistency and patterns
        return AnalyzeMultipleTitles(discInfo.Titles);
    }

    /// <summary>
    /// Analyzes a disc with exactly two titles to determine if it's a movie.
    /// Typically: main feature + bonus content.
    /// </summary>
    private (bool IsMovie, double Confidence) AnalyzeTwoTitles(List<TitleInfo> titles)
    {
        if (titles.Count != 2)
            return (false, 0.0);

        var longerDuration = Math.Max(titles[0].DurationSeconds, titles[1].DurationSeconds);
        var shorterDuration = Math.Min(titles[0].DurationSeconds, titles[1].DurationSeconds);

        // Filter out zero-duration titles
        if (longerDuration == 0)
            return (false, 0.3); // Uncertain

        // If longer title is significantly longer (at least 3x), likely movie + bonus
        if (shorterDuration > 0 && longerDuration >= shorterDuration * 3)
        {
            return (true, 0.85);
        }

        // If both are substantial (> 30 min each) and similar, might be movie with alternate cut
        if (longerDuration > 1800 && shorterDuration > 1800)
        {
            var ratio = longerDuration / (double)shorterDuration;
            if (ratio < 1.3) // Within 30% of each other
                return (true, 0.75); // Likely alternate cuts of same movie
        }

        // Uncertain case
        return (false, 0.5);
    }

    /// <summary>
    /// Analyzes a disc with 3 or more titles.
    /// Movies typically have 1-2 titles; TV series have multiple similar-length titles.
    /// </summary>
    private (bool? isTV, double confidence) AnalyzeMultipleTitles(List<TitleInfo> titles)
    {
        if (titles.Count < 3)
            return (null, 0.5);

        // Thresholds and buckets
        const int shortCutoffSeconds = 900;            // <= 15 min considered "short" (likely bonus/chapters)
        const int tvMinSeconds = 1200;                  // 20 min
        const int tvMaxSeconds = 3300;                  // 55 min
        const int movieFeatureSeconds = 4800;           // 80 min main feature indicator

        var shortTitles = titles.Where(t => t.DurationSeconds <= shortCutoffSeconds).ToList();
        var tvLikely = titles.Where(t => t.DurationSeconds >= tvMinSeconds && t.DurationSeconds <= tvMaxSeconds).ToList();
        var longTitles = titles.Where(t => t.DurationSeconds > movieFeatureSeconds).ToList();

        // Heuristic: strong TV signal when most titles cluster in TV episode range with low variation
        if (tvLikely.Count >= 3 && tvLikely.Count >= titles.Count * 0.6)
        {
            var tvDurations = tvLikely.Select(t => t.DurationSeconds).ToList();
            var tvAvg = tvDurations.Average();

            // Guard against division by zero
            if (tvAvg > 0)
            {
                var tvStdDev = Math.Sqrt(CalculateVariance(tvDurations));
                var cv = tvStdDev / tvAvg; // coefficient of variation

                if (cv < 0.15)
                {
                    return (true, 0.92);
                }

                if (cv < 0.25)
                {
                    return (true, 0.78);
                }
            }
        }

        // If there is one very long title and many shorts, likely a movie with bonus content
        if (longTitles.Count == 1 && shortTitles.Count >= 2 && titles.Count >= 4)
        {
            return (false, 0.85);
        }

        // If there are 2 long titles and few tv-length titles, treat as movie (alt cuts)
        if (longTitles.Count == 2 && tvLikely.Count <= 1)
        {
            return (false, 0.75);
        }

        // If all titles are substantial (no shorts) and low variance overall, lean TV
        var substantial = titles.Where(t => t.DurationSeconds > shortCutoffSeconds).ToList();
        if (substantial.Count >= 3 && shortTitles.Count == 0)
        {
            var substDurations = substantial.Select(t => t.DurationSeconds).ToList();
            var avg = substDurations.Average();

            // Guard against division by zero
            if (avg > 0)
            {
                var stdDev = Math.Sqrt(CalculateVariance(substDurations));
                var cv = stdDev / avg;

                if (cv < 0.18)
                {
                    return (true, 0.8);
                }
            }
        }

        // Movie default when many shorts and few substantial titles
        if (shortTitles.Count >= 3 && substantial.Count <= 2)
        {
            return (false, 0.7);
        }

        // Uncertain
        return (null, 0.5);
    }

    /// <summary>
    /// Calculates the variance of a collection of values.
    /// </summary>
    private double CalculateVariance(IEnumerable<int> values)
    {
        var valueList = values.ToList();
        if (valueList.Count == 0)
            return 0.0;

        var mean = valueList.Average();
        var squaredDiffs = valueList.Select(v => Math.Pow(v - mean, 2));
        return squaredDiffs.Average();
    }
}
