using System;
using System.Collections.Generic;
using RobotSNAP;
using RobotSNAP.Core;
using RobotSNAP.UI;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Wires the Settings tab: the window mode, the size and refresh rate of the display, the frame rate
/// ceiling, vertical sync, and the quality level.
///
/// The page prepares a display and the reader commits it. Moving a control changes what the page is
/// preparing - <see cref="_pending"/> - and nothing else; <c>Apply</c> is what hands that to
/// <see cref="DisplaySettings"/> and lets the window change under the cursor. The page says what is waiting
/// by naming the settings that differ from the ones in force, and the <c>Current</c> card keeps showing what
/// the application is really running at, so the two are never confused for one another.
///
/// <c>Reset to defaults</c> prepares the display the application shipped with; it is a preparation like any
/// other and is committed by the same button, which is what keeps one rule for the whole page.
///
/// It initializes lazily, the first time the tab is opened, because that is when the UXML template is
/// instantiated into the UIDocument - the same reason the other tab controllers wait for their own view.
/// The stored display is applied earlier, in <c>Awake</c>, so a session nobody configures still runs the way
/// the last one was left.
/// </summary>
public class SettingsTabController : MonoBehaviour
{
    /// <summary>The window modes, in the order the dropdown shows them.</summary>
    private static readonly (string Label, FullScreenMode Mode)[] WindowModes =
    {
        ("Windowed", FullScreenMode.Windowed),
        ("Fullscreen (borderless)", FullScreenMode.FullScreenWindow),
        ("Fullscreen (exclusive)", FullScreenMode.ExclusiveFullScreen),
        ("Maximized window", FullScreenMode.MaximizedWindow)
    };

    /// <summary>Frame rate ceilings offered, with -1 for the unlimited one Unity spells that way.</summary>
    private static readonly (string Label, int FramesPerSecond)[] FrameRates =
    {
        ("No limit", -1),
        ("30 frames per second", 30),
        ("60 frames per second", 60),
        ("120 frames per second", 120),
        ("144 frames per second", 144)
    };

    [SerializeField] private UIDocument uiDocument;

    private bool _initialized;

    /// <summary>The display the page is preparing, which is what <c>Apply</c> commits.</summary>
    private DisplayPreference _pending;

    private DropdownField _windowModeDropdown;
    private DropdownField _resolutionDropdown;
    private DropdownField _frameRateDropdown;
    private DropdownField _qualityDropdown;
    private SlideToggle _vsyncToggle;
    private Button _applyButton;
    private Button _resetButton;

    private Label _applyHint;
    private Label _resolutionDetail;
    private Label _frameRateDetail;
    private Label _editorNote;
    private Label _stateResolution;
    private Label _stateMode;
    private Label _stateFrameRate;
    private Label _stateQuality;

    private TextField _profileNameField;
    private Button _saveProfileButton;
    private Label _profileStatus;
    private Label _profileEmpty;
    private VisualElement _profileListHost;

    /// <summary>The page itself, which is what a confirmation is shown over.</summary>
    private VisualElement _page;

    /// <summary>The resolutions behind the dropdown, in the order the dropdown lists them.</summary>
    private readonly List<ResolutionChoice> _resolutions = new List<ResolutionChoice>();

    /// <summary>
    /// True while the page writes into its own controls. Setting a dropdown's index raises the same event a
    /// click does, so without this gate the page would read back what it had just written as a choice.
    /// </summary>
    private bool _filling;

    private void Awake()
    {
        // Before any view exists: a session that was ended fullscreen opens fullscreen even if the reader
        // never opens this tab.
        DisplaySettings.ApplyStored();
    }

    private void OnEnable()
    {
        MainViewController.OnViewLoaded += OnViewLoaded;
        TryInitialize();
    }

    private void OnDisable()
    {
        MainViewController.OnViewLoaded -= OnViewLoaded;
    }

    private void OnViewLoaded(string viewName)
    {
        if (viewName != "Settings")
            return;

        TryInitialize();

        // Coming back to a page that is already built keeps what it was preparing: a reader who stepped
        // away mid-decision did not decide to abandon it. Only the live figures are refreshed.
        FillLiveLabels();
        UpdateApplyState();
        RefreshProfileList();
    }

    private void TryInitialize()
    {
        if (_initialized)
            return;

        UIDocument document = ResolveDocument();
        if (document == null)
            return;

        VisualElement root = document.rootVisualElement;
        VisualElement page = root?.Q<VisualElement>("SettingsPage");
        if (page == null)
            return; // The tab has not been shown yet, so its template is not in the tree.

        _page = page;

        var missing = new List<string>();
        _windowModeDropdown = Require<DropdownField>(root, "WindowModeDropdown", missing);
        _resolutionDropdown = Require<DropdownField>(root, "ResolutionDropdown", missing);
        _frameRateDropdown = Require<DropdownField>(root, "FrameRateDropdown", missing);
        _qualityDropdown = Require<DropdownField>(root, "QualityDropdown", missing);
        _vsyncToggle = Require<SlideToggle>(root, "VsyncToggle", missing);
        _applyButton = Require<Button>(root, "ApplySettingsButton", missing);
        _resetButton = Require<Button>(root, "ResetDisplayButton", missing);
        _applyHint = Require<Label>(root, "SettingsApplyHint", missing);
        _resolutionDetail = Require<Label>(root, "ResolutionDetail", missing);
        _frameRateDetail = Require<Label>(root, "FrameRateDetail", missing);
        _editorNote = Require<Label>(root, "SettingsEditorNote", missing);
        _stateResolution = Require<Label>(root, "StateResolutionLabel", missing);
        _stateMode = Require<Label>(root, "StateModeLabel", missing);
        _stateFrameRate = Require<Label>(root, "StateFrameRateLabel", missing);
        _stateQuality = Require<Label>(root, "StateQualityLabel", missing);
        _profileNameField = Require<TextField>(root, "ProfileNameField", missing);
        _saveProfileButton = Require<Button>(root, "SaveProfileButton", missing);
        _profileStatus = Require<Label>(root, "ProfileStatusLabel", missing);
        _profileEmpty = Require<Label>(root, "ProfileEmptyLabel", missing);
        _profileListHost = Require<VisualElement>(root, "ProfileListHost", missing);
        if (missing.Count > 0)
        {
            Debug.LogError($"[SettingsTabController] Missing UI elements: {string.Join(", ", missing)}");
            return;
        }

        BuildWindowModeChoices();
        BuildResolutionChoices();
        BuildFrameRateChoices();
        BuildQualityChoices();
        RegisterCallbacks();

        // A requested resolution only shows up in Screen.currentResolution a frame or two later, so the
        // readout is refreshed on a timer rather than once on the click that caused it.
        _stateResolution.schedule.Execute(FillLiveLabels).Every(500);
        if (Application.isEditor)
            _editorNote.RemoveFromClassList("is-hidden");

        _initialized = true;
        AdoptCurrent();
        FillControls();
        UpdateApplyState();
        RefreshProfileList();
    }

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
            if (candidateRoot != null && candidateRoot.Q<VisualElement>("SettingsPage") != null)
            {
                uiDocument = candidate;
                return uiDocument;
            }
        }

        return null;
    }

    private static T Require<T>(VisualElement root, string name, List<string> missing) where T : VisualElement
    {
        T element = root.Q<T>(name);
        if (element == null)
            missing.Add(name);

        return element;
    }

    // -- the answers each field offers ------------------------------------------------------------

    private void BuildWindowModeChoices()
    {
        var labels = new List<string>();
        foreach ((string label, _) in WindowModes)
            labels.Add(label);

        _windowModeDropdown.choices = labels;
    }

    /// <summary>
    /// The common sizes the display actually supports, each at the highest refresh rate it reports for that
    /// size, plus the size the application is running at when that is a window size of its own.
    ///
    /// The editor's window follows the Game view, which is not a size this list has anything to say about, so
    /// the current window is only offered in a built player - where it is a real answer the reader may want
    /// back. A display that reports nothing still leaves the field with the common sizes it can offer rather
    /// than an empty list.
    /// </summary>
    private void BuildResolutionChoices()
    {
        var reported = new List<Resolution>(Screen.resolutions);

        var choices = new List<ResolutionChoice>();
        foreach (Vector2Int size in DisplaySettings.CommonSizes)
        {
            ResolutionChoice best = default;
            bool found = false;
            foreach (Resolution candidate in reported)
            {
                if (candidate.width != size.x || candidate.height != size.y)
                    continue;

                if (!found || candidate.refreshRateRatio.value > best.Refresh.value)
                {
                    best = new ResolutionChoice(candidate.width, candidate.height, candidate.refreshRateRatio);
                    found = true;
                }
            }

            if (found)
                choices.Add(best);
        }

        if (!Application.isEditor && Screen.width > 0 && Screen.height > 0)
        {
            bool alreadyOffered = false;
            foreach (ResolutionChoice choice in choices)
            {
                if (choice.Width == Screen.width && choice.Height == Screen.height)
                {
                    alreadyOffered = true;
                    break;
                }
            }

            if (!alreadyOffered)
            {
                choices.Add(new ResolutionChoice(
                    Screen.width, Screen.height, Screen.currentResolution.refreshRateRatio));
            }
        }

        var labels = new List<string>();
        _resolutions.Clear();
        foreach (ResolutionChoice choice in choices)
        {
            _resolutions.Add(choice);
            labels.Add($"{choice.Width} x {choice.Height}  -  {DisplaySettings.DescribeRefresh(choice.Refresh)}");
        }

        _resolutionDropdown.choices = labels;
    }

    private void BuildFrameRateChoices()
    {
        var labels = new List<string>();
        foreach ((string label, _) in FrameRates)
            labels.Add(label);

        _frameRateDropdown.choices = labels;
    }

    private void BuildQualityChoices()
    {
        _qualityDropdown.choices = new List<string>(QualitySettings.names);
    }

    // -- the page's own state ---------------------------------------------------------------------

    private void RegisterCallbacks()
    {
        _windowModeDropdown.RegisterValueChangedCallback(_ => OnWindowModeChanged());
        _resolutionDropdown.RegisterValueChangedCallback(_ => OnResolutionChanged());
        _frameRateDropdown.RegisterValueChangedCallback(_ => OnFrameRateChanged());
        _qualityDropdown.RegisterValueChangedCallback(_ => OnQualityChanged());
        _vsyncToggle.RegisterValueChangedCallback(evt => OnVsyncChanged(evt.newValue));
        _applyButton.clicked += OnApplyClicked;
        _resetButton.clicked += OnResetClicked;
        _saveProfileButton.clicked += OnSaveProfileClicked;
        _profileNameField.RegisterCallback<KeyDownEvent>(OnProfileNameKeyDown);
    }

    /// <summary>Points the page at the display that is in force, as the starting point of a decision.</summary>
    private void AdoptCurrent() => _pending = DisplaySettings.Current();

    /// <summary>
    /// Writes what the page is preparing into its controls, without letting the writes come back as choices.
    /// </summary>
    private void FillControls()
    {
        if (!_initialized)
            return;

        _filling = true;
        try
        {
            _windowModeDropdown.index = IndexOfMode(_pending.Mode);
            // A page whose reader has not chosen a size yet still has to show something, and what it shows is
            // the offered size closest to the window in front of them. Choosing one is what turns that display
            // into a preference; leaving it alone leaves the resolution out of what Apply commits.
            int width = _pending.HasResolution ? _pending.Width : Screen.width;
            int height = _pending.HasResolution ? _pending.Height : Screen.height;
            RefreshRate refresh = _pending.HasResolution
                ? _pending.Refresh
                : Screen.currentResolution.refreshRateRatio;
            _resolutionDropdown.index = IndexOfResolution(width, height, refresh);
            _frameRateDropdown.index = IndexOfFrameRate(_pending.FramesPerSecond);
            _vsyncToggle.SetValueWithoutNotify(_pending.Vsync);

            if (_qualityDropdown.choices.Count > 0)
                _qualityDropdown.index = Mathf.Clamp(_pending.Quality, 0, _qualityDropdown.choices.Count - 1);
        }
        finally
        {
            _filling = false;
        }
    }

    /// <summary>
    /// The lines that describe the application as it is, not as the page is preparing it: which size is
    /// really in force, and that the ceiling is not what decides the frame rate while vertical sync is on.
    /// </summary>
    private void FillLiveLabels()
    {
        if (!_initialized)
            return;

        Resolution current = Screen.currentResolution;
        _resolutionDetail.text =
            $"Running at {Screen.width} x {Screen.height}, {DisplaySettings.DescribeRefresh(current.refreshRateRatio)}.";

        _frameRateDetail.text = QualitySettings.vSyncCount != 0
            ? "Not in force while vertical sync is on."
            : "Frames per second the application will not exceed.";

        _stateResolution.text =
            $"Display  {Screen.width} x {Screen.height}  -  {DisplaySettings.DescribeRefresh(current.refreshRateRatio)}";
        _stateMode.text = $"Mode  {DescribeMode(Screen.fullScreenMode)}";

        if (QualitySettings.vSyncCount != 0)
            _stateFrameRate.text = "Vertical sync  on";
        else if (Application.targetFrameRate > 0)
            _stateFrameRate.text = $"Frame rate  {Application.targetFrameRate} fps";
        else
            _stateFrameRate.text = "Frame rate  no limit";

        _stateQuality.text = $"Quality  {DescribeQuality(QualitySettings.GetQualityLevel())}";
    }

    /// <summary>
    /// The Apply button carries whether there is anything to apply, and the line below the buttons names what
    /// is waiting. A disabled button that never said why would leave the reader moving a control again to find
    /// out, and a difference the page reports has to be one the reader can point at. The line always holds a
    /// sentence and always keeps its place, so committing a change never moves the buttons above it.
    /// </summary>
    private void UpdateApplyState()
    {
        if (!_initialized)
            return;

        List<string> changed = ChangedSettings();
        _applyButton.SetEnabled(changed.Count > 0);
        _applyHint.text = changed.Count == 0
            ? "Everything on this page is applied."
            : "Unapplied: " + string.Join(", ", changed) + ".";
    }

    /// <summary>Which settings the page is preparing differ from the ones in force.</summary>
    private List<string> ChangedSettings()
    {
        DisplayPreference current = DisplaySettings.Current();
        var changed = new List<string>();

        if (_pending.Mode != current.Mode)
            changed.Add("window mode");
        if (_pending.HasResolution)
        {
            if (_pending.Width != current.Width || _pending.Height != current.Height)
                changed.Add("resolution");
            else if (_pending.Refresh.numerator != current.Refresh.numerator ||
                     _pending.Refresh.denominator != current.Refresh.denominator)
                changed.Add("refresh rate");
        }
        if (_pending.Vsync != current.Vsync)
            changed.Add("vertical sync");
        if (_pending.FramesPerSecond != current.FramesPerSecond)
            changed.Add("frame rate limit");
        if (_pending.Quality != current.Quality)
            changed.Add("quality level");

        return changed;
    }

    // -- choosing ---------------------------------------------------------------------------------

    private void OnWindowModeChanged()
    {
        if (_filling)
            return;

        int index = _windowModeDropdown.index;
        if (index < 0 || index >= WindowModes.Length)
            return;

        _pending = _pending.WithMode(WindowModes[index].Mode);
        UpdateApplyState();
    }

    private void OnResolutionChanged()
    {
        if (_filling)
            return;

        int index = _resolutionDropdown.index;
        if (index < 0 || index >= _resolutions.Count)
            return;

        ResolutionChoice chosen = _resolutions[index];
        _pending = _pending.WithResolution(chosen.Width, chosen.Height, chosen.Refresh);
        UpdateApplyState();
    }

    private void OnFrameRateChanged()
    {
        if (_filling)
            return;

        int index = _frameRateDropdown.index;
        if (index < 0 || index >= FrameRates.Length)
            return;

        _pending = _pending.WithFrameRate(FrameRates[index].FramesPerSecond);
        UpdateApplyState();
    }

    private void OnQualityChanged()
    {
        if (_filling)
            return;

        int index = _qualityDropdown.index;
        if (index < 0 || index >= QualitySettings.names.Length)
            return;

        _pending = _pending.WithQuality(index);
        UpdateApplyState();
    }

    private void OnVsyncChanged(bool on)
    {
        if (_filling)
            return;

        _pending = _pending.WithVsync(on);
        UpdateApplyState();
    }

    private void OnApplyClicked()
    {
        DisplaySettings.Commit(_pending);
        AdoptCurrent();
        FillControls();
        FillLiveLabels();
        UpdateApplyState();
    }

    private void OnResetClicked()
    {
        // Prepared, not committed: the one button of the page is the one that changes the display.
        _pending = DisplaySettings.Shipped();
        FillControls();
        UpdateApplyState();
    }

    // -- saved configurations -----------------------------------------------------------------------

    /// <summary>
    /// The configurations that can be loaded, read through the boundary that owns those files and shown in
    /// the order a reader looks for a name in. The list is rebuilt rather than patched because every action
    /// of the card - save, load, delete - changes it, and it is short.
    /// </summary>
    private void RefreshProfileList()
    {
        if (!_initialized)
            return;

        var names = new List<string>(ConfigPersistence.GetAvailableNames());
        names.Sort(StringComparer.OrdinalIgnoreCase);

        _profileListHost.Clear();
        foreach (string name in names)
            _profileListHost.Add(BuildProfileRow(name));

        _profileEmpty.EnableInClassList("is-hidden", names.Count > 0);
    }

    /// <summary>One saved configuration, with the two things that can be done with it.</summary>
    private VisualElement BuildProfileRow(string name)
    {
        var row = new VisualElement();
        row.AddToClassList("settings-profile-row");

        var label = new Label(name);
        label.AddToClassList("settings-profile-name");
        row.Add(label);

        var load = new Button(() => OnLoadProfileRequested(name)) { text = "Load" };
        load.AddToClassList("settings-button");
        load.AddToClassList("settings-button-small");
        load.tooltip = $"Replace the configuration in force with '{name}'.";
        row.Add(load);

        var remove = new Button(() => OnDeleteProfileRequested(name)) { text = "Delete" };
        remove.AddToClassList("settings-button");
        remove.AddToClassList("settings-button-small");
        remove.AddToClassList("settings-button-danger");
        remove.tooltip = $"Remove '{name}' from the saved configurations.";
        row.Add(remove);

        return row;
    }

    /// <summary>A name typed and confirmed with the keyboard is a save, the way it is anywhere else.</summary>
    private void OnProfileNameKeyDown(KeyDownEvent evt)
    {
        if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter)
            return;

        evt.StopPropagation();
        OnSaveProfileClicked();
    }

    /// <summary>
    /// Writes the configuration in force under the typed name. The name is checked against the same list the
    /// card shows, so a name that would collide with a saved profile, escape the folder, or that a file
    /// system would refuse for its characters is answered here rather than by a failed write.
    /// </summary>
    private void OnSaveProfileClicked()
    {
        Supervisor supervisor = Supervisor.Instance;
        if (supervisor == null || supervisor.ActiveConfig == null)
        {
            SetProfileStatus("The simulation configuration is not available.", error: true);
            return;
        }

        ConfigNameProblem problem = ConfigPersistence.CheckName(
            _profileNameField.value, ConfigPersistence.GetAvailableNames(), out string cleanName);
        if (problem != ConfigNameProblem.None)
        {
            SetProfileStatus(DescribeNameProblem(problem, cleanName), error: true);
            return;
        }

        ConfigPersistence.Save(supervisor.ActiveConfig, cleanName);
        _profileNameField.SetValueWithoutNotify(string.Empty);
        SetProfileStatus($"Saved '{cleanName}'.", error: false);
        RefreshProfileList();
    }

    /// <summary>
    /// Asks before replacing the configuration in force, because a session already running under one
    /// configuration is not the same session under another. It is the modal the rest of the application puts
    /// in front of its irreversible actions.
    /// </summary>
    private void OnLoadProfileRequested(string name)
    {
        ConfirmationDialog dialog = new ConfirmationDialog(
            "Load this configuration?",
            $"Loading '{name}' replaces the configuration the simulation is running under. The display "
            + "settings on this page are not affected.",
            "Load",
            destructive: false);

        dialog.Confirmed += () => LoadProfile(name);
        dialog.Show(_page);
    }

    private void LoadProfile(string name)
    {
        if (!ConfigPersistence.TryLoad(name, out SimulationConfig loaded) || loaded == null)
        {
            SetProfileStatus($"Could not read '{name}'.", error: true);
            return;
        }

        try
        {
            Supervisor supervisor = Supervisor.Instance;
            if (supervisor == null)
            {
                SetProfileStatus("The simulation configuration is not available.", error: true);
                return;
            }

            // What a load produces is a working copy: the supervisor takes its values, and the copy is
            // released here rather than left behind as an asset nobody owns.
            supervisor.UpdateConfig(loaded);
            SetProfileStatus($"Loaded '{name}'.", error: false);
        }
        finally
        {
            Release(loaded);
        }
    }

    /// <summary>
    /// Asks before removing a saved configuration, the way the Analysis tab asks before it removes an
    /// episode. A configuration that ships beside the application is not in the user's folder and is
    /// reported as not removed.
    /// </summary>
    private void OnDeleteProfileRequested(string name)
    {
        ConfirmationDialog dialog = new ConfirmationDialog(
            "Delete this configuration?",
            $"This removes '{name}' from the saved configurations. It cannot be undone.",
            "Delete");

        dialog.Confirmed += () =>
        {
            bool removed = ConfigPersistence.Delete(name);
            SetProfileStatus(
                removed ? $"Deleted '{name}'." : $"Could not delete '{name}'.",
                error: !removed);
            RefreshProfileList();
        };
        dialog.Show(_page);
    }

    /// <summary>Writes what the last action of the card left behind, tinted when it is a failure.</summary>
    private void SetProfileStatus(string message, bool error)
    {
        _profileStatus.text = message ?? string.Empty;
        _profileStatus.EnableInClassList("is-error", error);
    }

    /// <summary>The reason a typed name cannot be used, in the words the card shows.</summary>
    private static string DescribeNameProblem(ConfigNameProblem problem, string cleanName)
    {
        switch (problem)
        {
            case ConfigNameProblem.Empty:
                return "Enter a name for the configuration.";
            case ConfigNameProblem.TooLong:
                return $"A name may not be longer than {ConfigPersistence.MaxNameLength} characters.";
            case ConfigNameProblem.InvalidCharacter:
                return "A name cannot contain \\ / : * ? \" < > | or a control character.";
            case ConfigNameProblem.Reserved:
                return "A name cannot be \".\" or \"..\".";
            case ConfigNameProblem.Taken:
                return $"A configuration named '{cleanName}' already exists. Delete it first, or pick "
                       + "another name.";
            default:
                return string.Empty;
        }
    }

    /// <summary>Releases the working copy a load produced, with the call each mode takes.</summary>
    private static void Release(ScriptableObject instance)
    {
        if (instance == null)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            DestroyImmediate(instance);
            return;
        }
#endif
        Destroy(instance);
    }

    // -- writing a value the way the page reads it -------------------------------------------------

    private static int IndexOfMode(FullScreenMode mode)
    {
        for (int index = 0; index < WindowModes.Length; index++)
        {
            if (WindowModes[index].Mode == mode)
                return index;
        }

        // A mode this page does not offer is shown as windowed rather than as nothing, because a dropdown
        // with no selection reads as a field that broke.
        return 0;
    }

    private static string DescribeMode(FullScreenMode mode)
    {
        foreach ((string label, FullScreenMode candidate) in WindowModes)
        {
            if (candidate == mode)
                return label;
        }

        // The editor reports an undefined mode - and a "full screen" that is true whichever way the Game
        // view is docked - so neither the enum nor the boolean can be trusted there. What is left is the
        // truth a person can see: in the editor the application runs in the Game view, which is a window.
        // The raw enum would otherwise read as "-1" on the page.
        return Application.isEditor ? "Windowed (Game view)" : "Unknown";
    }

    /// <summary>
    /// The entry closest to a size and rate. The exact rate is preferred, and when the display does not
    /// report it - which is the case right after the application asked for a size it had not been in - the
    /// closest size answers instead, so the field keeps naming something the reader asked for.
    /// </summary>
    private int IndexOfResolution(int width, int height, RefreshRate refresh)
    {
        int sameSizeFallback = -1;
        int closest = -1;
        long closestGap = long.MaxValue;
        long wanted = (long)Mathf.Max(0, width) * Mathf.Max(0, height);

        for (int index = 0; index < _resolutions.Count; index++)
        {
            ResolutionChoice candidate = _resolutions[index];
            if (candidate.Width == width && candidate.Height == height)
            {
                if (sameSizeFallback < 0)
                    sameSizeFallback = index;

                if (candidate.Refresh.numerator == refresh.numerator &&
                    candidate.Refresh.denominator == refresh.denominator)
                    return index;
            }

            long gap = Math.Abs((long)candidate.Width * candidate.Height - wanted);
            if (gap < closestGap)
            {
                closestGap = gap;
                closest = index;
            }
        }

        if (sameSizeFallback >= 0)
            return sameSizeFallback;

        return closest >= 0 ? closest : 0;
    }

    private static int IndexOfFrameRate(int framesPerSecond)
    {
        for (int index = 0; index < FrameRates.Length; index++)
        {
            if (FrameRates[index].FramesPerSecond == framesPerSecond)
                return index;
        }

        // A ceiling set outside this page is not one of the offered answers; "No limit" is the honest
        // placeholder for it, and moving the field is what replaces it.
        return 0;
    }

    private static string DescribeQuality(int level)
    {
        string[] names = QualitySettings.names;
        return level >= 0 && level < names.Length ? names[level] : $"level {level}";
    }

    /// <summary>One size and rate the dropdown offers, kept beside the label it is shown under.</summary>
    private readonly struct ResolutionChoice
    {
        public ResolutionChoice(int width, int height, RefreshRate refresh)
        {
            Width = width;
            Height = height;
            Refresh = refresh;
        }

        public int Width { get; }
        public int Height { get; }
        public RefreshRate Refresh { get; }
    }
}
