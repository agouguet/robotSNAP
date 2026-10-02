using System;
using System.Globalization;
using RobotSNAP.Metrics;

/// <summary>
/// How the analysis tab prints a number. Every metric of an episode is a physical quantity, so its text
/// always carries the unit it was measured in; a quantity the episode could not measure - the distance to
/// a human in an episode that held none - says so instead of printing a zero that would read like a
/// measurement. The formatting lives in one place so the tiles, the table and the map caption cannot
/// disagree on what a value means.
/// </summary>
public static class AnalysisFormatting
{
    /// <summary>Text a value carries when the episode did not measure it.</summary>
    public const string Unavailable = "n/a";

    /// <summary>Text the human-distance metrics carry when the episode held no human at all.</summary>
    public const string NoHumans = "no humans";

    /// <summary>Simulated seconds, the unit an episode time limit is measured in.</summary>
    public static string Seconds(double value)
        => Measured(value) ? value.ToString("0.00", CultureInfo.InvariantCulture) + " s" : Unavailable;

    /// <summary>A distance in metres.</summary>
    public static string Metres(double value)
        => Measured(value) ? value.ToString("0.00", CultureInfo.InvariantCulture) + " m" : Unavailable;

    /// <summary>A speed in metres per second.</summary>
    public static string Speed(double value)
        => Measured(value) ? value.ToString("0.00", CultureInfo.InvariantCulture) + " m/s" : Unavailable;

    /// <summary>A whole count, for example how many control steps an episode sampled.</summary>
    public static string Count(int value)
        => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// What an episode is called on screen: its place in the session, zero-padded, and the scenario it ran -
    /// <c>01_default</c>. Every episode of a session that repeats one scenario carries the same scenario id,
    /// so the index is the only thing that tells two runs apart in the list.
    ///
    /// An episode written before the index existed - or a document built by a test - has no place to show,
    /// so it falls back to its scenario alone rather than pretending to be number zero.
    /// </summary>
    public static string EpisodeLabel(EpisodeMetrics episode)
    {
        if (episode == null)
            return Unavailable;

        string scenario = string.IsNullOrEmpty(episode.Scenario) ? "(no scenario)" : episode.Scenario;
        return episode.Index > 0
            ? episode.Index.ToString("D2", CultureInfo.InvariantCulture) + "_" + scenario
            : scenario;
    }

    /// <summary>A ratio in percent, for a success rate.</summary>
    public static string Percent(double fraction)
        => Measured(fraction) ? (fraction * 100.0).ToString("0.#", CultureInfo.InvariantCulture) + " %" : Unavailable;

    /// <summary>
    /// A distance to the nearest human. A negative value is the sentinel the accumulator writes when the
    /// episode never saw a human, so it becomes <see cref="NoHumans"/> rather than "0.00 m".
    /// </summary>
    public static string HumanDistance(double value)
        => HasHumanReading(value) ? value.ToString("0.00", CultureInfo.InvariantCulture) + " m" : NoHumans;

    /// <summary>
    /// The gap left between two bodies. It reads in the same unit as a distance, but unlike one it may be
    /// negative: two outlines that overlap have a negative clearance, and a reader has to see that rather
    /// than be told there was nobody to measure. Only the no-human sentinel means "nothing to measure".
    /// </summary>
    public static string Clearance(double value)
        => double.IsNaN(value) || value == EpisodeMetrics.NoHumanDistance
            ? NoHumans
            : value.ToString("0.00", CultureInfo.InvariantCulture) + " m";

    /// <summary>True when a human-distance metric holds a reading instead of the no-human sentinel.</summary>
    public static bool HasHumanReading(double value) => !double.IsNaN(value) && value >= 0.0;

    /// <summary>The project's outcome vocabulary, spelled the way a reader expects to read it.</summary>
    public static string OutcomeName(string outcome)
    {
        switch (outcome)
        {
            case MetricsContract.OutcomeGoal: return "Reached goal";
            case MetricsContract.OutcomeCollision: return "Collision";
            case MetricsContract.OutcomeOutOfBounds: return "Out of bounds";
            case MetricsContract.OutcomeTimeout: return "Timed out";
            case MetricsContract.OutcomeStopped: return "Stopped";
            case MetricsContract.OutcomeUnknown: return "Unknown";
            default: return string.IsNullOrEmpty(outcome) ? "Unknown" : outcome;
        }
    }

    /// <summary>USS class carrying the colour an outcome reads in, so status stays legible at a glance.</summary>
    public static string OutcomeClass(string outcome)
    {
        switch (outcome)
        {
            case MetricsContract.OutcomeGoal: return "analysis-outcome-goal";
            case MetricsContract.OutcomeCollision: return "analysis-outcome-collision";
            case MetricsContract.OutcomeOutOfBounds: return "analysis-outcome-out-of-bounds";
            case MetricsContract.OutcomeTimeout: return "analysis-outcome-timeout";
            case MetricsContract.OutcomeStopped: return "analysis-outcome-stopped";
            default: return "analysis-outcome-unknown";
        }
    }

    /// <summary>
    /// An ISO-8601 instant from the store, printed short. An instant a future format change could make
    /// unreadable is shown untouched rather than dropped, so a row never loses the only timestamp it has.
    /// </summary>
    public static string Timestamp(string iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
            return Unavailable;

        if (DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed))
            return parsed.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "Z";

        return iso;
    }

    private static bool Measured(double value) => !double.IsNaN(value) && value >= 0.0;
}
