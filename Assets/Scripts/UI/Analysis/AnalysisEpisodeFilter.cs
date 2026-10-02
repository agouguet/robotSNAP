using System;
using System.Collections.Generic;
using RobotSNAP.Metrics;

/// <summary>
/// Which episodes of the session the list shows.
///
/// A benchmarking session runs the same scenario many times, so a reader who wants one outcome, one
/// scenario, or the runs that actually came near somebody is asking a question about a subset of the
/// session - and everything downstream of the list has to agree on that subset. The filter is therefore a
/// plain object with no UI on it: the toolbar writes it, the list reads it, and a test can ask it the same
/// question a reader does without opening the tab.
///
/// An unset criterion matches everything, so a filter with nothing set shows the whole session.
/// </summary>
public sealed class AnalysisEpisodeFilter
{
    /// <summary>Value of a criterion that was never set: match everything.</summary>
    public const string Any = "";

    /// <summary>Outcome an episode has to have ended as, or <see cref="Any"/>.</summary>
    public string Outcome { get; set; } = Any;

    /// <summary>Scenario an episode has to have run, or <see cref="Any"/>.</summary>
    public string Scenario { get; set; } = Any;

    /// <summary>Text the episode's label has to contain, case-insensitively. Blank matches everything.</summary>
    public string Search { get; set; } = string.Empty;

    /// <summary>When set, only the episodes whose robot entered a human's personal space at least once.</summary>
    public bool IntrusionsOnly { get; set; }

    /// <summary>Whether any criterion is set, so a caller can say the list is showing a subset.</summary>
    public bool IsActive =>
        !string.IsNullOrEmpty(Outcome) ||
        !string.IsNullOrEmpty(Scenario) ||
        !string.IsNullOrEmpty(Search) ||
        IntrusionsOnly;

    /// <summary>Whether one episode is one of the episodes the filter keeps.</summary>
    public bool Matches(EpisodeMetrics episode)
    {
        if (episode == null)
            return false;

        if (!string.IsNullOrEmpty(Outcome) &&
            !string.Equals(episode.Outcome, Outcome, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrEmpty(Scenario) &&
            !string.Equals(episode.Scenario, Scenario, StringComparison.Ordinal))
            return false;

        if (IntrusionsOnly && episode.PersonalSpaceIntrusions <= 0)
            return false;

        string search = (Search ?? string.Empty).Trim();
        if (search.Length > 0 &&
            AnalysisFormatting.EpisodeLabel(episode).IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
            return false;

        return true;
    }

    /// <summary>The episodes the filter keeps, in the order they were given.</summary>
    public List<EpisodeMetrics> Apply(IReadOnlyList<EpisodeMetrics> episodes)
    {
        var kept = new List<EpisodeMetrics>();
        if (episodes == null)
            return kept;

        foreach (EpisodeMetrics episode in episodes)
        {
            if (Matches(episode))
                kept.Add(episode);
        }
        return kept;
    }
}
