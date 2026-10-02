using System;
using System.Collections.Generic;
using RobotSNAP.Metrics;
using UnityEngine.UIElements;

/// <summary>
/// The overview half of the analysis dashboard: the headline numbers, one tile each, and the outcome mix of
/// the episodes it was handed. It is the two things a reader compares a benchmark by - "is this policy
/// getting better?" and "how did these runs end?".
///
/// It is deliberately given a list rather than a session. A session is only the widest possible list: the
/// reader who checks three episodes is asking the same two questions about those three, and an overview that
/// could only answer for the whole session would have to be rebuilt - or quietly lie - the moment a filter or
/// a checkbox narrowed what is on screen. The <c>scope</c> caption is what keeps the numbers honest: an
/// average that does not say what it averaged is not a measurement.
///
/// It is deliberately separate from the episode detail too. The two answer different questions and live in
/// different places on the page, so one view that carried both would have to be moved around the layout
/// instead of being placed once.
/// </summary>
public sealed class AnalysisSessionSummary
{
    /// <summary>Order the outcome mix is listed in: the project's own vocabulary, ending on "unknown".</summary>
    private static readonly string[] OutcomeOrder =
    {
        MetricsContract.OutcomeGoal,
        MetricsContract.OutcomeCollision,
        MetricsContract.OutcomeOutOfBounds,
        MetricsContract.OutcomeTimeout,
        MetricsContract.OutcomeStopped,
        MetricsContract.OutcomeUnknown,
    };

    /// <summary>Caption of the widest scope there is, so the caller never spells it twice.</summary>
    public const string WholeSession = "every episode the session ran";

    private readonly Label _overviewCaption;
    private readonly Label _distributionCaption;
    private readonly VisualElement _overviewTiles;
    private readonly VisualElement _distribution;

    public AnalysisSessionSummary(VisualElement host)
    {
        host.AddToClassList("analysis-session-summary");
        host.Add(Section("Session overview", "analysis-summary-overview", out _overviewCaption, out _overviewTiles));
        host.Add(Section("Outcomes", "analysis-summary-outcomes", out _distributionCaption, out _distribution));

        Show(null, WholeSession);
    }

    /// <summary>
    /// Draws the overview of one list of episodes and says which list it was. The summary keeps no copy of
    /// the episodes: it is redrawn whenever the session, the filters or the checked set change, so what is on
    /// screen is always the overview of what is on screen beside it.
    /// </summary>
    public void Show(IReadOnlyList<EpisodeMetrics> episodes, string scope)
    {
        string subject = string.IsNullOrEmpty(scope) ? WholeSession : scope;
        _overviewCaption.text = $"Averages and totals over {subject}.";
        _distributionCaption.text = $"Outcome mix over {subject}.";

        RefreshOverview(episodes);
        RefreshDistribution(episodes);
    }

    /// <summary>The caption for a scoped selection of <paramref name="count"/> episodes.</summary>
    public static string SelectionScope(int count)
        => count == 1 ? "the selected episode" : $"the {count} selected episodes";

    private void RefreshOverview(IReadOnlyList<EpisodeMetrics> episodes)
    {
        _overviewTiles.Clear();

        int total = episodes?.Count ?? 0;
        int goals = CountOutcome(episodes, MetricsContract.OutcomeGoal);
        double successRate = total > 0 ? (double)goals / total : double.NaN;

        AddTile("Episodes", AnalysisFormatting.Count(total));
        AddTile("Success rate", AnalysisFormatting.Percent(successRate));
        AddTile("Mean world duration", AnalysisFormatting.Seconds(Mean(episodes, episode => episode.WorldSeconds)));
        AddTile("Mean path length", AnalysisFormatting.Metres(Mean(episodes, episode => episode.PathLengthMetres)));
        AddTile("Mean max speed", AnalysisFormatting.Speed(Mean(episodes, episode => episode.MaxSpeedMetresPerSecond)));
        AddTile("Mean closest human", HumanMean(episodes));
        AddTile("Personal space intrusions", AnalysisFormatting.Count(Sum(episodes, episode => episode.PersonalSpaceIntrusions)));
    }

    private void RefreshDistribution(IReadOnlyList<EpisodeMetrics> episodes)
    {
        _distribution.Clear();

        int total = episodes?.Count ?? 0;
        if (total == 0)
        {
            _distribution.Add(EmptyMessage("No episode to count."));
            return;
        }

        foreach (string outcome in OutcomeOrder)
        {
            int count = CountOutcome(episodes, outcome);
            if (count == 0)
                continue;

            var row = new VisualElement();
            row.AddToClassList("analysis-distribution-row");

            var name = new Label(AnalysisFormatting.OutcomeName(outcome));
            name.AddToClassList("analysis-distribution-name");
            name.AddToClassList(AnalysisFormatting.OutcomeClass(outcome));
            row.Add(name);

            var countLabel = new Label(AnalysisFormatting.Count(count));
            countLabel.AddToClassList("analysis-distribution-count");
            row.Add(countLabel);

            var track = new VisualElement();
            track.AddToClassList("analysis-bar-track");
            var fill = new VisualElement();
            fill.AddToClassList("analysis-bar-fill");
            fill.AddToClassList(AnalysisFormatting.OutcomeClass(outcome));
            fill.style.width = Length.Percent((float)(100.0 * count / total));
            track.Add(fill);
            row.Add(track);

            _distribution.Add(row);
        }
    }

    private void AddTile(string caption, string value)
    {
        var tile = new VisualElement();
        tile.AddToClassList("analysis-tile");
        var valueLabel = new Label(value);
        valueLabel.AddToClassList("analysis-tile-value");
        var captionLabel = new Label(caption);
        captionLabel.AddToClassList("analysis-tile-caption");
        tile.Add(valueLabel);
        tile.Add(captionLabel);
        _overviewTiles.Add(tile);
    }

    /// <summary>
    /// One half of the overview. Each carries its own class so the wide band - the one a multi-episode
    /// selection takes - can lay the two side by side and tell which one holds the divider between them.
    /// </summary>
    private static VisualElement Section(
        string title,
        string className,
        out Label caption,
        out VisualElement body)
    {
        var section = new VisualElement();
        section.AddToClassList("analysis-section");
        section.AddToClassList(className);

        var heading = new VisualElement();
        heading.AddToClassList("analysis-section-heading");
        var text = new VisualElement();
        text.AddToClassList("analysis-section-text");
        var titleLabel = new Label(title);
        titleLabel.AddToClassList("analysis-section-title");
        caption = new Label();
        caption.AddToClassList("analysis-section-subtitle");
        text.Add(titleLabel);
        text.Add(caption);
        heading.Add(text);
        section.Add(heading);

        body = new VisualElement();
        body.AddToClassList("analysis-section-body");
        section.Add(body);
        return section;
    }

    private static Label EmptyMessage(string text)
    {
        var label = new Label(text);
        label.AddToClassList("analysis-empty-message");
        return label;
    }

    private static int CountOutcome(IReadOnlyList<EpisodeMetrics> episodes, string outcome)
    {
        int count = 0;
        if (episodes == null)
            return count;

        foreach (EpisodeMetrics episode in episodes)
        {
            if (string.Equals(episode.Outcome, outcome, StringComparison.Ordinal))
                count++;
        }
        return count;
    }

    private static int Sum(IReadOnlyList<EpisodeMetrics> episodes, Func<EpisodeMetrics, int> selector)
    {
        int total = 0;
        if (episodes == null)
            return total;

        foreach (EpisodeMetrics episode in episodes)
            total += selector(episode);
        return total;
    }

    /// <summary>
    /// Mean of a metric over the episodes that measured it. An episode that never saw a human reports the
    /// no-human sentinel, so it is left out rather than averaged in as a zero; an empty selection is not a
    /// measurement either, so it comes back as NaN and prints as n/a.
    /// </summary>
    private static double Mean(IReadOnlyList<EpisodeMetrics> episodes, Func<EpisodeMetrics, double> selector)
    {
        double sum = 0.0;
        int count = 0;
        if (episodes == null)
            return double.NaN;

        foreach (EpisodeMetrics episode in episodes)
        {
            double value = selector(episode);
            if (double.IsNaN(value) || value < 0.0)
                continue;
            sum += value;
            count++;
        }
        return count > 0 ? sum / count : double.NaN;
    }

    private static string HumanMean(IReadOnlyList<EpisodeMetrics> episodes)
    {
        double mean = Mean(episodes, episode => episode.MinHumanDistanceMetres);
        return AnalysisFormatting.HasHumanReading(mean)
            ? AnalysisFormatting.Metres(mean)
            : AnalysisFormatting.NoHumans;
    }
}
