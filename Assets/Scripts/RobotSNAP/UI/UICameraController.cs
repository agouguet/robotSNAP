using RobotSNAP.CameraControl;
using RobotSNAP;
using RobotSNAP.Agents;
using RobotSNAP.Core.Scenario;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

public class UICameraController : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private CameraController cameraController;

    private DropdownField _agentDropdown;
    private Button _prevButton, _nextButton, _viewButton;
    private Label _agentNameLabel, _agentProgressLabel;
    private VisualElement _viewMenu, _focusStatusDot, _agentProgressBlock, _agentProgressSeparator;
    private ScenarioManager _scenarioManager;

    private List<Button> _viewMenuButtons = new List<Button>();

    /// <summary>A target refresh inside <see cref="RefreshUI"/> calls the UI back through
    /// the camera events; the outer call already reads the fresh state afterwards.</summary>
    private bool _refreshing;

    private void Start()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        if (cameraController == null) cameraController = FindAnyObjectByType<CameraController>();
        if (cameraController == null) { Debug.LogError("CameraController missing"); return; }

        var root = uiDocument.rootVisualElement;
        _agentDropdown = root.Q<DropdownField>("AgentDropdown");
        _prevButton = root.Q<Button>("PrevAgentButton");
        _nextButton = root.Q<Button>("NextAgentButton");
        _agentNameLabel = root.Q<Label>("AgentNameLabel");
        _agentProgressLabel = root.Q<Label>("AgentProgressLabel");
        _viewButton = root.Q<Button>("ViewButton");
        _viewMenu = root.Q<VisualElement>("ViewMenu");
        _focusStatusDot = root.Q<VisualElement>("FocusStatusDot");
        _agentProgressBlock = root.Q<VisualElement>("AgentProgressBlock");
        _agentProgressSeparator = root.Q<VisualElement>("AgentProgressSeparator");

        BuildViewMenu();
        RefreshUI();

        if (_prevButton != null) _prevButton.clicked += () => { cameraController.CycleFollowTarget(-1); RefreshUI(); };
        if (_nextButton != null) _nextButton.clicked += () => { cameraController.CycleFollowTarget(1); RefreshUI(); };
        if (_agentDropdown != null) _agentDropdown.RegisterValueChangedCallback(evt => OnAgentDropdownChanged(evt.newValue));
        if (_viewButton != null) _viewButton.RegisterCallback<ClickEvent>(evt => ToggleViewMenu());

        cameraController.OnFollowTargetChanged += _ => RefreshUI();
        // The dropdown tracks the live target list, which grows when a scenario spawns its agents.
        cameraController.OnTargetsUpdated += _ => RefreshUI();

        // The robot is spawned by the scenario, long after this UI exists. That event is the
        // signal that a new target appeared, so it is the moment to refresh the target list and
        // let the camera hand the focus to the robot.
        _scenarioManager = FindAnyObjectByType<ScenarioManager>();
        if (_scenarioManager != null)
            _scenarioManager.OnScenarioApplied += OnScenarioApplied;
    }

    private void OnDestroy()
    {
        if (_scenarioManager != null)
            _scenarioManager.OnScenarioApplied -= OnScenarioApplied;
    }

    private void OnScenarioApplied(ScenarioData scenario)
    {
        if (cameraController != null) cameraController.RefreshFollowableTargets();
    }

    private void BuildViewMenu()
    {
        if (_viewMenu == null) return;
        _viewMenu.Clear();
        _viewMenuButtons.Clear();

        Debug.Log("Building view menu with modes:");
        Debug.Log($"Current follow target: {(cameraController.GetCurrentFollowTarget() != null ? cameraController.GetCurrentFollowTarget().name : "None")}");
        Debug.Log("[UICameraController] Mode entries:" + cameraController.ModeEntries);
        foreach (var pair in cameraController.ModeEntries)
        {
            var mode = pair.Key;
            var entry = pair.Value;
            var btn = new Button { text = entry.DisplayName };
            btn.AddToClassList("menu-option");
            btn.userData = entry; // On stocke l'entrée (pas besoin du mode car on peut récupérer RequiresFocus)
            btn.clicked += () => OnViewModeSelected(mode.ToString(), entry.DisplayName);
            _viewMenu.Add(btn);
            _viewMenuButtons.Add(btn);
        }
        UpdateViewMenuInteractivity();
    }

    private void ToggleViewMenu()
    {
        // if (_viewMenu == null) return;
        // bool isVisible = _viewMenu.style.display == DisplayStyle.Flex;
        // _viewMenu.style.display = isVisible ? DisplayStyle.None : DisplayStyle.Flex;
    }

    private void OnViewModeSelected(string modeName, string displayName)
    {
        // Vérification du focus
        foreach (var pair in cameraController.ModeEntries)
        {
            if (pair.Key.ToString() == modeName && pair.Value.RequiresFocus && cameraController.GetCurrentFollowTarget() == null)
            {
                Debug.LogWarning($"Cannot switch to {displayName}: no agent focused.");
                return;
            }
        }
        cameraController.SetCameraMode(modeName);
        if (_viewButton != null) _viewButton.text = displayName;
        if (_viewMenu != null) _viewMenu.style.display = DisplayStyle.None;
    }

    private void RefreshUI()
    {
        // Refreshing the target list is also what lets the camera hand the focus to the robot by
        // default, so the targets have to be asked for before the current target is read. The
        // camera events fired from in there re-enter this method; the outer call is the one
        // that ends up writing the fresh values.
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            RefreshUIInternal();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshUIInternal()
    {
        var targets = cameraController.GetFollowableTargets();
        var target = cameraController.GetCurrentFollowTarget();
        bool hasFocus = target != null;

        if (_agentNameLabel != null) _agentNameLabel.text = hasFocus ? GetAgentDisplayName(target) : "No target";

        // Camera lock: filled while an agent is followed, muted while there is no focus.
        if (_focusStatusDot != null)
        {
            if (hasFocus) _focusStatusDot.RemoveFromClassList("idle");
            else _focusStatusDot.AddToClassList("idle");
        }

        // No progress figure rather than a made-up one: the bar hides the block until the
        // followed agent actually reports its progress.
        float progress = 0f;
        bool hasProgress = hasFocus && TryGetAgentProgress(target, out progress);
        if (_agentProgressBlock != null)
            _agentProgressBlock.style.display = hasProgress ? DisplayStyle.Flex : DisplayStyle.None;
        if (_agentProgressSeparator != null)
            _agentProgressSeparator.style.display = hasProgress ? DisplayStyle.Flex : DisplayStyle.None;
        if (_agentProgressLabel != null)
            _agentProgressLabel.text = hasProgress ? $"{progress * 100:F0}%" : string.Empty;

        if (targets != null && targets.Count > 0 && _agentDropdown != null)
        {
            var names = new List<string>();
            foreach (var t in targets) names.Add(GetAgentDisplayName(t));

            bool needRefresh = false;
            if (_agentDropdown.choices.Count != names.Count) needRefresh = true;
            else
            {
                for (int i = 0; i < names.Count; i++)
                    if (_agentDropdown.choices[i] != names[i]) { needRefresh = true; break; }
            }
            if (needRefresh) _agentDropdown.choices = names;

            string currentName = hasFocus ? GetAgentDisplayName(target) : "";
            if (_agentDropdown.value != currentName) _agentDropdown.SetValueWithoutNotify(currentName);
            _agentDropdown.SetEnabled(hasFocus);
        }

        if (_viewButton != null)
        {
            string currentMode = cameraController.GetCurrentModeName();
            foreach (var pair in cameraController.ModeEntries)
            {
                if (pair.Key.ToString() == currentMode)
                {
                    _viewButton.text = pair.Value.DisplayName;
                    break;
                }
            }
        }
        UpdateViewMenuInteractivity();
    }

    private void UpdateViewMenuInteractivity()
    {
        bool hasFocus = cameraController.GetCurrentFollowTarget() != null;
        foreach (var btn in _viewMenuButtons)
        {
            if (btn.userData is CameraModeEntry entry)
            {
                bool shouldEnable = !entry.RequiresFocus || hasFocus;
                btn.SetEnabled(shouldEnable);
                if (shouldEnable)
                    btn.RemoveFromClassList("disabled-option");
                else
                    btn.AddToClassList("disabled-option");
            }
        }
    }

    private void OnAgentDropdownChanged(string selectedName)
    {
        var targets = cameraController.GetFollowableTargets();
        foreach (var t in targets)
        {
            if (GetAgentDisplayName(t) == selectedName)
            {
                cameraController.SetFollowTarget(t);
                RefreshUI();
                break;
            }
        }
    }

    private string GetAgentDisplayName(Transform target)
    {
        if (target == null) return "None";
        var robot = target.GetComponent<Robot>();
        if (robot != null && !string.IsNullOrEmpty(robot.AgentName))
            return robot.AgentName;
        var human = target.GetComponent<HumanAgent>();
        if (human != null)
            return $"Agent {human.AgentName}";
        return target.name;
    }

    /// <summary>
    /// Reads the progress an agent publishes through <see cref="IAgentProgress"/>. Returns false
    /// when the agent does not implement it, so the bar shows nothing instead of a stand-in value.
    /// </summary>
    private static bool TryGetAgentProgress(Transform target, out float progress)
    {
        progress = 0f;
        if (target == null) return false;
        var progressAgent = target.GetComponent<IAgentProgress>();
        if (progressAgent == null) return false;
        progress = Mathf.Clamp01(progressAgent.Progress);
        return true;
    }

    public interface IAgentProgress { float Progress { get; } }
}
