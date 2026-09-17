using System;
using System.Collections.Generic;
using RobotSNAP.Agents;
using RobotSNAP.CameraControl;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Right-hand panel of the simulation overlay: the agent the camera is on, with its live figures,
/// then the searchable list of the agents the view can be handed to. Both sections fold away and
/// the whole panel collapses to its rail.
///
/// The camera owns the selection. This panel never decides who is followed: it reflects the current
/// target and asks the camera for a new one when a row is clicked, which is the same call the click
/// on an agent in the scene makes. The figures of the selected agent move every frame, so they are
/// re-read on a slow tick rather than per frame.
/// </summary>
public sealed class SimulationAgentPanel
{
    private const string RobotIconPath = "Icons/robot";
    private const string HumanIconPath = "Icons/humans";

    /// <summary>How often the figures of the selected agent are re-read. Ten times a second reads
    /// as live, and keeps the panel from rebuilding its rows every frame.</summary>
    private const float InfoRefreshInterval = 0.1f;

    /// <summary>How often an empty list is re-checked, in case agents appeared without an event.</summary>
    private const float EmptyListRecheckInterval = 1f;

    private const string ExpandedGlyph = "▾";
    private const string FoldedGlyph = "▸";

    private static Texture2D _robotIcon;
    private static Texture2D _humanIcon;
    private static bool _iconsLoaded;

    private readonly CameraController _camera;

    private readonly VisualElement _panel;
    private readonly Button _collapseButton;
    private readonly Button _selectedHeader;
    private readonly VisualElement _selectedContent;
    private readonly Button _listHeader;
    private readonly VisualElement _listContent;
    private readonly VisualElement _selectedIcon;
    private readonly Label _selectedName;
    private readonly Label _selectedBadge;
    private readonly VisualElement _info;
    private readonly TextField _search;
    private readonly VisualElement _list;

    private readonly List<Transform> _targets = new List<Transform>();
    private string _filter = string.Empty;
    private string _listHeadingText = "Agents List";
    private bool _collapsed;
    private bool _selectedFolded;
    private bool _listFolded;
    private float _nextInfoRefresh;
    private float _nextEmptyCheck;
    private Transform _describedTarget;

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
        _selectedHeader = Query<Button>(root, "SelectedAgentSectionHeader");
        _selectedContent = Query<VisualElement>(root, "SelectedAgentSectionContent");
        _listHeader = Query<Button>(root, "AgentListSectionHeader");
        _listContent = Query<VisualElement>(root, "AgentListSectionContent");
        _selectedIcon = Query<VisualElement>(root, "SelectedAgentIcon");
        _selectedName = Query<Label>(root, "SelectedAgentName");
        _selectedBadge = Query<Label>(root, "SelectedAgentBadge");
        _info = Query<VisualElement>(root, "SelectedAgentInfo");
        _search = Query<TextField>(root, "AgentListSearch");
        _list = Query<VisualElement>(root, "AgentList");

        if (_collapseButton != null)
            _collapseButton.clicked += ToggleCollapsed;

        if (_selectedHeader != null)
            _selectedHeader.clicked += ToggleSelectedSection;

        if (_listHeader != null)
            _listHeader.clicked += ToggleListSection;

        if (_search != null)
            _search.RegisterValueChangedCallback(evt => OnSearchChanged(evt.newValue));

        if (_camera != null)
        {
            _camera.OnTargetsUpdated += OnTargetsUpdated;
            _camera.OnFollowTargetChanged += OnFollowTargetChanged;
        }

        ApplySectionState();
        Refresh();
    }

    /// <summary>
    /// Called every frame. Only the figures of the selected agent are re-read, and only a few times
    /// a second: the list and the card change on camera events, not on the clock.
    /// </summary>
    public void Tick()
    {
        if (_camera == null || _info == null) return;
        if (Time.unscaledTime < _nextInfoRefresh) return;

        _nextInfoRefresh = Time.unscaledTime + InfoRefreshInterval;

        // Safety net: a scenario can spawn agents without the camera list being refreshed by hand.
        if (_targets.Count == 0 && Time.unscaledTime >= _nextEmptyCheck)
        {
            _nextEmptyCheck = Time.unscaledTime + EmptyListRecheckInterval;
            Refresh();
        }

        Transform target = _camera.GetCurrentFollowTarget();
        if (target == null) return;

        BuildInfoRows(target);
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

        // Reading the targets is also what lets the camera resolve its list, so the list has to be
        // asked for before the current target is read.
        _targets.Clear();
        List<Transform> targets = _camera.GetFollowableTargets();
        if (targets != null) _targets.AddRange(targets);

        Transform current = _camera.GetCurrentFollowTarget();

        UpdateSelectedCard(current);
        UpdateListHeader();
        BuildAgentRows(current);
    }

    // ==========================================
    //          SECTIONS AND COLLAPSING
    // ==========================================

    private void ToggleCollapsed()
    {
        _collapsed = !_collapsed;
        _panel?.EnableInClassList("is-collapsed", _collapsed);

        if (_collapseButton != null)
            _collapseButton.text = _collapsed ? "‹" : "›";
    }

    private void ToggleSelectedSection()
    {
        _selectedFolded = !_selectedFolded;
        ApplySectionState();
    }

    private void ToggleListSection()
    {
        _listFolded = !_listFolded;
        ApplySectionState();
    }

    /// <summary>The header carries the chevron, so the folded state is readable without a tooltip.</summary>
    private void ApplySectionState()
    {
        SetSection(_selectedHeader, _selectedContent, "Selected Agent", _selectedFolded);
        SetSection(_listHeader, _listContent, _listHeadingText, _listFolded);
    }

    private static void SetSection(Button header, VisualElement content, string title, bool folded)
    {
        if (header != null)
            header.text = $"{(folded ? FoldedGlyph : ExpandedGlyph)}  {title}";

        content?.EnableInClassList("is-folded", folded);
    }

    // ==========================================
    //          SELECTED AGENT
    // ==========================================

    private void UpdateSelectedCard(Transform target)
    {
        _describedTarget = target;
        bool hasTarget = target != null;

        if (_selectedName != null)
            _selectedName.text = hasTarget ? GetAgentName(target) : "No agent";

        if (_selectedBadge != null)
            _selectedBadge.text = hasTarget ? "Selected" : "Nothing selected";

        if (_selectedIcon != null)
        {
            // The card icon follows the agent type; without a target the USS colour stands alone.
            Texture2D texture = hasTarget ? GetIconTexture(target.GetComponentInParent<Robot>() != null) : null;
            _selectedIcon.style.backgroundImage = texture != null ? new StyleBackground(texture) : StyleKeyword.None;
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
        RobotProfile profile = RobotProfileOf(robot);

        _info.Add(CreateInfoRow("Type", DescribeType(robot, human, profile)));

        Vector3 position;
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

        if (agent == null)
        {
            // Target without a known agent component: report what the transform alone can tell.
            _info.Add(CreateInfoRow("Orientation", FormatYaw(target.rotation.eulerAngles.y)));
            _info.Add(CreateInfoRow("Status", target.gameObject.activeInHierarchy ? "Active" : "Idle"));
            return;
        }

        _info.Add(CreateInfoRow("Orientation", FormatYaw(agent.Rotation.eulerAngles.y)));
        _info.Add(CreateInfoRow("Speed", $"{FormatNumber(agent.Speed)} m/s"));

        // The ceiling of the type sits next to the live figure, so a slow robot reads as that robot
        // being slow and not as the run being slow.
        if (profile != null)
            _info.Add(CreateInfoRow("Max speed", $"{FormatNumber(profile.MaxLinearSpeed)} m/s"));

        _info.Add(CreateInfoRow("Goal", agent.HasGoal ? FormatPlanar(agent.Goal) : "—"));
        _info.Add(CreateInfoRow("Goal reached", agent.HasGoal ? "No" : "Yes"));
        _info.Add(CreateInfoRow("Status", agent.IsActive ? "Active" : "Idle"));
    }

    // ==========================================
    //          AGENT LIST
    // ==========================================

    private void UpdateListHeader()
    {
        int robots = 0;
        int humans = 0;
        foreach (Transform target in _targets)
        {
            if (target == null) continue;
            if (target.GetComponentInParent<Robot>() != null) robots++;
            else if (target.GetComponentInParent<HumanAgent>() != null) humans++;
        }

        // The heading stays short: the panel is narrow, and the rows already say what each agent is.
        _listHeadingText = $"Agents List ({_targets.Count})";

        if (_listHeader != null)
        {
            _listHeader.text = $"{(_listFolded ? FoldedGlyph : ExpandedGlyph)}  {_listHeadingText}";

            if (robots > 0 || humans > 0)
                _listHeader.tooltip = $"{_targets.Count} agents — {robots} robot{Plural(robots)}, {humans} human{Plural(humans)}";
        }
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
        row.tooltip = "Select this agent for the camera view";
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

        // The whole row is the target: one click hands the camera to that agent, exactly like a
        // click on the agent in the scene view.
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

    // ==========================================
    //          HELPERS
    // ==========================================

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
        if (robot != null)
        {
            // A robot of a scenario is named by its type and its id - "TurtleBot 4 (robot_2)" - which is
            // what tells two of them apart in a list. A robot with no identity keeps the name it had.
            RobotIdentity identity = RobotIdentity.Of(robot);
            if (identity != null) return identity.DisplayName;

            if (!string.IsNullOrEmpty(robot.AgentName)) return robot.AgentName;
        }

        HumanAgent human = target.GetComponentInParent<HumanAgent>();
        if (human != null && !string.IsNullOrEmpty(human.AgentName)) return human.AgentName;

        return target.name;
    }

    /// <summary>
    /// What the card calls the selected agent: the readable name of a robot type when there is one -
    /// "TurtleBot 4" - and the plain kind otherwise. A pedestrian keeps the wording it has always had.
    /// </summary>
    private static string DescribeType(Robot robot, HumanAgent human, RobotProfile profile)
    {
        if (robot == null)
            return human != null ? "Human" : "Agent";

        return profile != null && !string.IsNullOrEmpty(profile.DisplayName) ? profile.DisplayName : "Robot";
    }

    /// <summary>
    /// The profile of a robot: the one its identity was bound with, and the copy the robot itself carries
    /// as the fallback for a robot without an identity, such as a hand-placed one.
    /// </summary>
    private static RobotProfile RobotProfileOf(Robot robot)
    {
        if (robot == null) return null;

        RobotIdentity identity = RobotIdentity.Of(robot);
        return identity != null && identity.Profile != null ? identity.Profile : robot.Profile;
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
