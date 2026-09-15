using System.Collections.Generic;
using RobotSNAP.Agents;
using RobotSNAP.CameraControl;
using RobotSNAP.Core.Scenario;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The camera bar of the simulation view: it picks which agent the camera works with, offers the
/// focus, follow and first-person actions, and hosts the view selector in the top-right corner.
///
/// This class is the single writer of the bar. The camera makes the focus, the bar only reflects
/// it, so the view button can never disagree with the agent that is actually followed.
/// </summary>
public class UICameraController : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private CameraController cameraController;

    private VisualElement _root;
    private VisualElement _agentSelector;
    private Button _selectorButton;
    private Label _selectorIcon;
    private Label _selectorLabel;
    private VisualElement _selectorPopup;
    private TextField _searchField;
    private Button _freeCameraOption;
    private VisualElement _robotOptions;
    private VisualElement _humanOptions;
    private Button _focusButton;
    private Button _followButton;
    private Button _firstPersonButton;
    private Button _viewButton;
    private VisualElement _viewMenu;

    private readonly List<Button> _viewMenuButtons = new List<Button>();
    private readonly Dictionary<Button, CameraController.CameraMode> _viewMenuModes = new Dictionary<Button, CameraController.CameraMode>();
    private readonly List<string> _optionNames = new List<string>();
    private ScenarioManager _scenarioManager;

    /// <summary>A refresh triggered from a camera event re-enters this class; the outer call is the
    /// one that ends up writing the fresh values.</summary>
    private bool _refreshing;

    private string _search = string.Empty;

    private void Start()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        if (cameraController == null) cameraController = FindAnyObjectByType<CameraController>();

        _root = uiDocument != null ? uiDocument.rootVisualElement : null;
        if (_root == null || cameraController == null)
        {
            Debug.LogWarning("[UICameraController] UI document or camera controller missing; the camera bar stays inert.");
            return;
        }

        _agentSelector = _root.Q<VisualElement>("AgentSelector");
        _selectorButton = _root.Q<Button>("AgentSelectorButton");
        _selectorIcon = _root.Q<Label>("AgentSelectorIcon");
        _selectorLabel = _root.Q<Label>("AgentSelectorLabel");
        _selectorPopup = _root.Q<VisualElement>("AgentSelectorPopup");
        _searchField = _root.Q<TextField>("AgentSearchField");
        _freeCameraOption = _root.Q<Button>("FreeCameraOption");
        _robotOptions = _root.Q<VisualElement>("RobotOptionList");
        _humanOptions = _root.Q<VisualElement>("HumanOptionList");
        _focusButton = _root.Q<Button>("FocusAgentButton");
        _followButton = _root.Q<Button>("FollowAgentButton");
        _firstPersonButton = _root.Q<Button>("FirstPersonButton");
        _viewButton = _root.Q<Button>("ViewButton");
        _viewMenu = _root.Q<VisualElement>("ViewMenu");

        if (_selectorButton == null || _focusButton == null || _viewButton == null || _viewMenu == null)
        {
            Debug.LogWarning("[UICameraController] The camera bar is incomplete; it stays inert.");
            return;
        }

        // Both popups start hidden. The class keeps them out of the first frame, the inline style is
        // what the code toggles afterwards — the two agree on the same value.
        SetPopupDisplay(_selectorPopup, false);
        SetPopupDisplay(_viewMenu, false);

        BuildViewMenu();
        WireEvents();
        Refresh();
    }

    private void OnDestroy()
    {
        if (_scenarioManager != null)
            _scenarioManager.OnScenarioApplied -= OnScenarioApplied;

        if (cameraController != null)
        {
            cameraController.OnFollowTargetChanged -= OnFollowTargetChanged;
            cameraController.OnTargetsUpdated -= OnTargetsUpdated;
            cameraController.OnViewChanged -= Refresh;
            cameraController.OnToolChanged -= OnToolChanged;
        }
    }

    private void WireEvents()
    {
        if (_selectorButton != null)
            _selectorButton.clicked += ToggleSelector;

        if (_freeCameraOption != null)
            _freeCameraOption.clicked += UseFreeCamera;

        if (_focusButton != null)
            _focusButton.clicked += FocusCurrentTarget;

        if (_followButton != null)
            _followButton.clicked += ToggleFollow;

        if (_firstPersonButton != null)
            _firstPersonButton.clicked += UseFirstPerson;

        if (_viewButton != null)
            _viewButton.clicked += ToggleViewMenu;

        if (_searchField != null)
            _searchField.RegisterValueChangedCallback(evt =>
            {
                _search = evt.newValue ?? string.Empty;
                RebuildAgentOptions();
            });

        // A click anywhere else closes the agent list; the selector itself is excluded so its own
        // button can open it without the same click closing it again.
        _root.RegisterCallback<ClickEvent>(evt =>
        {
            if (evt.target is VisualElement clicked && _agentSelector != null && _agentSelector.Contains(clicked))
                return;

            SetSelectorOpen(false);
        });

        cameraController.OnFollowTargetChanged += OnFollowTargetChanged;
        cameraController.OnTargetsUpdated += OnTargetsUpdated;
        cameraController.OnViewChanged += Refresh;
        cameraController.OnToolChanged += OnToolChanged;

        // The robot is spawned by the scenario, long after this UI exists. That event is the signal
        // that a new target appeared, so it is the moment to refresh the list and hand over the focus.
        _scenarioManager = FindAnyObjectByType<ScenarioManager>();
        if (_scenarioManager != null)
            _scenarioManager.OnScenarioApplied += OnScenarioApplied;
    }

    private void OnScenarioApplied(ScenarioData scenario)
    {
        if (cameraController != null) cameraController.RefreshFollowableTargets();
    }

    private void OnFollowTargetChanged(Transform target) => Refresh();

    private void OnTargetsUpdated(List<Transform> targets) => Refresh();

    private void OnToolChanged(CameraController.CameraTool tool) => Refresh();

    // ==========================================
    //          AGENT SELECTOR
    // ==========================================

    private void ToggleSelector() => SetSelectorOpen(_selectorPopup == null || !IsOpen(_selectorPopup));

    private void SetSelectorOpen(bool open)
    {
        if (_selectorPopup == null) return;

        SetPopupDisplay(_selectorPopup, open);
        if (open) RebuildAgentOptions();
    }

    private void UseFreeCamera()
    {
        cameraController.ClearFollowTarget();
        cameraController.SetCameraMode(CameraController.CameraMode.Free);
        SetSelectorOpen(false);
    }

        private void SelectTarget(Transform target)
        {
            // Picking a subject is asking the camera to work with it, so the bar can never show a
            // name the camera then ignores. Releasing the camera goes through the free camera entry.
            if (target != null)
                cameraController.FocusAgent(target);

            SetSelectorOpen(false);
        }

    /// <summary>
    /// Rebuilds the two option groups from the live target list. The list only changes when agents
    /// spawn or despawn, so the rebuild is skipped while the names stay the same.
    /// </summary>
    private void RebuildAgentOptions()
    {
        if (_robotOptions == null || _humanOptions == null) return;

        List<Transform> targets = cameraController.GetFollowableTargets();
        var names = new List<string>(targets.Count);
        foreach (Transform target in targets)
            names.Add(GetDisplayName(target));

        if (!SameNames(names))
        {
            _optionNames.Clear();
            _optionNames.AddRange(names);

            _robotOptions.Clear();
            _humanOptions.Clear();

            foreach (Transform target in targets)
            {
                Button option = CreateOption(target);
                bool isRobot = target.GetComponentInParent<Robot>() != null;
                (isRobot ? _robotOptions : _humanOptions).Add(option);
            }
        }

        ApplySearchFilter();
        HighlightSelectedOption();
    }

    private Button CreateOption(Transform target)
    {
        var option = new Button { text = GetDisplayName(target) };
        option.AddToClassList("agent-option");
        option.userData = target;
        option.clicked += () => SelectTarget(target);
        return option;
    }

    private void ApplySearchFilter()
    {
        foreach (VisualElement group in new[] { _robotOptions, _humanOptions })
        {
            if (group == null) continue;

            foreach (VisualElement option in group.Children())
            {
                bool matches = string.IsNullOrEmpty(_search) ||
                               option is Button button &&
                               button.text.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0;

                option.style.display = matches ? DisplayStyle.Flex : DisplayStyle.None;
            }

            bool anyVisible = false;
            foreach (VisualElement option in group.Children())
                if (option.style.display != DisplayStyle.None) { anyVisible = true; break; }

            // The group caption lives right before its list in the popup.
            VisualElement caption = group.parent != null
                ? group.parent.ElementAt(group.parent.IndexOf(group) - 1)
                : null;

            if (caption != null && caption.ClassListContains("agent-option-group"))
                caption.style.display = anyVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void HighlightSelectedOption()
    {
        Transform current = cameraController.GetCurrentFollowTarget();

        foreach (VisualElement group in new[] { _robotOptions, _humanOptions })
        {
            if (group == null) continue;

            foreach (VisualElement option in group.Children())
                option.EnableInClassList("is-selected", option.userData as Transform == current);
        }
    }

    private bool SameNames(List<string> names)
    {
        if (names.Count != _optionNames.Count) return false;

        for (int index = 0; index < names.Count; index++)
            if (names[index] != _optionNames[index]) return false;

        return true;
    }

    // ==========================================
    //          ACTIONS
    // ==========================================

    private void FocusCurrentTarget()
    {
        Transform target = cameraController.GetCurrentFollowTarget();
        if (target != null)
            cameraController.FocusAgent(target);
    }

        /// <summary>
        /// The follow toggle is about the camera binding, not about the selection: turning it off
        /// leaves the agent selected in the bar and in the panel, and the camera goes back to free.
        /// </summary>
        private void ToggleFollow()
        {
            cameraController.ToggleFollow();
        }

    private void UseFirstPerson()
    {
        if (cameraController.GetCurrentFollowTarget() == null) return;

        cameraController.SetCameraMode(CameraController.CameraMode.FirstPerson);
    }

    private void ToggleViewMenu()
    {
        if (_viewMenu == null) return;

        SetPopupDisplay(_viewMenu, !IsOpen(_viewMenu));
    }

    private void BuildViewMenu()
    {
        if (_viewMenu == null) return;

        _viewMenu.Clear();
        _viewMenuButtons.Clear();
        _viewMenuModes.Clear();

        foreach (KeyValuePair<CameraController.CameraMode, CameraModeEntry> pair in cameraController.ModeEntries)
        {
            CameraController.CameraMode mode = pair.Key;
            CameraModeEntry entry = pair.Value;

            var button = new Button { text = entry.DisplayName };
            button.AddToClassList("menu-option");
            button.userData = entry;
            button.clicked += () => OnViewModeSelected(mode, entry);

            _viewMenu.Add(button);
            _viewMenuButtons.Add(button);
            _viewMenuModes[button] = mode;
        }
    }

    private void OnViewModeSelected(CameraController.CameraMode mode, CameraModeEntry entry)
    {
        if (entry.RequiresFocus && cameraController.GetCurrentFollowTarget() == null)
        {
            Debug.LogWarning($"[UICameraController] {entry.DisplayName} needs a focused agent.");
            return;
        }

        cameraController.SetCameraMode(mode);
        SetPopupDisplay(_viewMenu, false);
        Refresh();
    }

    private static bool IsOpen(VisualElement popup) => popup.style.display == DisplayStyle.Flex;

    private static void SetPopupDisplay(VisualElement popup, bool open)
    {
        if (popup == null) return;

        popup.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
        popup.EnableInClassList("is-hidden", !open);
    }

    // ==========================================
    //          REFRESH
    // ==========================================

    private void Refresh()
    {
        if (_refreshing) return;

        _refreshing = true;
        try
        {
            RefreshInternal();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshInternal()
    {
        Transform target = cameraController.GetCurrentFollowTarget();
        bool hasTarget = target != null;
        bool following = cameraController.IsFollowingTarget;

        if (_selectorLabel != null)
            _selectorLabel.text = !hasTarget
                ? "Free camera"
                : following
                    ? $"Following: {GetDisplayName(target)}"
                    : $"Selected: {GetDisplayName(target)}";

        if (_selectorIcon != null)
        {
            string iconName = !hasTarget
                ? "Icons/agents"
                : target.GetComponentInParent<Robot>() != null ? "Icons/robot" : "Icons/humans";

            Texture2D icon = Resources.Load<Texture2D>(iconName);
            if (icon != null) _selectorIcon.style.backgroundImage = new StyleBackground(icon);
        }

        if (_focusButton != null) _focusButton.SetEnabled(hasTarget);
        if (_firstPersonButton != null) _firstPersonButton.SetEnabled(hasTarget);

        if (_followButton != null)
        {
            _followButton.SetEnabled(hasTarget);
            _followButton.text = following ? "Following" : "Follow";
            _followButton.EnableInClassList("is-active", following);
        }

        if (_viewButton != null)
            _viewButton.text = $"{cameraController.GetCurrentDisplayName()}  ▾";

        RefreshViewMenuInteractivity(hasTarget);
        HighlightSelectedOption();
    }

    private void RefreshViewMenuInteractivity(bool hasTarget)
    {
        foreach (Button button in _viewMenuButtons)
        {
            if (button.userData is not CameraModeEntry entry) continue;

            bool shouldEnable = !entry.RequiresFocus || hasTarget;
            button.SetEnabled(shouldEnable);
            button.EnableInClassList("disabled-option", !shouldEnable);

            // The menu carries the same truth as the chip: the view that is actually running.
            bool isCurrent = _viewMenuModes.TryGetValue(button, out CameraController.CameraMode mode) &&
                             mode == cameraController.GetCurrentMode();
            button.text = isCurrent ? $"✓  {entry.DisplayName}" : $"     {entry.DisplayName}";
            button.EnableInClassList("is-active", isCurrent);
        }
    }

    private string GetDisplayName(Transform target)
    {
        if (target == null) return "Free camera";

        // A robot is followed through its base link, which does not carry the Robot component.
        Robot robot = target.GetComponentInParent<Robot>();
        if (robot != null && !string.IsNullOrEmpty(robot.AgentName)) return robot.AgentName;

        HumanAgent human = target.GetComponentInParent<HumanAgent>();
        if (human != null && !string.IsNullOrEmpty(human.AgentName)) return human.AgentName;

        return target.name;
    }

    /// <summary>
    /// Optional progress an agent can publish. Kept as a public contract for agents that want the
    /// dashboard to show how far along they are.
    /// </summary>
    public interface IAgentProgress { float Progress { get; } }
}
