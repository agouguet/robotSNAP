using RobotSNAP.CameraControl;
using RobotSNAP;
using RobotSNAP.Human;
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

    private List<(string displayName, string modeName)> _viewModes;

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

        _viewModes = new List<(string displayName, string modeName)>
        {
            ("Free", "Free"),
            ("Top Down", "TopDown"),
            ("First Person", "FirstPerson"),
            ("Orbit", "Orbit"),
            ("Multi Target", "MultiTarget"),
            ("Cinematic", "Cinematic")
        };

        BuildViewMenu();
        RefreshUI();

        // Événements UI
        if (_prevButton != null) _prevButton.clicked += () => { cameraController.CycleFollowTarget(-1); RefreshUI(); };
        if (_nextButton != null) _nextButton.clicked += () => { cameraController.CycleFollowTarget(1); RefreshUI(); };
        if (_agentDropdown != null) _agentDropdown.RegisterValueChangedCallback(evt => OnAgentDropdownChanged(evt.newValue));
        if (_viewButton != null) _viewButton.RegisterCallback<ClickEvent>(evt => ToggleViewMenu());
    }

    private void BuildViewMenu()
    {
        if (_viewMenu == null) return;
        _viewMenu.Clear();
        foreach (var (display, mode) in _viewModes)
        {
            var btn = new Button { text = display };
            btn.AddToClassList("menu-option");
            // Capturer les variables pour éviter les problèmes de closure
            string capturedMode = mode;
            string capturedDisplay = display;
            btn.clicked += () => OnViewModeSelected(capturedMode, capturedDisplay);
            _viewMenu.Add(btn);
        }
        Debug.Log($"ViewMenu built with {_viewMenu.childCount} options");
    }

    private void ToggleViewMenu()
    {
        // if (_viewMenu == null) return;
        // bool isVisible = _viewMenu.style.display == DisplayStyle.Flex;
        // _viewMenu.style.display = isVisible ? DisplayStyle.None : DisplayStyle.Flex;
        // Debug.Log($"ToggleViewMenu: now visible = {!isVisible}");
    }

    private void OnViewModeSelected(string modeName, string displayName)
    {
        cameraController.SetCameraMode(modeName);
        if (_viewButton != null) _viewButton.text = displayName;
        if (_viewMenu != null) _viewMenu.style.display = DisplayStyle.None;
    }

    private void RefreshUI()
    {
        var target = cameraController.GetCurrentFollowTarget();

        // 1. Labels de l'agent courant
        if (target != null)
        {
            if (_agentNameLabel != null)
                _agentNameLabel.text = GetAgentDisplayName(target);
            if (_agentProgressLabel != null)
                _agentProgressLabel.text = GetAgentProgress(target);
        }
        else
        {
            if (_agentNameLabel != null) _agentNameLabel.text = "No target";
            if (_agentProgressLabel != null) _agentProgressLabel.text = "0%";
        }

        // 2. Dropdown des agents (liste complète)
        var targets = cameraController.GetFollowableTargets();
        if (targets != null && targets.Count > 0 && _agentDropdown != null)
        {
            // Générer les noms affichés
            var names = new List<string>();
            foreach (var t in targets)
                names.Add(GetAgentDisplayName(t));

            // Mise à jour des choix seulement si nécessaire
            bool needRefresh = false;
            if (_agentDropdown.choices.Count != names.Count)
                needRefresh = true;
            else
            {
                for (int i = 0; i < names.Count; i++)
                    if (_agentDropdown.choices[i] != names[i])
                    {
                        needRefresh = true;
                        break;
                    }
            }
            if (needRefresh)
                _agentDropdown.choices = names;

            // Sélectionner l'index correspondant à l'agent courant
            string currentName = GetAgentDisplayName(target);
            if (_agentDropdown.value != currentName)
                _agentDropdown.SetValueWithoutNotify(currentName);
        }

        // 3. Mettre à jour le texte du bouton de vue selon le mode courant
        if (_viewButton != null)
        {
            string currentMode = cameraController.GetCurrentModeName();
            foreach (var (display, mode) in _viewModes)
            {
                if (mode == currentMode)
                {
                    _viewButton.text = display;
                    break;
                }
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
        if (robot != null && !string.IsNullOrEmpty(robot.robotName))
            return robot.robotName;
        var human = target.GetComponent<HumanAvatar>();
        if (human != null)
            return $"Agent {human.agentName}";
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

    // Interface optionnelle pour les agents qui fournissent leur progression
    public interface IAgentProgress
    {
        float Progress { get; }
    }
}