using System;
using RobotSNAP;
using RobotSNAP.Agents;
using RobotSNAP.Core;
using RobotSNAP.Core.Scenario;
using RobotSNAP.ROS;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The strip along the bottom of the simulation view: which scenario runs, for how long, with how
/// many agents, in which state — and whether the ROS side is up.
/// </summary>
public sealed class SimulationStatusBar : IDisposable
{
    private const float AgentCountRefresh = 0.5f;
    private const string ScenarioIconPath = "Icons/folder";
    private const string TimeIconPath = "Icons/clock";
    private const string AgentsIconPath = "Icons/agents";
    private const string RosIconPath = "Icons/path";

    private readonly Label _scenarioLabel;
    private readonly Label _timeLabel;
    private readonly Label _agentsLabel;
    private readonly Label _stateLabel;
    private readonly Label _rosLabel;
    private readonly VisualElement _stateDot;
    private readonly VisualElement _scenarioIcon;
    private readonly VisualElement _timeIcon;
    private readonly VisualElement _agentsIcon;
    private readonly VisualElement _rosIcon;
    private readonly ScenarioManager _scenarioManager;
    private readonly EnvROS _envRos;

    private SimulationState _state = SimulationState.Idle;
    private float _elapsedSeconds;
    private float _nextAgentCount;
    private int _robotCount;
    private int _humanCount;

    public SimulationStatusBar(VisualElement root)
    {
        _scenarioLabel = root.Q<Label>("StatusScenario");
        _timeLabel = root.Q<Label>("StatusTime");
        _agentsLabel = root.Q<Label>("StatusAgents");
        _stateLabel = root.Q<Label>("StatusStateLabel");
        _rosLabel = root.Q<Label>("StatusRos");
        _stateDot = root.Q<VisualElement>("StatusStateDot");
        _scenarioIcon = root.Q<VisualElement>("StatusScenarioIcon");
        _timeIcon = root.Q<VisualElement>("StatusTimeIcon");
        _agentsIcon = root.Q<VisualElement>("StatusAgentsIcon");
        _rosIcon = root.Q<VisualElement>("StatusRosIcon");

        if (_stateLabel == null || _stateDot == null)
        {
            Debug.LogWarning("[SimulationStatusBar] The status bar is incomplete; it stays inert.");
            return;
        }

        ApplyIcon(_scenarioIcon, ScenarioIconPath);
        ApplyIcon(_timeIcon, TimeIconPath);
        ApplyIcon(_agentsIcon, AgentsIconPath);
        ApplyIcon(_rosIcon, RosIconPath);

        _scenarioManager = UnityEngine.Object.FindAnyObjectByType<ScenarioManager>();
        if (_scenarioManager != null)
        {
            _scenarioManager.OnScenarioApplied += OnScenarioApplied;
            if (_scenarioManager.CurrentScenarioData != null)
                OnScenarioApplied(_scenarioManager.CurrentScenarioData);
        }

        _envRos = UnityEngine.Object.FindAnyObjectByType<EnvROS>();
        EventBus.Instance.Subscribe<SimulationStateChangedEvent>(OnStateChanged);

        CountAgents();
        ApplyState();
    }

    public void Dispose()
    {
        EventBus.Instance.Unsubscribe<SimulationStateChangedEvent>(OnStateChanged);

        if (_scenarioManager != null)
            _scenarioManager.OnScenarioApplied -= OnScenarioApplied;
    }

    /// <summary>Called every frame: it advances the mission clock and refreshes the slow figures.</summary>
    public void Tick()
    {
        if (_state == SimulationState.Running)
            _elapsedSeconds += Time.unscaledDeltaTime;

        if (_timeLabel != null)
            _timeLabel.text = $"Time: {FormatElapsed(_elapsedSeconds)}";

        if (Time.unscaledTime >= _nextAgentCount)
        {
            _nextAgentCount = Time.unscaledTime + AgentCountRefresh;
            CountAgents();
        }

        if (_rosLabel != null)
        {
            bool ready = _envRos != null && _envRos.IsInitialized;
            _rosLabel.EnableInClassList("is-online", ready);
            _rosLabel.EnableInClassList("is-offline", !ready);

            // The link pictogram follows the label: it lights up only once the ROS bridge is up.
            if (_rosIcon != null)
                _rosIcon.style.opacity = ready ? 1f : 0.4f;
        }
    }

    private void OnScenarioApplied(ScenarioData scenario)
    {
        _elapsedSeconds = 0f;

        if (_scenarioLabel != null)
            _scenarioLabel.text = $"Scenario: {scenario?.Name ?? "—"}";

        CountAgents();
    }

    private void OnStateChanged(SimulationStateChangedEvent evt)
    {
        _state = evt.NewState;
        ApplyState();
    }

    private void ApplyState()
    {
        if (_stateLabel != null)
            _stateLabel.text = _state switch
            {
                SimulationState.Ready => "Ready",
                SimulationState.Running => "Simulation running",
                SimulationState.Paused => "Paused",
                _ => "Idle"
            };

        if (_stateDot != null)
        {
            _stateDot.EnableInClassList("is-running", _state == SimulationState.Running);
            _stateDot.EnableInClassList("is-paused", _state == SimulationState.Paused);
            _stateDot.EnableInClassList("is-idle", _state == SimulationState.Idle || _state == SimulationState.Ready);
        }
    }

    private void CountAgents()
    {
        _robotCount = UnityEngine.Object.FindObjectsByType<Robot>().Length;
        _humanCount = UnityEngine.Object.FindObjectsByType<HumanAgent>().Length;

        if (_agentsLabel != null)
        {
            int total = _robotCount + _humanCount;
            _agentsLabel.text = total == 0
                ? "Agents: 0"
                : $"Agents: {total} ({_robotCount} {Plural("robot", _robotCount)}, {_humanCount} {Plural("human", _humanCount)})";
        }
    }

    private static string Plural(string noun, int count) => count == 1 ? noun : noun + "s";

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
}
