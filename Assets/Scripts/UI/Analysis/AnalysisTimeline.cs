using System;
using System.Globalization;
using UnityEngine.UIElements;

/// <summary>
/// The transport strip of one replayed episode: back to the start, play or pause, one second back, one second
/// forward, a frise that spans the run, and the instant it stands on in clear text.
///
/// It owns no cursor. Each control reports what a reader asked for and the tab moves the shared
/// <see cref="AnalysisEpisodeReplay"/>; this redraws from that replay whenever it changes, which is what keeps
/// a drag of the frise and a step of the button in agreement. The same reason is why the slider is written
/// with <c>SetValueWithoutNotify</c>: moving it to follow playback must not read back as a reader dragging
/// it, or the two would chase each other one frame behind.
///
/// The strip is only ever shown for a single selected episode - the overview of several has no one run to
/// rewind - so it renders nothing but its own dials when the tab hands it no episode.
/// </summary>
public sealed class AnalysisTimeline : VisualElement, IDisposable
{
    /// <summary>Raised with the instant the reader scrubbed the frise to, in world seconds.</summary>
    public event Action<double> SeekRequested;

    /// <summary>Raised when the reader asked for one more second.</summary>
    public event Action StepRequested;

    /// <summary>Raised when the reader asked to go one second back.</summary>
    public event Action StepBackRequested;

    /// <summary>Raised when the reader asked to go back to the beginning of the episode.</summary>
    public event Action RewindRequested;

    /// <summary>Raised when the reader asked to play or to stop; the replay decides which one it means.</summary>
    public event Action TogglePlayRequested;

    private readonly Button _rewind;
    private readonly Button _play;
    private readonly Button _stepBack;
    private readonly Button _step;
    private readonly Slider _slider;
    private readonly Label _clock;

    private AnalysisEpisodeReplay _replay;
    private bool _syncing;

    public AnalysisTimeline()
    {
        AddToClassList("analysis-timeline");

        _rewind = Transport("analysis-timeline-rewind", "Back to the beginning of the episode",
            () => RewindRequested?.Invoke());
        _play = Transport("analysis-timeline-play", "Play or stop the episode",
            () => TogglePlayRequested?.Invoke());
        _stepBack = Transport("analysis-timeline-step-back", "Go back one second",
            () => StepBackRequested?.Invoke());
        _stepBack.text = "-1 s";
        _step = Transport("analysis-timeline-step", "Advance one second", () => StepRequested?.Invoke());
        _step.text = "+1 s";

        _slider = new Slider(0f, 1f) { showInputField = false };
        _slider.AddToClassList("analysis-timeline-slider");
        _slider.tooltip = "Scrub the episode from its start to its end";
        _slider.RegisterValueChangedCallback(OnSliderChanged);
        Add(_slider);

        _clock = new Label();
        _clock.AddToClassList("analysis-timeline-clock");
        Add(_clock);

        Refresh();
    }

    /// <summary>Follows one cursor, from now on and immediately.</summary>
    public void Bind(AnalysisEpisodeReplay replay)
    {
        if (_replay != null)
            _replay.Changed -= Refresh;

        _replay = replay;
        if (_replay != null)
            _replay.Changed += Refresh;

        Refresh();
    }

    /// <summary>Detaches from the cursor; a disposed strip never draws again.</summary>
    public void Dispose()
    {
        if (_replay != null)
            _replay.Changed -= Refresh;
        _replay = null;
    }

    /// <summary>
    /// An instant as <c>mm:ss</c>, which is how a reader reads a clip-relative time. Seconds are truncated
    /// rather than rounded, so the clock never shows the end of the episode while the cursor is still a
    /// fraction of a second short of it.
    /// </summary>
    public static string Clock(double seconds)
    {
        int whole = (int)Math.Floor(Math.Max(0.0, seconds) + 1e-6);
        return (whole / 60).ToString("D2", CultureInfo.InvariantCulture) + ":" +
               (whole % 60).ToString("D2", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Asks for one instant, which is what moving the dragger does. It is separate from the slider's callback
    /// so the strip can be driven without a live panel to dispatch events through - the callback is a call to
    /// this - and so a caller that already knows the instant does not have to pretend to drag.
    /// </summary>
    public void ScrubTo(double seconds) => SeekRequested?.Invoke(seconds);

    private void OnSliderChanged(ChangeEvent<float> evt)
    {
        if (_syncing)
            return;

        ScrubTo(evt.newValue);
    }

    private Button Transport(string className, string tooltip, Action clicked)
    {
        var button = new Button(clicked) { text = string.Empty };
        button.AddToClassList("analysis-timeline-button");
        button.AddToClassList(className);
        button.tooltip = tooltip;
        Add(button);
        return button;
    }

    private void Refresh()
    {
        bool has = _replay != null && _replay.HasEpisode && _replay.Duration > 0.0;

        _rewind.SetEnabled(has);
        _play.SetEnabled(has);
        _stepBack.SetEnabled(has);
        _step.SetEnabled(has);
        _slider.SetEnabled(has);

        _syncing = true;
        try
        {
            _slider.lowValue = 0f;
            _slider.highValue = has ? (float)_replay.Duration : 1f;
            _slider.SetValueWithoutNotify(has ? (float)_replay.Time : 0f);
        }
        finally
        {
            _syncing = false;
        }

        _play.EnableInClassList("is-replaying", has && _replay.IsPlaying);
        _clock.text = has
            ? Clock(_replay.Time) + " / " + Clock(_replay.Duration)
            : string.Empty;
    }
}
