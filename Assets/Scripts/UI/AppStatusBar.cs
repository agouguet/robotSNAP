using System;
using RobotSNAP.Agents;
using RobotSNAP.Core;
using RobotSNAP.Core.Scenario;
using RobotSNAP.ROS;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The application bar along the bottom of the window: what the simulation is doing and for how
/// long, how many agents are around, whether the ROS bridge is up, and the application menu.
///
/// The bar reads the simulation, it never drives it: the state comes from the event bus, the agent
/// figures are counted from the scene, and the clock only advances while the simulation runs.
/// </summary>
public sealed class AppStatusBar : IDisposable
{
    private const float AgentCountRefresh = 0.5f;
    private const string AgentsIconPath = "Icons/agents";

    private readonly Label _messageLabel;
    private readonly Label _timeLabel;
    private readonly Label _agentsLabel;
    private readonly VisualElement _rosDot;
    private readonly Button _menuButton;
    private readonly VisualElement _menu;
    private readonly Button _quitButton;

    private readonly ScenarioManager _scenarioManager;
    private EnvROS _envRos;

    private SimulationState _state = SimulationState.Idle;
    private string _scenarioName;
    private float _elapsedSeconds;
    private float _nextAgentCount;
    private int _robotCount;
    private int _humanCount;

    public AppStatusBar(VisualElement root)
    {
        if (root == null)
        {
            Debug.LogWarning("[AppStatusBar] Root element is null; the application bar stays inert.");
            return;
        }

        // The simulation view hosts a status bar of its own that reuses the same element names, so
        // the queries are scoped to the application bar. The menu button is the anchor: it is the
        // one piece of this bar the simulation view does not carry.
        VisualElement bar = root.Q<Button>("AppMenuButton")?.parent;
        if (bar == null)
        {
            Debug.LogWarning("[AppStatusBar] The application bar is missing; it stays inert.");
            return;
        }

        _messageLabel = Query<Label>(bar, "StatusMessage");
        _timeLabel = Query<Label>(bar, "StatusTime");
        _agentsLabel = Query<Label>(bar, "StatusAgentsLabel");
        VisualElement agentsIcon = Query<VisualElement>(bar, "StatusAgentsIcon");
        _rosDot = Query<VisualElement>(bar, "StatusRosDot");
        _menuButton = Query<Button>(bar, "AppMenuButton");
        _menu = Query<VisualElement>(bar, "AppMenu");
        _quitButton = Query<Button>(bar, "QuitAppButton");

        if (_messageLabel == null || _timeLabel == null || _agentsLabel == null ||
            _menuButton == null || _menu == null || _quitButton == null)
        {
            Debug.LogWarning("[AppStatusBar] The application bar is incomplete; it stays inert.");
            return;
        }

        ApplyIcon(agentsIcon, AgentsIconPath);

        _menuButton.clicked += ToggleMenu;
        _quitButton.clicked += Quit;

        // The application menu starts closed. The class writes the same state in the inline display
        // and in the markup class, so the two never disagree.
        SetPopupDisplay(_menu, false);

        _scenarioManager = UnityEngine.Object.FindAnyObjectByType<ScenarioManager>();
        if (_scenarioManager != null)
        {
            _scenarioManager.OnScenarioApplied += OnScenarioApplied;
            if (_scenarioManager.CurrentScenarioData != null)
                OnScenarioApplied(_scenarioManager.CurrentScenarioData);
        }

        // The ROS bridge comes with the environment, which can be built after this bar appears, so a
        // missing reference is looked up again on the slow refresh instead of being cached for good.
        _envRos = UnityEngine.Object.FindAnyObjectByType<EnvROS>();
        EventBus.Instance.Subscribe<SimulationStateChangedEvent>(OnStateChanged);

        CountAgents();
        ApplyMessage();
        ApplyTime();
        ApplyRosState();
    }

    public void Dispose()
    {
        EventBus.Instance.Unsubscribe<SimulationStateChangedEvent>(OnStateChanged);

        if (_scenarioManager != null)
            _scenarioManager.OnScenarioApplied -= OnScenarioApplied;

        if (_menuButton != null)
            _menuButton.clicked -= ToggleMenu;

        if (_quitButton != null)
            _quitButton.clicked -= Quit;
    }

    /// <summary>Called every frame: it advances the mission clock and refreshes the slow figures.</summary>
    public void Tick()
    {
        if (_state == SimulationState.Running)
            _elapsedSeconds += Time.unscaledDeltaTime;

        ApplyTime();

        if (Time.unscaledTime >= _nextAgentCount)
        {
            _nextAgentCount = Time.unscaledTime + AgentCountRefresh;
            _envRos ??= UnityEngine.Object.FindAnyObjectByType<EnvROS>();
            CountAgents();
        }

        ApplyRosState();
    }

    /// <summary>
    /// Restarts the mission clock. Applying a scenario asks for it through this method, so no other
    /// path of this class ever takes the time back to zero.
    /// </summary>
    public void ResetClock()
    {
        _elapsedSeconds = 0f;
        ApplyTime();
    }

    private void ToggleMenu() => SetPopupDisplay(_menu, !IsOpen(_menu));

    private void Quit()
    {
        Debug.Log("[AppStatusBar] Quit requested.");
        SetPopupDisplay(_menu, false);

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnScenarioApplied(ScenarioData scenario)
    {
        _scenarioName = scenario?.Name;
        ResetClock();
        ApplyMessage();
    }

    private void OnStateChanged(SimulationStateChangedEvent evt)
    {
        _state = evt.NewState;
        ApplyMessage();
    }

    private void ApplyMessage()
    {
        if (_messageLabel == null) return;

        string state = StateText(_state);
        _messageLabel.text = string.IsNullOrEmpty(_scenarioName) ? state : $"{_scenarioName} — {state}";
    }

    private void ApplyTime()
    {
        if (_timeLabel != null)
            _timeLabel.text = $"Time: {FormatElapsed(_elapsedSeconds)}";
    }

    private void ApplyRosState()
    {
        if (_rosDot == null) return;

        _rosDot.EnableInClassList("is-online", _envRos != null && _envRos.IsInitialized);
    }

    private void CountAgents()
    {
        _robotCount = UnityEngine.Object.FindObjectsByType<Robot>().Length;
        _humanCount = UnityEngine.Object.FindObjectsByType<HumanAgent>().Length;

        if (_agentsLabel == null) return;

        int total = _robotCount + _humanCount;
        string text = $"{total} {Plural("agent", total)}";

        // The breakdown goes in the tooltip: the bar is narrow, and the chip only has to answer
        // "how many" at a glance.
        string breakdown = $"{_robotCount} {Plural("robot", _robotCount)}, {_humanCount} {Plural("human", _humanCount)}";
        _agentsLabel.tooltip = breakdown;

        _agentsLabel.text = text;
    }

    private static string StateText(SimulationState state) => state switch
    {
        SimulationState.Ready => "Ready",
        SimulationState.Running => "Simulation running",
        SimulationState.Paused => "Paused",
        _ => "Idle"
    };

    private static string Plural(string noun, int count) => count == 1 ? noun : noun + "s";

    private static bool IsOpen(VisualElement popup) => popup.style.display == DisplayStyle.Flex;

    private static void SetPopupDisplay(VisualElement popup, bool open)
    {
        if (popup == null) return;

        popup.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
        popup.EnableInClassList("is-hidden", !open);
    }

    private static void ApplyIcon(VisualElement element, string resourcePath)
    {
        if (element == null) return;

        Texture2D texture = Resources.Load<Texture2D>(resourcePath);
        if (texture != null)
            element.style.backgroundImage = new StyleBackground(texture);
    }

    private static string FormatElapsed(float seconds)
    {
        var time = TimeSpan.FromSeconds(Mathf.Max(0f, seconds));

        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    private static T Query<T>(VisualElement root, string name) where T : VisualElement
    {
        T element = root.Q<T>(name);
        if (element == null)
            Debug.LogWarning($"[AppStatusBar] Element '{name}' ({typeof(T).Name}) not found in the application bar; that part of the bar stays inert.");
        return element;
    }
}
