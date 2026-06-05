using RobotSNAP.CameraControl;
using RobotSNAP;
using RobotSNAP.Agents;
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
    private VisualElement _viewMenu;

    private List<Button> _viewMenuButtons = new List<Button>();

    private void Start()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        if (cameraController == null) cameraController = FindObjectOfType<CameraController>();
        if (cameraController == null) { Debug.LogError("CameraController missing"); return; }

        var root = uiDocument.rootVisualElement;
        _agentDropdown = root.Q<DropdownField>("AgentDropdown");
        _prevButton = root.Q<Button>("PrevAgentButton");
        _nextButton = root.Q<Button>("NextAgentButton");
        _agentNameLabel = root.Q<Label>("AgentNameLabel");
        _agentProgressLabel = root.Q<Label>("AgentProgressLabel");
        _viewButton = root.Q<Button>("ViewButton");
        _viewMenu = root.Q<VisualElement>("ViewMenu");

        BuildViewMenu();
        RefreshUI();

        if (_prevButton != null) _prevButton.clicked += () => { cameraController.CycleFollowTarget(-1); RefreshUI(); };
        if (_nextButton != null) _nextButton.clicked += () => { cameraController.CycleFollowTarget(1); RefreshUI(); };
        if (_agentDropdown != null) _agentDropdown.RegisterValueChangedCallback(evt => OnAgentDropdownChanged(evt.newValue));
        if (_viewButton != null) _viewButton.RegisterCallback<ClickEvent>(evt => ToggleViewMenu());

        cameraController.OnFollowTargetChanged += _ => RefreshUI();
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
        var target = cameraController.GetCurrentFollowTarget();
        bool hasFocus = target != null;

        if (hasFocus)
        {
            if (_agentNameLabel != null) _agentNameLabel.text = GetAgentDisplayName(target);
            if (_agentProgressLabel != null) _agentProgressLabel.text = GetAgentProgress(target);
        }
        else
        {
            if (_agentNameLabel != null) _agentNameLabel.text = "No target";
            if (_agentProgressLabel != null) _agentProgressLabel.text = "0%";
        }

        var targets = cameraController.GetFollowableTargets();
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

    private string GetAgentProgress(Transform target)
    {
        if (target == null) return "0%";
        var progressAgent = target.GetComponent<IAgentProgress>();
        if (progressAgent != null)
            return $"{progressAgent.Progress * 100:F0}%";
        return "50%";
    }

    public interface IAgentProgress { float Progress { get; } }
}