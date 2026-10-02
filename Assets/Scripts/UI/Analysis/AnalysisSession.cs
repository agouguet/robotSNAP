using System;
using System.Collections.Generic;
using RobotSNAP.Metrics;

/// <summary>
/// What the analysis tab reads: the store's episodes, the one the user picked, and one change signal the
/// views subscribe to.
///
/// The store is a plain list the recorder appends to and has no event of its own, so this class watches the
/// two things a change can move - the session identity and the episode count - each time the tab's
/// <c>Update</c> ticks, and raises <see cref="Changed"/> only when one of them actually moved. That keeps a
/// finished episode a pull of two cheap comparisons away instead of a subscription the data layer would have
/// had to grow.
///
/// Views subscribe to <see cref="Changed"/> and drop the subscription in their own dispose, so a view that
/// is rebuilt never leaves a dead one behind.
/// </summary>
public sealed class AnalysisSession
{
    private readonly MetricsStore _store;
    private List<EpisodeMetrics> _episodes = new List<EpisodeMetrics>();
    private int _count;
    private string _sessionId;

    public AnalysisSession(MetricsStore store = null)
    {
        _store = store ?? MetricsStore.Instance;
        Refresh();
    }

    /// <summary>Raised after a refresh or a selection change; views redraw from the properties below.</summary>
    public event Action Changed;

    /// <summary>The store the export button writes, so the interface never needs a second reference.</summary>
    public MetricsStore Store => _store;

    /// <summary>Episodes of the session, oldest first - the order the store keeps them in.</summary>
    public IReadOnlyList<EpisodeMetrics> Episodes => _episodes;

    /// <summary>The selected episode, or null while the session holds none.</summary>
    public EpisodeMetrics Selected { get; private set; }

    /// <summary>
    /// Picks up a store change and returns whether one happened. Called every frame by the tab, the check is
    /// two scalar comparisons until an episode actually finishes.
    /// </summary>
    public bool Poll()
    {
        if (_store.Count == _count && string.Equals(_store.SessionId, _sessionId, StringComparison.Ordinal))
            return false;

        Refresh();
        return true;
    }

    /// <summary>
    /// Reads the store whole. The selection follows the newest episode when the previously selected one is
    /// gone (a cleared store) or when there was none, and otherwise stays on the same episode id - a new
    /// episode finishing must not pull the reader off the one being examined.
    /// </summary>
    public void Refresh()
    {
        _count = _store.Count;
        _sessionId = _store.SessionId;
        _episodes = new List<EpisodeMetrics>(_store.Episodes);

        if (Selected == null || _store.Get(Selected.Id) == null)
            Selected = _episodes.Count > 0 ? _episodes[_episodes.Count - 1] : null;

        Changed?.Invoke();
    }

    /// <summary>Selects one episode by id. Unknown ids are ignored rather than clearing the view.</summary>
    public void Select(string id)
    {
        EpisodeMetrics episode = _store.Get(id);
        if (episode == null || ReferenceEquals(episode, Selected))
            return;

        Selected = episode;
        Changed?.Invoke();
    }
}
