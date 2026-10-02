using System;
using System.Collections.Generic;
using RobotSNAP.Metrics;
using UnityEngine;
using UnityEngine.UIElements;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Wires the Analysis tab: the episode list, the trajectory map, and the panel beside it - the metrics of the
/// one episode the list points at, or the overview of the episodes the reader checked - all reading the
/// session's <see cref="MetricsStore"/>.
///
/// The tab owns what the views only report as intents: choosing the export folder, asking before a delete, and
/// deciding which of the two right-hand panels the selection asks for. Keeping that here is what lets the list
/// and the panels stay views of the data instead of owners of a policy.
///
/// It initializes lazily, the first time the tab is opened, because that is when the UXML template is
/// instantiated into the UIDocument - the same reason the other tab controllers wait for their own view.
/// Once built it watches the store from <c>Update</c> so an episode that ends while the tab is open shows up
/// without the reader doing anything.
/// </summary>
public class AnalysisTabController : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;

    private bool _initialized;
    private VisualElement _page;
    private AnalysisSession _session;
    private AnalysisEpisodeList _list;
    private AnalysisEpisodeMap _map;
    private AnalysisSessionSummary _summary;
    private AnalysisEpisodeDetail _detail;
    private AnalysisEpisodeReplay _replay;
    private AnalysisTimeline _timeline;
    private VisualElement _mapPanel;
    private VisualElement _detailColumn;
    private VisualElement _timelineHost;
    private VisualElement _detailHost;
    private VisualElement _summaryHost;
    private Button _exportButton;
    private Button _clearButton;
    private Label _exportStatus;

    private void OnEnable()
    {
        MainViewController.OnViewLoaded += OnViewLoaded;
        TryInitialize();
    }

    private void OnDisable()
    {
        MainViewController.OnViewLoaded -= OnViewLoaded;
    }

    private void OnDestroy()
    {
        Detach();
    }

    /// <summary>
    /// A finished episode is a pull of two comparisons, so polling every frame costs nothing and spares the
    /// metrics layer an event it does not have.
    /// </summary>
    private void Update()
    {
        // The tab is torn down and rebuilt on its own schedule, and Unity can tick a frame while the view it
        // was built on is gone. Polling nothing is not a failure - there is simply nothing to poll yet.
        if (_initialized && _session != null)
            _session.Poll();

        // Playback is measured in real seconds and never in the simulator's. Replaying an episode is a
        // reading of the record, not a run of the world, so the time scale - which the reader may have set to
        // study a live run - has nothing to say about how fast the record is read back.
        _replay?.Advance(Time.unscaledDeltaTime);
    }

    private void OnViewLoaded(string viewName)
    {
        if (viewName != "Analysis")
            return;

        TryInitialize();
        _session?.Refresh();
    }

    private void TryInitialize()
    {
        if (_initialized)
            return;

        UIDocument document = ResolveDocument();
        if (document == null)
            return;

        VisualElement root = document.rootVisualElement;
        if (root == null || root.Q<VisualElement>("AnalysisPage") == null)
            return; // The tab has not been shown yet, so its template is not in the tree.

        var missing = new List<string>();
        _page = Require<VisualElement>(root, "AnalysisPage", missing);
        _exportButton = Require<Button>(root, "AnalysisExportButton", missing);
        _clearButton = Require<Button>(root, "AnalysisClearButton", missing);
        _exportStatus = Require<Label>(root, "AnalysisExportStatus", missing);
        VisualElement listHost = Require<VisualElement>(root, "AnalysisEpisodeListHost", missing);
        VisualElement detailHost = Require<VisualElement>(root, "AnalysisEpisodeDetailHost", missing);
        VisualElement summaryHost = Require<VisualElement>(root, "AnalysisSessionSummaryHost", missing);
        _mapPanel = Require<VisualElement>(root, "AnalysisMapPanel", missing);
        _detailColumn = Require<VisualElement>(root, "AnalysisDetailColumn", missing);
        VisualElement timelineHost = Require<VisualElement>(root, "AnalysisTimelineHost", missing);
        if (missing.Count > 0)
        {
            Debug.LogError($"[AnalysisTabController] Missing UI elements: {string.Join(", ", missing)}");
            return;
        }

        _session = new AnalysisSession();

        _detailHost = detailHost;
        _summaryHost = summaryHost;
        _timelineHost = timelineHost;

        _map = new AnalysisEpisodeMap(root);
        _list = new AnalysisEpisodeList();
        listHost.Add(_list);
        _detail = new AnalysisEpisodeDetail(detailHost);
        _summary = new AnalysisSessionSummary(summaryHost);

        // The cursor the map, the numbers and the frise all read: one object, so the three of them can only
        // ever be talking about the same instant.
        _replay = new AnalysisEpisodeReplay();
        _timeline = new AnalysisTimeline();
        _timelineHost.Add(_timeline);

        _list.EpisodeSelected += OnEpisodeSelected;
        _list.ExportRequested += OnExportEpisodeRequested;
        _list.DeleteRequested += OnDeleteEpisodeRequested;
        _list.SelectionChanged += RefreshView;
        _session.Changed += RefreshView;
        _map.Bind(_session);
        _map.SetReplay(_replay);
        _list.Bind(_session);
        _detail.Bind(_session);
        _detail.SetReplay(_replay);
        _timeline.Bind(_replay);
        _timeline.SeekRequested += OnSeekRequested;
        _timeline.StepRequested += OnStepRequested;
        _timeline.StepBackRequested += OnStepBackRequested;
        _timeline.RewindRequested += OnRewindRequested;
        _timeline.TogglePlayRequested += OnTogglePlayRequested;
        RefreshView();

        _exportButton.clicked += OnExportSessionClicked;
        _clearButton.clicked += OnClearSessionClicked;
        _initialized = true;
    }

    /// <summary>
    /// The UIDocument is authored on the HUD, not on this object, so it is resolved as the one whose tree
    /// actually holds the analysis page. That keeps the controller inert until its view exists instead of
    /// failing on a document that happens to be found first.
    /// </summary>
    private UIDocument ResolveDocument()
    {
        if (uiDocument != null)
            return uiDocument;

        uiDocument = GetComponent<UIDocument>();
        if (uiDocument != null)
            return uiDocument;

        foreach (UIDocument candidate in FindObjectsByType<UIDocument>())
        {
            VisualElement candidateRoot = candidate.rootVisualElement;
            if (candidateRoot != null && candidateRoot.Q<VisualElement>("AnalysisPage") != null)
            {
                uiDocument = candidate;
                break;
            }
        }

        return uiDocument;
    }

    private static T Require<T>(VisualElement root, string name, ICollection<string> missing) where T : VisualElement
    {
        T element = root.Q<T>(name);
        if (element == null)
            missing.Add(name);
        return element;
    }

    private void OnEpisodeSelected(string id)
    {
        _session.Select(id);
    }

    /// <summary>
    /// Puts the right panel in the mode the selection asks for. The rule is the checkbox count, and the three
    /// cases are the three things a reader does with a session:
    ///
    ///   none checked    the overview of the whole session - what the tab opens on, and what "Clear selection"
    ///                   goes back to
    ///   one checked     that episode, metric by metric, beside its own trajectory and under the frise that
    ///                   replays it. A click on a row is a single selection, so this is the case a reader
    ///                   meets first
    ///   several checked the overview again - scoped to the checked episodes, averaging the subset the reader
    ///                   asked about - and it takes the whole band: there is no one trajectory to draw for
    ///                   several runs at once, and a map of one of them would say the reader asked about a run
    ///                   the summary beside it is not describing
    ///
    /// The overview keeps its own caption of what it averaged, so a scoped reading can never be mistaken for
    /// the session's.
    /// </summary>
    private void RefreshView()
    {
        if (_session == null || _summary == null)
            return;

        IReadOnlyList<EpisodeMetrics> checkedEpisodes = _list.CheckedEpisodes;
        int checkedCount = checkedEpisodes.Count;
        bool episodeMode = ShowsEpisodeDetail(checkedCount);
        bool trajectories = ShowsTrajectoryMap(checkedCount);

        _detailHost.style.display = episodeMode ? DisplayStyle.Flex : DisplayStyle.None;
        _summaryHost.style.display = episodeMode ? DisplayStyle.None : DisplayStyle.Flex;
        _timelineHost.style.display = episodeMode ? DisplayStyle.Flex : DisplayStyle.None;
        _mapPanel.style.display = trajectories ? DisplayStyle.Flex : DisplayStyle.None;
        // With the map gone the detail column is all that is left beside the list, so it takes the band.
        _detailColumn.EnableInClassList("analysis-detail-column-wide", !trajectories);
        // And in a band that wide the overview reads across rather than down: the tiles answer "how well" and
        // the outcome mix answers "how did it end", which are two columns of the same reading, not a stack.
        _summaryHost.EnableInClassList("analysis-summary-wide", !trajectories);

        if (episodeMode)
        {
            _replay.Show(_session.Selected);
            return;
        }

        // No cursor outside the one-episode view. What is left of the map draws the whole run the list points
        // at, and a frise would be replaying a run the panels beside it are not describing.
        _replay.Show(null);

        if (checkedCount == 0)
            _summary.Show(_session.Episodes, AnalysisSessionSummary.WholeSession);
        else
            _summary.Show(checkedEpisodes, AnalysisSessionSummary.SelectionScope(checkedCount));
    }

    /// <summary>
    /// Which of the two right-hand panels a selection asks for. Exactly one checked episode is a single
    /// selection - that run, beside its own trajectory. Nothing checked is the whole session, and several
    /// checked are those several: both of those are the overview, scoped to what the reader asked about.
    /// </summary>
    public static bool ShowsEpisodeDetail(int checkedCount) => checkedCount == 1;

    /// <summary>
    /// Whether the band still carries a trajectory map. Only a single checked episode has one trajectory to
    /// draw: nothing checked opens on the session overview alone, and several checked is an overview of
    /// several runs at once - neither has one run to put beside the summary, so both take the whole band.
    /// </summary>
    public static bool ShowsTrajectoryMap(int checkedCount) => checkedCount == 1;

    // -- replay ---------------------------------------------------------------

    /// <summary>
    /// The transport's five intents. Taking hold of the frise and stepping it stop playback, because a reader
    /// who moves the cursor is reading one instant rather than watching the run; play is what starts it again.
    /// </summary>
    private void OnSeekRequested(double seconds)
    {
        _replay?.Pause();
        _replay?.Seek(seconds);
    }

    private void OnStepRequested()
    {
        _replay?.Pause();
        _replay?.Step();
    }

    private void OnStepBackRequested()
    {
        _replay?.Pause();
        _replay?.StepBack();
    }

    private void OnRewindRequested()
    {
        _replay?.Pause();
        _replay?.Rewind();
    }

    private void OnTogglePlayRequested() => _replay?.TogglePlay();

    // -- export ---------------------------------------------------------------

    /// <summary>Exports the whole session to a folder the user picks, and names where it landed.</summary>
    private void OnExportSessionClicked()
    {
        if (_session == null)
            return;

        ChooseExportFolder(folder =>
        {
            MetricsExportReport report = MetricsExporter.Export(_session.Store, folder);
            int episodes = _session.Episodes.Count;
            _exportStatus.text = Describe(report, $"{episodes} episode{(episodes == 1 ? "" : "s")}");
        });
    }

    /// <summary>Exports the selected episode on its own, to the folder the user picks.</summary>
    private void OnExportEpisodeRequested(string id)
    {
        if (_session == null)
            return;

        ChooseExportFolder(folder =>
        {
            MetricsExportReport report = MetricsExporter.ExportEpisode(_session.Store, id, folder);
            _exportStatus.text = Describe(report, "episode " + id);
        });
    }

    /// <summary>
    /// Runs <paramref name="export"/> with a folder the user picked. In the editor that is the native folder
    /// picker; a player has none without a plugin, so with no dialog already open it asks with a field
    /// carrying the last folder, and with one open it writes to that last folder - two stacked modals would
    /// be worse than a documented fallback.
    /// </summary>
    private void ChooseExportFolder(Action<string> export, ConfirmationDialog parent = null)
    {
#if UNITY_EDITOR
        string folder = EditorUtility.SaveFolderPanel(
            "Choose the folder the metrics are exported to",
            AnalysisExportFolder.Last,
            string.Empty);

        if (string.IsNullOrEmpty(folder))
            return; // Closing the picker is a decision, not a failure.

        AnalysisExportFolder.Last = folder;
        export(folder);
#else
        if (parent != null)
        {
            export(AnalysisExportFolder.Last);
            return;
        }

        ShowExportFolderDialog(export);
#endif
    }

    /// <summary>
    /// The outline of a finished export, or the reason it did not happen. The write itself belongs to
    /// <see cref="MetricsExporter"/>; this only reports its outcome.
    /// </summary>
    private static string Describe(MetricsExportReport report, string subject)
        => string.IsNullOrEmpty(report.Directory)
            ? "Export failed: " + (MetricsExporter.LastError ?? "unknown error")
            : $"Exported {subject} to {report.Directory}";

    // -- destructive actions --------------------------------------------------

    /// <summary>
    /// Empties the session, after a confirmation that says so. The dialog is reused rather than rebuilt
    /// because the same warning applies to deleting one episode, and a reader who has met one has met the
    /// other.
    /// </summary>
    private void OnClearSessionClicked()
    {
        if (_session == null)
            return;

        int episodes = _session.Episodes.Count;
        ConfirmationDialog dialog = new ConfirmationDialog(
            "Clear this session?",
            episodes == 0
                ? "The session holds no episode to clear."
                : $"This removes all {episodes} episode{(episodes == 1 ? "" : "s")} of the session from the " +
                  "Analysis list. It cannot be undone. Export first if you want to keep the records.",
            "Clear session",
            alternateText: "Export first");

        dialog.Alternate += () => ExportBeforeDestructiveAction(
            dialog, folder => MetricsExporter.Export(_session.Store, folder));
        dialog.Confirmed += () =>
        {
            _session.Store.Clear();
            _session.Refresh();
        };

        if (episodes == 0)
        {
            dialog.ConfirmButton.SetEnabled(false);
            dialog.AlternateButton.SetEnabled(false);
        }

        dialog.Show(_page);
    }

    /// <summary>Deletes the selected episode, after the same confirmation the session clear uses.</summary>
    private void OnDeleteEpisodeRequested(string id)
    {
        EpisodeMetrics episode = _session?.Store.Get(id);
        if (episode == null)
            return;

        ConfirmationDialog dialog = new ConfirmationDialog(
            "Delete this episode?",
            $"This removes episode {AnalysisFormatting.EpisodeLabel(episode)} from the session. It cannot be " +
            "undone. Export first if you want to keep it.",
            "Delete episode",
            alternateText: "Export first");

        dialog.Alternate += () => ExportBeforeDestructiveAction(
            dialog, folder => MetricsExporter.ExportEpisode(_session.Store, id, folder));
        dialog.Confirmed += () =>
        {
            _session.Store.Remove(id);
            _session.Refresh();
        };

        dialog.Show(_page);
    }

    /// <summary>
    /// The "export first" of a destructive dialog: writes the records the user asked for, leaves the dialog
    /// open while it writes them, and closes it once the write is done. A failure is the outcome the reader
    /// has to act on, so it stays in front of them inside the dialog; a finished export is an answer, and the
    /// question that asked for it has been answered - leaving the dialog up would only ask it again.
    /// </summary>
    private void ExportBeforeDestructiveAction(ConfirmationDialog dialog, Func<string, MetricsExportReport> export)
    {
        ChooseExportFolder(
            folder =>
            {
                (string status, bool close) = ExportOutcome(export(folder), MetricsExporter.LastError);
                if (!close)
                {
                    dialog.SetStatus(status, error: true);
                    return;
                }

                _exportStatus.text = status;
                dialog.Dismiss();
            },
            dialog);
    }

    /// <summary>
    /// What one answered export from a dialog leaves behind: the line that reports it, and whether the dialog
    /// that asked for it is done. A failed write is the one outcome the reader has to act on, so it stays in
    /// the dialog where they are already looking; a finished export is an answer, and the question has been
    /// answered. Split out from the handler so the rule can be read - and tested - without the editor's folder
    /// picker standing in the way.
    /// </summary>
    public static (string Status, bool CloseDialog) ExportOutcome(MetricsExportReport report, string error)
        => string.IsNullOrEmpty(report.Directory)
            ? ("Export failed: " + (string.IsNullOrEmpty(error) ? "unknown error" : error), false)
            : ("Exported to " + report.Directory, true);

#if !UNITY_EDITOR
    /// <summary>
    /// Asks for the export folder when no native picker exists. This is the editor's picker replaced by a
    /// field, not a different feature: the folder the user types is the folder every later export uses.
    /// </summary>
    private void ShowExportFolderDialog(Action<string> export)
    {
        ConfirmationDialog dialog = new ConfirmationDialog(
            "Export metrics",
            "Choose the folder the exported files are written to.",
            "Export",
            destructive: false);

        var field = new TextField("Folder") { value = AnalysisExportFolder.Last };
        field.AddToClassList("confirmation-field");
        dialog.Body.Add(field);

        dialog.Confirmed += () =>
        {
            string folder = field.value?.Trim();
            if (string.IsNullOrEmpty(folder))
            {
                dialog.SetStatus("Enter a folder path.", error: true);
                return;
            }

            AnalysisExportFolder.Last = folder;
            export(folder);
        };

        dialog.Show(_page);
    }
#endif

    private void Detach()
    {
        if (_exportButton != null)
            _exportButton.clicked -= OnExportSessionClicked;
        if (_clearButton != null)
            _clearButton.clicked -= OnClearSessionClicked;
        if (_list != null)
        {
            _list.EpisodeSelected -= OnEpisodeSelected;
            _list.ExportRequested -= OnExportEpisodeRequested;
            _list.DeleteRequested -= OnDeleteEpisodeRequested;
            _list.SelectionChanged -= RefreshView;
        }
        if (_session != null)
            _session.Changed -= RefreshView;

        if (_timeline != null)
        {
            _timeline.SeekRequested -= OnSeekRequested;
            _timeline.StepRequested -= OnStepRequested;
            _timeline.StepBackRequested -= OnStepBackRequested;
            _timeline.RewindRequested -= OnRewindRequested;
            _timeline.TogglePlayRequested -= OnTogglePlayRequested;
        }

        _list?.Dispose();
        _map?.Dispose();
        _detail?.Dispose();
        _timeline?.Dispose();

        _list = null;
        _map = null;
        _summary = null;
        _detail = null;
        _replay = null;
        _timeline = null;
        _mapPanel = null;
        _detailColumn = null;
        _timelineHost = null;
        _detailHost = null;
        _summaryHost = null;
        _session = null;
        _page = null;
        _initialized = false;
    }
}
