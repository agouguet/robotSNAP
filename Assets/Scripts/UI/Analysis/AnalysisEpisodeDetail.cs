using System;
using System.Collections.Generic;
using RobotSNAP.Metrics;
using UnityEngine.UIElements;

/// <summary>
/// The episode half of the analysis dashboard: the one episode a reader picked, metric by metric.
///
/// It reads that run at the replay cursor, not only whole. Positioned anywhere but the end of the episode,
/// the rows a trajectory can answer - duration, path length, speeds, the human distances, the personal space
/// - show the prefix up to that instant and keep the value the run ended on beside them, because a number
/// read halfway through an episode is only meaningful next to the number it is heading for. At the end of
/// the episode the rows read the recorded values themselves: the prefix of a whole run is the run, and
/// recomputing it would only invite the panel to disagree with the caption above it.
///
/// It carries no action of its own. Exporting a run and deleting it belong to the row that names the run, in
/// the list where the reader is already pointing: an action on the panel that describes a run would have to
/// be found again every time the panel is shown, and a reader scanning a benchmark never looks for it there.
///
/// The detail reads the session through <see cref="AnalysisSession"/> and the cursor through
/// <see cref="AnalysisEpisodeReplay"/>, and detaches from both in <see cref="Dispose"/>, like the other views
/// of the tab.
/// </summary>
public sealed class AnalysisEpisodeDetail : IDisposable
{
    private readonly VisualElement _body;
    private readonly Label _subject;

    private AnalysisSession _session;
    private AnalysisEpisodeReplay _replay;

    // The rows a moving cursor rewrites: the value label of every metric that reads the prefix, and how to
    // read it again. Kept across ticks so advancing the frise rewrites text instead of building a panel.
    private readonly List<(Label Label, Func<AnalysisPrefixMetrics, string> Read)> _readings = new();
    private bool _built;
    private EpisodeMetrics _builtFor;
    private bool _builtScrubbed;

    public AnalysisEpisodeDetail(VisualElement host)
    {
        host.AddToClassList("analysis-section");
        host.AddToClassList("analysis-episode-panel");

        var heading = new VisualElement();
        heading.AddToClassList("analysis-section-heading");

        var text = new VisualElement();
        text.AddToClassList("analysis-section-text");
        var title = new Label("Episode detail");
        title.AddToClassList("analysis-section-title");
        var subtitle = new Label("The episode the list is pointing at, metric by metric.");
        subtitle.AddToClassList("analysis-section-subtitle");
        text.Add(title);
        text.Add(subtitle);
        heading.Add(text);
        host.Add(heading);

        // The episode names itself on its own line, where the width of the panel is not shared with anything.
        _subject = new Label();
        _subject.AddToClassList("analysis-detail-subject");
        _subject.style.display = DisplayStyle.None;
        host.Add(_subject);

        _body = new VisualElement();
        _body.AddToClassList("analysis-section-body");
        _body.AddToClassList("analysis-detail");
        host.Add(_body);
    }

    /// <summary>Follows one session, from now on and immediately.</summary>
    public void Bind(AnalysisSession session)
    {
        if (_session != null)
            _session.Changed -= OnChanged;

        _session = session;
        if (_session != null)
            _session.Changed += OnChanged;

        OnChanged();
    }

    /// <summary>
    /// Follows the replay cursor of the episode the session points at, so every move of the frise redraws the
    /// numbers. A null cursor is the whole episode, which is what the panel showed before there was one.
    /// </summary>
    public void SetReplay(AnalysisEpisodeReplay replay)
    {
        if (_replay != null)
            _replay.Changed -= OnChanged;

        _replay = replay;
        if (_replay != null)
            _replay.Changed += OnChanged;

        OnChanged();
    }

    /// <summary>Detaches from the session and the cursor; a disposed detail never draws again.</summary>
    public void Dispose()
    {
        if (_session != null)
            _session.Changed -= OnChanged;
        if (_replay != null)
            _replay.Changed -= OnChanged;

        _session = null;
        _replay = null;
    }

    private void OnChanged()
    {
        if (_session == null)
            return;

        RefreshDetail(_session.Selected);
    }

    private void RefreshDetail(EpisodeMetrics episode)
    {
        // Reading the prefix is what moving the cursor changes, and the rows it feeds are rewritten in place
        // below. Only a change of run - or the step from a scrubbed reading back to the whole episode - asks
        // for the panel to be laid out again: rebuilding it every frame is what made a long replay slow down.
        AnalysisPrefixMetrics prefix = PrefixFor(episode);
        bool scrubbed = prefix.HasRobot;
        if (!_built || !ReferenceEquals(_builtFor, episode) || _builtScrubbed != scrubbed)
        {
            BuildDetail(episode, prefix, scrubbed);
            _built = true;
            _builtFor = episode;
            _builtScrubbed = scrubbed;
        }

        if (episode == null)
            return;

        foreach ((Label label, Func<AnalysisPrefixMetrics, string> read) in _readings)
            label.text = read(prefix);
    }

    /// <summary>
    /// Lays the panel out for one run and one state of the cursor. Only a change of run, or a change in
    /// whether a cursor is being read at all, calls this - so the visual elements a reader sees are built once
    /// and the ticks of a playback only rewrite the readings they hold.
    /// </summary>
    private void BuildDetail(EpisodeMetrics episode, AnalysisPrefixMetrics prefix, bool scrubbed)
    {
        _body.Clear();
        _readings.Clear();
        if (episode == null)
        {
            _subject.text = string.Empty;
            _subject.style.display = DisplayStyle.None;
            var empty = new Label("Select an episode in the list to read its metrics.");
            empty.AddToClassList("analysis-empty-message");
            _body.Add(empty);
            return;
        }

        _subject.text = "Episode " + AnalysisFormatting.EpisodeLabel(episode);
        _subject.style.display = string.IsNullOrEmpty(_subject.text) ? DisplayStyle.None : DisplayStyle.Flex;

        // Only the metrics a trajectory can shorten are read at the cursor; the identity of the run, its wall
        // clock and the geometry of its mission are properties of the whole episode.
        var performance = new List<MetricRowSpec>
        {
            Fixed("Episode id", Text(episode.Id)),
            Compared("World duration", scrubbed, prefix, p => p.Seconds, episode.WorldSeconds,
                AnalysisFormatting.Seconds),
            Fixed("Wall-clock duration", AnalysisFormatting.Seconds(episode.WallSeconds)),
            Fixed("Control steps", AnalysisFormatting.Count(episode.Steps)),
            Compared("Path length", scrubbed, prefix, p => p.PathLengthMetres, episode.PathLengthMetres,
                AnalysisFormatting.Metres),
            Fixed("Straight line to goal", AnalysisFormatting.Metres(episode.StraightLineMetres)),
            Compared("Average speed", scrubbed, prefix, p => p.AverageSpeedMetresPerSecond,
                episode.AverageSpeedMetresPerSecond, AnalysisFormatting.Speed),
            Compared("Max speed", scrubbed, prefix, p => p.MaxSpeedMetresPerSecond,
                episode.MaxSpeedMetresPerSecond, AnalysisFormatting.Speed),
            Fixed("Time to goal", TimeToGoal(episode)),
        };

        var social = new List<MetricRowSpec>
        {
            Compared("Closest human", scrubbed, prefix, p => p.MinHumanDistanceMetres,
                episode.MinHumanDistanceMetres,
                AnalysisFormatting.HumanDistance),
            Compared("Mean nearest human", scrubbed, prefix, p => p.AverageHumanDistanceMetres,
                episode.AverageHumanDistanceMetres, AnalysisFormatting.HumanDistance),
            Compared("Closest clearance", scrubbed, prefix, p => p.MinClearanceMetres, episode.MinClearanceMetres,
                AnalysisFormatting.Clearance),
            Compared("Personal space intrusions", scrubbed, prefix, p => p.PersonalSpaceIntrusions,
                episode.PersonalSpaceIntrusions, value => AnalysisFormatting.Count((int)Math.Round(value))),
            Compared("Time in personal space", scrubbed, prefix, p => p.PersonalSpaceSeconds,
                episode.PersonalSpaceSeconds, AnalysisFormatting.Seconds),
            Fixed("Personal space radius", AnalysisFormatting.Metres(episode.PersonalSpaceRadiusMetres)),
            Fixed(FleetCaption(episode), FleetLabel(episode)),
            Fixed("Scenario", string.IsNullOrEmpty(episode.Scenario) ? "n/a" : episode.Scenario),
            Fixed("Started at", AnalysisFormatting.Timestamp(episode.StartedAt)),
        };

        var panels = new VisualElement();
        panels.AddToClassList("analysis-detail-panels");
        panels.Add(MetricPanel("Performance", performance));
        panels.Add(MetricPanel("Social", social));
        _body.Add(panels);
    }

    /// <summary>
    /// The prefix the cursor asks for, or an unusable reading while the panel is showing the whole episode.
    /// The cursor only describes the episode the session points at, so a cursor left on a previous run by a
    /// selection change is not the cursor of this one.
    /// </summary>
    private AnalysisPrefixMetrics PrefixFor(EpisodeMetrics episode)
    {
        if (_replay == null || !_replay.IsScrubbed || !ReferenceEquals(_replay.Episode, episode))
            return default;

        return _replay.Metrics();
    }

    /// <summary>
    /// One row of the detail: what it is called, the reading it shows now, the value the run ended on - only
    /// while a cursor is inside the episode - and how to read the first of those again once the cursor has
    /// moved. A row that reads the run whole carries no reader, and the panel writes it once.
    /// </summary>
    private readonly struct MetricRowSpec
    {
        public MetricRowSpec(string label, string value, string final, Func<AnalysisPrefixMetrics, string> read)
        {
            Label = label;
            Value = value;
            Final = final;
            Read = read;
        }

        public string Label { get; }
        public string Value { get; }
        public string Final { get; }
        public Func<AnalysisPrefixMetrics, string> Read { get; }
    }

    /// <summary>A row whose reading is a property of the run and does not move with the cursor.</summary>
    private static MetricRowSpec Fixed(string label, string value)
        => new MetricRowSpec(label, value, null, null);

    /// <summary>
    /// One metric a prefix can shorten: what it stands at up to the cursor, and - only while the cursor is
    /// inside the episode - the value the run ended on, kept beside it so the two can be compared. At the end
    /// there is nothing to compare, so the row carries the recorded value alone.
    /// </summary>
    private static MetricRowSpec Compared(
        string label,
        bool scrubbed,
        AnalysisPrefixMetrics prefix,
        Func<AnalysisPrefixMetrics, double> reading,
        double atEnd,
        Func<double, string> format)
        => scrubbed
            ? new MetricRowSpec(label, format(reading(prefix)), format(atEnd), p => format(reading(p)))
            : new MetricRowSpec(label, format(atEnd), null, null);

    private static string Text(string value) => string.IsNullOrEmpty(value) ? AnalysisFormatting.Unavailable : value;

    /// <summary>"Robot" for a single tracked robot, "Robots" once the episode saw a fleet.</summary>
    private static string FleetCaption(EpisodeMetrics episode)
        => episode.Robots != null && episode.Robots.Count > 1 ? "Robots" : "Robot";

    /// <summary>
    /// The robot the episode followed, or the whole fleet in the recorder's order when it saw several, so the
    /// panel that calls the other robots robots says which ones they were.
    /// </summary>
    private static string FleetLabel(EpisodeMetrics episode)
    {
        if (episode.Robots == null || episode.Robots.Count == 0)
            return string.IsNullOrEmpty(episode.Robot) ? "robot" : episode.Robot;
        return string.Join(", ", episode.Robots);
    }

    /// <summary>
    /// The world time the robot needed to reach its goal, which only a run that actually reached it can
    /// report; every other outcome has no time to goal to show.
    /// </summary>
    private static string TimeToGoal(EpisodeMetrics episode)
        => episode.Outcome == MetricsContract.OutcomeGoal
            ? AnalysisFormatting.Seconds(episode.WorldSeconds)
            : AnalysisFormatting.Unavailable;

    private VisualElement MetricPanel(string title, IReadOnlyList<MetricRowSpec> rows)
    {
        var panel = new VisualElement();
        panel.AddToClassList("analysis-metric-panel");

        var titleLabel = new Label(title);
        titleLabel.AddToClassList("analysis-metric-panel-title");
        panel.Add(titleLabel);

        foreach (MetricRowSpec row in rows)
            panel.Add(MetricRow(row));

        return panel;
    }

    /// <summary>
    /// One metric row: its name, the value it reads now, and - while a cursor is inside the episode - the
    /// muted value the episode ended on. The value stays the second label of the row either way, so a reader
    /// (and the tests) find the reading in the same place whether or not anything is being replayed.
    ///
    /// A row that reads the prefix hands its value label to <see cref="_readings"/>, which is what lets the
    /// next tick of the replay rewrite the reading instead of rebuilding the row.
    /// </summary>
    private VisualElement MetricRow(MetricRowSpec spec)
    {
        var row = new VisualElement();
        row.AddToClassList("analysis-metric-row");

        var name = new Label(spec.Label);
        name.AddToClassList("analysis-metric-label");
        row.Add(name);

        var valueLabel = new Label(spec.Value);
        valueLabel.AddToClassList("analysis-metric-value");
        row.Add(valueLabel);
        if (spec.Read != null)
            _readings.Add((valueLabel, spec.Read));

        if (!string.IsNullOrEmpty(spec.Final))
        {
            var finalLabel = new Label("final " + spec.Final);
            finalLabel.AddToClassList("analysis-metric-final");
            row.Add(finalLabel);
        }

        return row;
    }
}
