using System;
using System.Collections.Generic;
using RobotSNAP.Agents;
using RobotSNAP.CameraControl;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Right-hand panel of the simulation overlay: the followed-agent card plus the searchable
/// agent list. Rows are rebuilt from the camera target list on events only, never per frame.
/// </summary>
public sealed class SimulationAgentPanel
{
    private const string RobotIconPath = "Icons/robot";
    private const string HumanIconPath = "Icons/humans";

    private static Texture2D _robotIcon;
    private static Texture2D _humanIcon;
    private static bool _iconsLoaded;

    private readonly CameraController _camera;

    private readonly VisualElement _panel;
    private readonly Button _collapseButton;
    private readonly VisualElement _selectedIcon;
    private readonly Label _selectedName;
    private readonly Label _selectedBadge;
    private readonly VisualElement _info;
    private readonly Button _focusButton;
    private readonly Button _followButton;
    private readonly Label _listTitle;
    private readonly TextField _search;
    private readonly VisualElement _list;

    private readonly List<Transform> _targets = new List<Transform>();
    private string _filter = string.Empty;
    private bool _collapsed;

    /// <summary>A refresh done from inside a camera event loops back through the panel's own
    /// subscriptions; the outer call is the one that already reads the fresh state.</summary>
    private bool _refreshing;

    public SimulationAgentPanel(VisualElement root, CameraController camera)
    {
        _camera = camera;

        if (root == null)
        {
            Debug.LogWarning("[SimulationAgentPanel] Overlay root is null; the agent panel stays inert.");
            return;
        }

        if (_camera == null)
            Debug.LogWarning("[SimulationAgentPanel] CameraController is null; the agent panel stays inert.");

        _panel = Query<VisualElement>(root, "AgentPanel");
        _collapseButton = Query<Button>(root, "AgentPanelCollapseButton");
        _selectedIcon = Query<VisualElement>(root, "SelectedAgentIcon");
        _selectedName = Query<Label>(root, "SelectedAgentName");
        _selectedBadge = Query<Label>(root, "SelectedAgentBadge");
        _info = Query<VisualElement>(root, "SelectedAgentInfo");
        _focusButton = Query<Button>(root, "PanelFocusButton");
        _followButton = Query<Button>(root, "PanelFollowButton");
        _listTitle = Query<Label>(root, "AgentListTitle");
        _search = Query<TextField>(root, "AgentListSearch");
        _list = Query<VisualElement>(root, "AgentList");

        if (_collapseButton != null)
            _collapseButton.clicked += ToggleCollapsed;

        if (_focusButton != null)
        {
            _focusButton.clicked += () =>
            {
                Transform target = CurrentTarget();
                if (target != null) _camera.FocusAgent(target);
            };
        }

        if (_followButton != null)
        {
            _followButton.clicked += () =>
            {
                Transform target = CurrentTarget();
                if (target == null) return;

                // The button doubles as follow and release. The camera owns that decision so the
                // bar and this panel can never disagree about what "following" means.
                if (_camera.GetCurrentFollowTarget() == target && _camera.IsFollowingTarget)
                    _camera.ToggleFollow();
                else
                    _camera.FocusAgent(target);
            };
        }

        if (_search != null)
            _search.RegisterValueChangedCallback(evt => OnSearchChanged(evt.newValue));

        if (_camera != null)
        {
            _camera.OnTargetsUpdated += OnTargetsUpdated;
            _camera.OnFollowTargetChanged += OnFollowTargetChanged;
            // The card badge reports the camera binding, and picking a view is what changes it.
            _camera.OnViewChanged += OnViewChanged;
        }

        Refresh();
    }

    /// <summary>Rebuilds the card and the list from the camera's current targets.</summary>
    public void Refresh()
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
        if (_camera == null || _panel == null) return;

        // Reading the targets is also what lets the camera resolve its default focus, so the
        // list has to be asked for before the current target is read.
        _targets.Clear();
        List<Transform> targets = _camera.GetFollowableTargets();
        if (targets != null) _targets.AddRange(targets);

        Transform current = _camera.GetCurrentFollowTarget();

        UpdateSelectedCard(current);
        UpdateListTitle();
        BuildAgentRows(current);
    }

    private void UpdateSelectedCard(Transform target)
    {
        bool hasTarget = target != null;

        if (_selectedName != null)
            _selectedName.text = hasTarget ? GetAgentName(target) : "No agent";

        if (_selectedBadge != null)
            _selectedBadge.text = !hasTarget
                ? "Free camera"
                : _camera.IsFollowingTarget ? "Following" : "Selected";

        if (_selectedIcon != null)
        {
            // The card icon follows the agent type; without a target the USS colour stands alone.
            Texture2D texture = hasTarget ? GetIconTexture(target.GetComponentInParent<Robot>() != null) : null;
            _selectedIcon.style.backgroundImage = texture != null ? new StyleBackground(texture) : StyleKeyword.None;
        }

        if (_focusButton != null) _focusButton.SetEnabled(hasTarget);
        if (_followButton != null)
        {
            _followButton.SetEnabled(hasTarget);
            _followButton.text = hasTarget && _camera.IsFollowingTarget ? "Stop following" : "Follow";
        }

        BuildInfoRows(target);
    }

    private void BuildInfoRows(Transform target)
    {
        if (_info == null) return;

        _info.Clear();
        if (target == null) return;

        // Robots are followed through their moving child (base_link), so the agent component
        // lives above the target in the hierarchy.
        Robot robot = target.GetComponentInParent<Robot>();
        HumanAgent human = robot != null ? null : target.GetComponentInParent<HumanAgent>();
        BaseAgent agent = robot != null ? robot : (BaseAgent)human;

        _info.Add(CreateInfoRow("Type", robot != null ? "Robot" : human != null ? "Human" : "Agent"));

        Vector3 position = Vector3.zero;
        if (robot != null)
        {
            position = robot.Position;
        }
        else if (human != null)
        {
            Vector2 planar = human.GetCurrentPosition2D();
            position = new Vector3(planar.x, 0f, planar.y);
        }
        else
        {
            position = target.position;
        }
        _info.Add(CreateInfoRow("Position", FormatPlanar(position)));

        if (agent != null)
        {
            _info.Add(CreateInfoRow("Orientation", FormatYaw(agent.Rotation.eulerAngles.y)));
            _info.Add(CreateInfoRow("Speed", $"{FormatNumber(agent.Speed)} m/s"));
            _info.Add(CreateInfoRow("Goal", agent.HasGoal ? FormatPlanar(agent.Goal) : "—"));
            _info.Add(CreateInfoRow("Status", agent.IsActive ? "Active" : "Idle"));
        }
        else
        {
            // Target without a known agent component: report what the transform alone can tell.
            _info.Add(CreateInfoRow("Orientation", FormatYaw(target.rotation.eulerAngles.y)));
            _info.Add(CreateInfoRow("Status", target.gameObject.activeInHierarchy ? "Active" : "Idle"));
        }
    }

    private void UpdateListTitle()
    {
        if (_listTitle == null) return;

        int robots = 0;
        int humans = 0;
        foreach (Transform target in _targets)
        {
            if (target == null) continue;
            if (target.GetComponentInParent<Robot>() != null) robots++;
            else if (target.GetComponentInParent<HumanAgent>() != null) humans++;
        }

        _listTitle.text = robots > 0 && humans > 0
            ? $"Agent list ({_targets.Count} — {robots} robot{Plural(robots)}, {humans} human{Plural(humans)})"
            : $"Agent list ({_targets.Count})";
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";

    private void BuildAgentRows(Transform current)
    {
        if (_list == null) return;

        _list.Clear();

        string filter = _filter == null ? string.Empty : _filter.Trim();
        foreach (Transform target in _targets)
        {
            if (target == null) continue;

            string name = GetAgentName(target);
            if (filter.Length > 0 && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            _list.Add(CreateAgentRow(target, name, current));
        }
    }

    private VisualElement CreateAgentRow(Transform target, string name, Transform current)
    {
        bool isRobot = target.GetComponentInParent<Robot>() != null;

        var row = new VisualElement();
        row.AddToClassList("agent-row");
        if (current == target) row.AddToClassList("is-selected");

        var icon = new VisualElement();
        icon.AddToClassList("agent-row-icon");
        icon.AddToClassList(isRobot ? "robot" : "human");
        Texture2D texture = GetIconTexture(isRobot);
        // No texture in Resources: the coloured USS class is enough, so leave the image empty.
        if (texture != null) icon.style.backgroundImage = new StyleBackground(texture);
        row.Add(icon);

        var nameLabel = new Label(name);
        nameLabel.AddToClassList("agent-row-name");
        row.Add(nameLabel);

        var detail = new Label(isRobot ? "Robot" : "Human");
        detail.AddToClassList("agent-row-detail");
        row.Add(detail);

        var focusButton = new Button { text = "◎", tooltip = "Focus this agent" };
        focusButton.AddToClassList("agent-row-action");
        // The row itself focuses too, and a click on the button bubbles up to it: stop it here
        // so a single click is acted on once.
        focusButton.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
        focusButton.clicked += () => _camera.FocusAgent(target);
        row.Add(focusButton);

        row.RegisterCallback<ClickEvent>(_ => _camera.FocusAgent(target));
        return row;
    }

    private void OnSearchChanged(string value)
    {
        _filter = value ?? string.Empty;
        if (_camera == null) return;

        // Only the rows are rebuilt: the selection shown in the card is left untouched.
        BuildAgentRows(_camera.GetCurrentFollowTarget());
    }

    private void OnTargetsUpdated(List<Transform> targets) => Refresh();

    private void OnFollowTargetChanged(Transform target) => Refresh();

    private void OnViewChanged() => Refresh();

    private void ToggleCollapsed()
    {
        _collapsed = !_collapsed;

        if (_panel != null)
        {
            if (_collapsed) _panel.AddToClassList("is-collapsed");
            else _panel.RemoveFromClassList("is-collapsed");
        }

        if (_collapseButton != null) _collapseButton.text = _collapsed ? "›" : "‹";
    }

    private Transform CurrentTarget()
    {
        if (_camera == null)
        {
            Debug.LogWarning("[SimulationAgentPanel] No CameraController: the action is ignored.");
            return null;
        }

        return _camera.GetCurrentFollowTarget();
    }

    private static VisualElement CreateInfoRow(string label, string value)
    {
        var row = new VisualElement();
        row.AddToClassList("agent-info-row");

        var labelElement = new Label(label);
        labelElement.AddToClassList("agent-info-label");
        row.Add(labelElement);

        var valueElement = new Label(value);
        valueElement.AddToClassList("agent-info-value");
        row.Add(valueElement);

        return row;
    }

    private static string FormatPlanar(Vector3 position) => $"({FormatNumber(position.x)}, {FormatNumber(position.z)}) m";

    /// <summary>Fixed separators: the values read the same whatever the machine locale is.</summary>
    private static string FormatNumber(float value) =>
        value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatYaw(float yaw) => $"{Mathf.RoundToInt(yaw)}°";

    private static string GetAgentName(Transform target)
    {
        if (target == null) return "No agent";

        Robot robot = target.GetComponentInParent<Robot>();
        if (robot != null && !string.IsNullOrEmpty(robot.AgentName)) return robot.AgentName;

        HumanAgent human = target.GetComponentInParent<HumanAgent>();
        if (human != null && !string.IsNullOrEmpty(human.AgentName)) return human.AgentName;

        return target.name;
    }

    /// <summary>Textures are loaded once: the list rebuilds on every target or filter change.</summary>
    private static Texture2D GetIconTexture(bool isRobot)
    {
        if (!_iconsLoaded)
        {
            _robotIcon = Resources.Load<Texture2D>(RobotIconPath);
            _humanIcon = Resources.Load<Texture2D>(HumanIconPath);
            _iconsLoaded = true;
        }

        return isRobot ? _robotIcon : _humanIcon;
    }

    private static T Query<T>(VisualElement root, string name) where T : VisualElement
    {
        T element = root.Q<T>(name);
        if (element == null)
            Debug.LogWarning($"[SimulationAgentPanel] Element '{name}' ({typeof(T).Name}) not found in the simulation overlay; that part of the panel stays inert.");
        return element;
    }
}
