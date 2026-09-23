using System;
using System.Collections.Generic;
using RobotSNAP.UI;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The visualization switches of the shot: a translucent panel folded into the top right corner of the
/// camera view, which drives one setting of <see cref="VisualizationManager"/> per switch.
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

    /// <summary>One switch: the toggle in the overlay, and the manager field it writes.</summary>
    private readonly struct Switch
    {
        public Switch(Toggle toggle, Func<VisualizationManager, bool> read, Action<VisualizationManager, bool> write)
        {
            Toggle = toggle;
            Read = read;
            Write = write;
        }

        public Toggle Toggle { get; }
        public Func<VisualizationManager, bool> Read { get; }
        public Action<VisualizationManager, bool> Write { get; }
    }

    /// <summary>One line of the table the panel is built from: the name of a toggle, and its setting.</summary>
    private readonly struct SwitchDefinition
    {
        public SwitchDefinition(
            string toggleName,
            Func<VisualizationManager, bool> read,
            Action<VisualizationManager, bool> write)
        {
            ToggleName = toggleName;
            Read = read;
            Write = write;
        }

        public string ToggleName { get; }
        public Func<VisualizationManager, bool> Read { get; }
        public Action<VisualizationManager, bool> Write { get; }
    }

    /// <summary>
    /// Every switch of the panel, in the order the overlay shows them. Written once so the panel, the overlay
    /// and the tests cannot drift apart: a switch nobody drives is a line that does nothing, and a setting
    /// with no switch is one nobody can reach.
    /// </summary>
    private static readonly SwitchDefinition[] Definitions =
    {
        // Robot
        new SwitchDefinition("VizRobotTrajectory", manager => manager.showRobotTrajectory, (manager, on) => manager.showRobotTrajectory = on),
        new SwitchDefinition("VizRobotPath", manager => manager.showRobotPath, (manager, on) => manager.showRobotPath = on),
        new SwitchDefinition("VizRobotGoal", manager => manager.showRobotGoal, (manager, on) => manager.showRobotGoal = on),
        new SwitchDefinition("VizRobotVelocity", manager => manager.showRobotVelocityVector, (manager, on) => manager.showRobotVelocityVector = on),
        new SwitchDefinition("VizRobotLidar", manager => manager.showRobotSensorRays, (manager, on) => manager.showRobotSensorRays = on),

        // Crowd
        new SwitchDefinition("VizHumanTrajectories", manager => manager.showHumanTrajectories, (manager, on) => manager.showHumanTrajectories = on),
        new SwitchDefinition("VizHumanGoals", manager => manager.showHumanGoals, (manager, on) => manager.showHumanGoals = on),
        new SwitchDefinition("VizHumanVelocity", manager => manager.showHumanVelocityVectors, (manager, on) => manager.showHumanVelocityVectors = on),
        new SwitchDefinition("VizHumanInteraction", manager => manager.showHumanInteractionRadius, (manager, on) => manager.showHumanInteractionRadius = on),
        new SwitchDefinition("VizHumanColors", manager => manager.colorHumansByState, (manager, on) => manager.colorHumansByState = on),

        // Scene
        new SwitchDefinition("VizGrid", manager => manager.showGrid, (manager, on) => manager.showGrid = on),
        new SwitchDefinition("VizAxes", manager => manager.showAxes, (manager, on) => manager.showAxes = on),
        new SwitchDefinition("VizWireframe", manager => manager.wireframeMode, (manager, on) => manager.wireframeMode = on),
        new SwitchDefinition("VizAgentLabels", manager => manager.showAgentIDs, (manager, on) => manager.showAgentIDs = on),
        new SwitchDefinition("VizDistances", manager => manager.showDistanceLabels, (manager, on) => manager.showDistanceLabels = on)
    };

    /// <summary>Names of the switches, for whoever holds the overlay and this panel together.</summary>
    public static IReadOnlyList<string> SwitchNames { get; } =
        Array.ConvertAll(Definitions, definition => definition.ToggleName);

    private readonly List<Switch> _switches = new();
    private readonly VisualElement _panel;
    private readonly Button _collapseButton;
    private VisualizationManager _manager;
    private bool _collapsed;
    private bool _updating;
    private float _nextRefresh;

    /// <summary>
    /// Binds the switches of the overlay to a manager, creating one when the scene carries none. A missing
    /// overlay element turns the panel inert rather than throwing, because the overlay is rebuilt on its own.
    /// </summary>
    public SimulationVisualizationPanel(VisualElement root)
    {
        if (root == null)
        {
            Debug.LogWarning("[SimulationVisualizationPanel] Overlay root is null; the panel stays inert.");
            return;
        }

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
        SyncFromManager();
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
    public void Refresh() => SyncFromManager();

    /// <summary>The switches, in the order the panel shows them.</summary>
    private void BuildSwitches(VisualElement root)
    {
        for (int index = 0; index < Definitions.Length; index++)
            Register(root, Definitions[index]);
    }

    private void Register(VisualElement root, SwitchDefinition definition)
    {
        Toggle toggle = Query<Toggle>(root, definition.ToggleName);
        if (toggle == null)
            return;

        _switches.Add(new Switch(toggle, definition.Read, definition.Write));

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

        for (int index = 0; index < Definitions.Length; index++)
        {
            if (Definitions[index].ToggleName != toggleName)
                continue;

            Definitions[index].Write(_manager, value);
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

    /// <summary>Writes every setting into its switch, without waking the handlers that write them back.</summary>
    private void SyncFromManager()
    {
        if (_manager == null)
            return;

        _updating = true;
        try
        {
            for (int index = 0; index < _switches.Count; index++)
            {
                Switch entry = _switches[index];
                entry.Toggle.SetValueWithoutNotify(entry.Read(_manager));
            }
        }
        finally
        {
            _updating = false;
        }

        _manager.enabled = AnySwitchOn();
    }

    private bool AnySwitchOn()
    {
        for (int index = 0; index < _switches.Count; index++)
        {
            if (_switches[index].Read(_manager))
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
