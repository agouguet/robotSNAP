using System;
using System.Collections.Generic;
using RobotSNAP.UI;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The visualization switches of the shot: a translucent panel folded into the top right corner of the
/// camera view, which drives one setting per switch - a field of <see cref="VisualizationManager"/>, or the
/// footprints of the minimap, which the minimap draws itself.
///
/// The manager is the one that draws, and this panel is the only thing that asks it to: it is created on
/// first use and enabled only while at least one switch is on, so a run nobody is debugging pays nothing
/// for a component that would otherwise walk the crowd every frame to keep a history nobody reads.
///
/// The switches are written from a table rather than one handler per toggle, so the panel and the manager
/// cannot drift apart: a switch with no setting behind it is a line that does nothing, and a setting with
/// no switch is a line nobody can reach.
/// </summary>
public sealed class SimulationVisualizationPanel
{
    /// <summary>How often the switches are re-read from the manager, in seconds.</summary>
    private const float RefreshInterval = 0.25f;

    private const string ExpandedGlyph = "▾";
    private const string FoldedGlyph = "▸";

    /// <summary>
    /// One switch: the toggle in the overlay, and the setting behind it. Most settings belong to the manager,
    /// and the footprints of the minimap belong to the minimap itself, which is why the switch holds the two
    /// closures rather than a manager field.
    /// </summary>
    private readonly struct Switch
    {
        public Switch(Toggle toggle, SwitchDefinition definition)
        {
            Toggle = toggle;
            Read = definition.Read;
            Write = definition.Write;
            DrivesManager = definition.DrivesManager;
        }

        public Toggle Toggle { get; }
        public Func<bool> Read { get; }
        public Action<bool> Write { get; }

        /// <summary>
        /// True when this switch is a reason for the manager to be awake. A footprint of the minimap is drawn
        /// by the minimap, so asking for one must not wake a component that would then walk the whole crowd
        /// every quarter second to fill a history nobody reads.
        /// </summary>
        public bool DrivesManager { get; }
    }

    /// <summary>One line of the table the panel is built from: the name of a toggle, and its setting.</summary>
    private readonly struct SwitchDefinition
    {
        public SwitchDefinition(string toggleName, Func<bool> read, Action<bool> write, bool drivesManager = true)
        {
            ToggleName = toggleName;
            Read = read;
            Write = write;
            DrivesManager = drivesManager;
        }

        public string ToggleName { get; }
        public Func<bool> Read { get; }
        public Action<bool> Write { get; }
        public bool DrivesManager { get; }
    }

    /// <summary>
    /// Every switch of the panel, in the order the overlay shows them. Written once so the panel, the overlay
    /// and the tests cannot drift apart: a switch nobody drives is a line that does nothing, and a setting
    /// with no switch is one nobody can reach.
    /// </summary>
    private static readonly string[] DefinitionNames =
    {
        // Robot
        "VizRobotTrajectory",
        "VizRobotPath",
        "VizRobotGoal",
        "VizRobotVelocity",
        "VizRobotLidar",

        // Crowd
        "VizHumanTrajectories",
        "VizHumanGoals",
        "VizHumanVelocity",
        "VizHumanInteraction",
        "VizHumanColors",

        // Scene
        "VizGrid",
        "VizAxes",
        "VizWireframe",
        "VizAgentLabels",
        "VizDistances",

        // Minimap
        "VizDetectionFootprints"
    };

    /// <summary>Names of the switches, for whoever holds the overlay and this panel together.</summary>
    public static IReadOnlyList<string> SwitchNames { get; } = DefinitionNames;

    private readonly List<Switch> _switches = new();
    private readonly VisualElement _panel;
    private readonly Button _collapseButton;
    private readonly SimulationMinimap _minimap;
    private VisualizationManager _manager;
    private bool _collapsed;
    private bool _updating;
    private float _nextRefresh;

    /// <summary>
    /// Binds the switches of the overlay to a manager, creating one when the scene carries none. A missing
    /// overlay element turns the panel inert rather than throwing, because the overlay is rebuilt on its own.
    ///
    /// The minimap is optional for the same reason: an environment whose view has no camera has no minimap
    /// either, and its footprint switch is then the one switch the panel leaves inert.
    /// </summary>
    public SimulationVisualizationPanel(VisualElement root, SimulationMinimap minimap = null)
    {
        if (root == null)
        {
            Debug.LogWarning("[SimulationVisualizationPanel] Overlay root is null; the panel stays inert.");
            return;
        }

        _minimap = minimap;
        _panel = Query<VisualElement>(root, "VisualizationPanel");
        _collapseButton = Query<Button>(root, "VisualizationPanelCollapseButton");

        if (_panel == null)
            return;

        _manager = ResolveManager();
        if (_manager == null)
        {
            Debug.LogWarning("[SimulationVisualizationPanel] No VisualizationManager; the panel stays inert.");
            return;
        }

        if (_collapseButton != null)
        {
            _collapseButton.text = ExpandedGlyph;
            _collapseButton.clicked += ToggleCollapsed;
        }

        BuildSwitches(root);
        SyncFromSettings();
    }

    /// <summary>Re-reads the settings on a slow tick, so the panel shows what the scene is doing.</summary>
    public void Tick()
    {
        if (_switches.Count == 0)
            return;

        if (Time.unscaledTime < _nextRefresh)
            return;

        _nextRefresh = Time.unscaledTime + RefreshInterval;
        Refresh();
    }

    /// <summary>
    /// Re-reads every setting into its switch, for whoever knows the scene changed without waiting for the
    /// next tick - and for a test, which does not get the ticks of a running editor.
    /// </summary>
    public void Refresh() => SyncFromSettings();

    /// <summary>The switches, in the order the panel shows them.</summary>
    private void BuildSwitches(VisualElement root)
    {
        for (int index = 0; index < DefinitionNames.Length; index++)
            Register(root, DefinitionFor(DefinitionNames[index], _manager, _minimap));
    }

    private void Register(VisualElement root, SwitchDefinition definition)
    {
        Toggle toggle = Query<Toggle>(root, definition.ToggleName);
        if (toggle == null)
            return;

        _switches.Add(new Switch(toggle, definition));

        toggle.RegisterValueChangedCallback(evt =>
        {
            if (_manager == null || _updating)
                return;

            ApplySwitch(definition.ToggleName, evt.newValue);
        });
    }

    /// <summary>
    /// Writes one switch into the manager, named the way the overlay names it, and starts or stops the manager
    /// accordingly. This is what a click on the toggle runs; it is public because a toggle that is not on
    /// screen does not dispatch its change event, and the pair switch-setting is worth holding under test.
    /// </summary>
    public bool ApplySwitch(string toggleName, bool value)
    {
        if (_manager == null || string.IsNullOrEmpty(toggleName))
            return false;

        for (int index = 0; index < _switches.Count; index++)
        {
            if (_switches[index].Toggle.name != toggleName)
                continue;

            _switches[index].Write(value);
            // A manager nobody asks anything of should not be walking the crowd: it runs while a switch is on.
            _manager.enabled = AnySwitchOn();
            return true;
        }

        return false;
    }

    private void ToggleCollapsed()
    {
        _collapsed = !_collapsed;
        _panel?.EnableInClassList("is-collapsed", _collapsed);

        if (_collapseButton != null)
            _collapseButton.text = _collapsed ? FoldedGlyph : ExpandedGlyph;
    }

    /// <summary>
    /// The setting behind one switch. Spelled out rather than reflected over, so renaming a field of the
    /// manager breaks the build instead of quietly leaving a switch that does nothing.
    /// </summary>
    private static SwitchDefinition DefinitionFor(string toggleName, VisualizationManager manager, SimulationMinimap minimap)
    {
        switch (toggleName)
        {
            // Robot
            case "VizRobotTrajectory":
                return new SwitchDefinition(toggleName, () => manager.showRobotTrajectory, on => manager.showRobotTrajectory = on);
            case "VizRobotPath":
                return new SwitchDefinition(toggleName, () => manager.showRobotPath, on => manager.showRobotPath = on);
            case "VizRobotGoal":
                return new SwitchDefinition(toggleName, () => manager.showRobotGoal, on => manager.showRobotGoal = on);
            case "VizRobotVelocity":
                return new SwitchDefinition(toggleName, () => manager.showRobotVelocityVector, on => manager.showRobotVelocityVector = on);
            case "VizRobotLidar":
                return new SwitchDefinition(toggleName, () => manager.showRobotSensorRays, on => manager.showRobotSensorRays = on);

            // Crowd
            case "VizHumanTrajectories":
                return new SwitchDefinition(toggleName, () => manager.showHumanTrajectories, on => manager.showHumanTrajectories = on);
            case "VizHumanGoals":
                return new SwitchDefinition(toggleName, () => manager.showHumanGoals, on => manager.showHumanGoals = on);
            case "VizHumanVelocity":
                return new SwitchDefinition(toggleName, () => manager.showHumanVelocityVectors, on => manager.showHumanVelocityVectors = on);
            case "VizHumanInteraction":
                return new SwitchDefinition(toggleName, () => manager.showHumanInteractionRadius, on => manager.showHumanInteractionRadius = on);
            case "VizHumanColors":
                return new SwitchDefinition(toggleName, () => manager.colorHumansByState, on => manager.colorHumansByState = on);

            // Scene
            case "VizGrid":
                return new SwitchDefinition(toggleName, () => manager.showGrid, on => manager.showGrid = on);
            case "VizAxes":
                return new SwitchDefinition(toggleName, () => manager.showAxes, on => manager.showAxes = on);
            case "VizWireframe":
                return new SwitchDefinition(toggleName, () => manager.wireframeMode, on => manager.wireframeMode = on);
            case "VizAgentLabels":
                return new SwitchDefinition(toggleName, () => manager.showAgentIDs, on => manager.showAgentIDs = on);
            case "VizDistances":
                return new SwitchDefinition(toggleName, () => manager.showDistanceLabels, on => manager.showDistanceLabels = on);

            // Minimap. Null only where the overlay has no view at all, and the switch is inert there.
            case "VizDetectionFootprints":
                return new SwitchDefinition(
                    toggleName,
                    () => minimap != null && minimap.ShowDetectionFootprints,
                    on => { if (minimap != null) minimap.ShowDetectionFootprints = on; },
                    drivesManager: false);

            default:
                return new SwitchDefinition(toggleName, () => false, _ => { }, drivesManager: false);
        }
    }

    /// <summary>Writes every setting into its switch, without waking the handlers that write them back.</summary>
    private void SyncFromSettings()
    {
        _updating = true;
        try
        {
            for (int index = 0; index < _switches.Count; index++)
            {
                Switch entry = _switches[index];
                entry.Toggle.SetValueWithoutNotify(entry.Read());
            }
        }
        finally
        {
            _updating = false;
        }

        if (_manager != null)
            _manager.enabled = AnySwitchOn();
    }

    /// <summary>True while any switch that only the manager can honour is asking for something.</summary>
    private bool AnySwitchOn()
    {
        for (int index = 0; index < _switches.Count; index++)
        {
            if (_switches[index].DrivesManager && _switches[index].Read())
                return true;
        }

        return false;
    }

    /// <summary>
    /// The manager of this scene, or a fresh one. Nothing places it by hand: the panel is the only caller, and
    /// a scene that carries one - a hand-built environment, a testbed - keeps the settings it was authored with.
    /// </summary>
    private static VisualizationManager ResolveManager()
    {
        VisualizationManager existing = UnityEngine.Object.FindAnyObjectByType<VisualizationManager>();
        if (existing != null)
            return existing;

        var host = new GameObject("Visualization");
        return host.AddComponent<VisualizationManager>();
    }

    private static T Query<T>(VisualElement root, string name) where T : VisualElement
    {
        T element = root.Q<T>(name);
        if (element == null)
            Debug.LogWarning($"[SimulationVisualizationPanel] Element '{name}' ({typeof(T).Name}) not found in the simulation overlay; that switch stays inert.");
        return element;
    }
}
