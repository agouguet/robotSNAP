using System;
using System.Collections.Generic;
using System.Linq;
using RobotSNAP.Agents;
using RobotSNAP.Core.Scenario;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Owns the multi-route editing state for the scenario wizard.
/// One human route can represent one or several humans sharing the same ordered goals.
/// The map is the primary editing surface: points are selected and dragged directly on it.
/// </summary>
public sealed class ScenarioRouteEditor
{
    private const string HiddenClass = "route-editor-hidden";
    private const float PointHitRadius = 16f;
    private const float RouteHitRadius = 9f;
    private const float DragThreshold = 2f;
    private const float FieldEditCoalesceSeconds = 1.5f;
    private const float PointLabelWidth = 78f;
    private const int UndoLimit = 60;

    private sealed class RouteDraft
    {
        public string Id;
        public bool IsRobot;
        public int Count;
        public float Speed = 1f;
        public HumanEndBehavior EndBehavior = HumanEndBehavior.Stay;
        public string Group;
        public string Formation = "pair";
        public float GroupSpacing = 1.5f;
        public readonly List<Vector2> Points = new();
        public HumanScenarioConfig Source;
        public bool RouteModified;
        public bool HasNonSpatialGoal;
    }

    private sealed class RouteSnapshot
    {
        public string Id;
        public bool IsRobot;
        public int Count;
        public float Speed;
        public HumanEndBehavior EndBehavior;
        public string Group;
        public string Formation;
        public float GroupSpacing;
        public List<Vector2> Points;
        public HumanScenarioConfig Source;
        public bool RouteModified;
        public bool HasNonSpatialGoal;
    }

    private sealed class EditorSnapshot
    {
        public List<RouteSnapshot> Routes;
        public int ActiveRouteIndex;
        public int PendingPointIndex;
    }

    private struct CoordinateFields
    {
        public FloatField X;
        public FloatField Z;
    }

    private static readonly Color RobotColor = new(0.22f, 0.78f, 0.45f);
    private static readonly Color[] HumanColors =
    {
        new(0.22f, 0.57f, 0.92f),
        new(0.66f, 0.38f, 0.91f),
        new(0.94f, 0.49f, 0.27f),
        new(0.20f, 0.76f, 0.76f),
        new(0.89f, 0.36f, 0.60f)
    };

    private static readonly string[] EndBehaviorChoices =
    {
        HumanEndBehaviorParser.ToDisplayName(HumanEndBehavior.Stay),
        HumanEndBehaviorParser.ToDisplayName(HumanEndBehavior.Disappear),
        HumanEndBehaviorParser.ToDisplayName(HumanEndBehavior.Loop)
    };

    private static readonly string[] FormationChoices =
    {
        "Pair",
        "Row",
        "Column",
        "Wedge",
        "Cluster"
    };

    private readonly List<RouteDraft> _routes = new();
    private readonly List<EditorSnapshot> _undoStack = new();
    private readonly List<EditorSnapshot> _redoStack = new();
    private readonly List<VisualElement> _pointRows = new();
    private readonly Dictionary<int, CoordinateFields> _pointFields = new();
    private readonly VisualElement _routeList;
    private readonly VisualElement _groupList;
    private readonly Button _addHumanRouteButton;
    private readonly Button _removeHumanRouteButton;
    private readonly Button _setRouteStartButton;
    private readonly Button _addRouteObjectiveButton;
    private readonly Button _toggleMapGridButton;
    private readonly VisualElement _robotRouteSettings;
    private readonly VisualElement _humanRouteSettings;
    private readonly IntegerField _humanCountField;
    private readonly FloatField _humanSpeedField;
    private readonly DropdownField _endBehaviorDropdown;
    private readonly TextField _groupField;
    private readonly DropdownField _formationDropdown;
    private readonly FloatField _groupSpacingField;
    private readonly Label _formationPreviewLabel;
    private readonly VisualElement _routePointsContainer;
    private readonly VisualElement _canvas;
    private readonly Image _mapImage;
    private readonly Label _mapPlaceholder;
    private readonly Label _instructionLabel;
    private readonly Label _cursorCoordinatesLabel;
    private readonly Label _activeRouteLabel;
    private readonly Label _gridScaleLabel;
    private readonly OccupancyMapRouteOverlay _overlay;
    private readonly VisualElement _pointLabelLayer;
    private readonly List<OccupancyMapRouteOverlay.FormationPreview> _formationPreviews = new();

    private Texture2D _mapTexture;
    private Bounds _mapBounds;
    private int _activeRouteIndex;
    private int _pendingPointIndex;
    private bool _updatingFields;
    private bool _showGrid = true;

    private bool _dragging;
    private bool _dragActive;
    private int _dragRouteIndex = -1;
    private int _dragPointIndex = -1;
    private Vector2 _dragOrigin;
    private Vector2 _dragStartWorld;
    private EditorSnapshot _dragUndoSnapshot;
    private bool _skipNextDragUndo;

    private string _lastEditKey;
    private float _lastEditTime = float.NegativeInfinity;

    public ScenarioRouteEditor(VisualElement root)
    {
        _routeList = root.Q<VisualElement>("RouteSelectorList");
        _groupList = root.Q<VisualElement>("GroupList");
        _addHumanRouteButton = root.Q<Button>("AddHumanRouteButton");
        _removeHumanRouteButton = root.Q<Button>("RemoveHumanRouteButton");
        _setRouteStartButton = root.Q<Button>("SetRouteStartButton");
        _addRouteObjectiveButton = root.Q<Button>("AddRouteObjectiveButton");
        _toggleMapGridButton = root.Q<Button>("ToggleMapGridButton");
        _robotRouteSettings = root.Q<VisualElement>("RobotRouteSettings");
        _humanRouteSettings = root.Q<VisualElement>("HumanRouteSettings");
        _humanCountField = root.Q<IntegerField>("HumanCountField");
        _humanSpeedField = root.Q<FloatField>("HumanSpeedField");
        _endBehaviorDropdown = root.Q<DropdownField>("EndBehaviorDropdown");
        _groupField = root.Q<TextField>("GroupField");
        _formationDropdown = root.Q<DropdownField>("FormationDropdown");
        _groupSpacingField = root.Q<FloatField>("GroupSpacingField");
        _formationPreviewLabel = root.Q<Label>("FormationPreviewLabel");
        _routePointsContainer = root.Q<VisualElement>("RoutePointsContainer");
        _canvas = root.Q<VisualElement>("AgentsEnvironmentCanvas");
        _mapImage = root.Q<Image>("AgentsEnvironmentImage");
        _mapPlaceholder = root.Q<Label>("AgentsMapPlaceholder");
        _instructionLabel = root.Q<Label>("PlacementInstructionLabel");
        _cursorCoordinatesLabel = root.Q<Label>("MapCursorCoordinatesLabel");
        _activeRouteLabel = root.Q<Label>("ActiveRouteLabel");
        _gridScaleLabel = root.Q<Label>("GridScaleLabel");
        VisualElement overlayHost = root.Q<VisualElement>("RouteOverlayHost");

        if (new VisualElement[]
            {
                _routeList, _groupList, _addHumanRouteButton, _removeHumanRouteButton,
                _setRouteStartButton, _addRouteObjectiveButton, _toggleMapGridButton,
                _robotRouteSettings, _humanRouteSettings, _humanCountField, _humanSpeedField,
                _endBehaviorDropdown, _groupField, _formationDropdown, _groupSpacingField,
                _formationPreviewLabel,
                _routePointsContainer, _canvas, _mapImage, _mapPlaceholder, _instructionLabel,
                _cursorCoordinatesLabel, _activeRouteLabel, _gridScaleLabel, overlayHost
            }.Any(element => element == null))
        {
            throw new InvalidOperationException("The scenario route editor UI is incomplete.");
        }

        _overlay = new OccupancyMapRouteOverlay();
        overlayHost.Add(_overlay);

        _pointLabelLayer = new VisualElement { pickingMode = PickingMode.Ignore };
        _pointLabelLayer.AddToClassList("route-point-layer");
        overlayHost.Add(_pointLabelLayer);

        _mapImage.scaleMode = ScaleMode.ScaleToFit;
        _canvas.focusable = true;
        _canvas.AddManipulator(new OccupancyMapPlacementManipulator(
            OnMapPointerDown,
            OnMapPointerMove,
            OnMapPointerLeave,
            OnMapPointerUp));
        _canvas.RegisterCallback<KeyDownEvent>(OnCanvasKeyDown);
        _canvas.RegisterCallback<GeometryChangedEvent>(_ => RefreshOverlay());

        _addHumanRouteButton.clicked += AddHumanRoute;
        _removeHumanRouteButton.clicked += RemoveActiveHumanRoute;
        _setRouteStartButton.clicked += SelectStartForPlacement;
        _addRouteObjectiveButton.clicked += AddObjective;
        _toggleMapGridButton.clicked += ToggleGrid;

        _humanCountField.RegisterValueChangedCallback(evt =>
        {
            int value = Mathf.Max(0, evt.newValue);
            if (value != evt.newValue) _humanCountField.SetValueWithoutNotify(value);
            if (_updatingFields) return;
            PushUndo($"human-count:{_activeRouteIndex}");
            UpdateHumanDraft(draft => draft.Count = value);
            RefreshRouteList();
        });
        _humanSpeedField.RegisterValueChangedCallback(evt =>
        {
            float value = Mathf.Max(0.01f, evt.newValue);
            if (!Mathf.Approximately(value, evt.newValue)) _humanSpeedField.SetValueWithoutNotify(value);
            if (_updatingFields) return;
            PushUndo($"human-speed:{_activeRouteIndex}");
            UpdateHumanDraft(draft => draft.Speed = value);
        });
        _endBehaviorDropdown.RegisterValueChangedCallback(evt =>
        {
            if (_updatingFields) return;
            PushUndo($"human-end-behavior:{_activeRouteIndex}");
            UpdateHumanDraft(draft => draft.EndBehavior = ParseEndBehaviorChoice(evt.newValue));
        });
        _groupField.RegisterValueChangedCallback(evt =>
        {
            if (_updatingFields) return;
            PushUndo($"human-group:{_activeRouteIndex}");
            UpdateHumanDraft(draft => draft.Group = evt.newValue);
            ApplyGroupLayoutToPeers(ActiveRoute);
            RefreshGroupList();
        });
        _formationDropdown.RegisterValueChangedCallback(evt =>
        {
            if (_updatingFields) return;
            PushUndo($"human-formation:{_activeRouteIndex}");
            UpdateHumanDraft(draft => draft.Formation = FormationFromDisplay(evt.newValue));
            ApplyGroupLayoutToPeers(ActiveRoute);
            RefreshGroupList();
        });
        _groupSpacingField.RegisterValueChangedCallback(evt =>
        {
            float value = Mathf.Clamp(evt.newValue, 0.4f, 3f);
            if (!Mathf.Approximately(value, evt.newValue)) _groupSpacingField.SetValueWithoutNotify(value);
            if (_updatingFields) return;
            PushUndo($"human-group-spacing:{_activeRouteIndex}");
            UpdateHumanDraft(draft => draft.GroupSpacing = value);
            ApplyGroupLayoutToPeers(ActiveRoute);
            RefreshGroupList();
        });
    }

    public int TotalHumanCount => _routes.Where(route => !route.IsRobot).Sum(route => Mathf.Max(0, route.Count));
    public int TotalObjectiveCount => _routes
        .Where(route => route.IsRobot || route.Count > 0)
        .Sum(route => Mathf.Max(0, route.Points.Count - 1));
    public int HumanRouteCount => _routes.Count(route => !route.IsRobot && route.Count > 0);

    public string RobotRouteSummary
    {
        get
        {
            RouteDraft robot = _routes.FirstOrDefault(route => route.IsRobot);
            return robot == null ? "No robot route" : FormatRouteSummary(robot);
        }
    }

    public string HumanRouteSummary
    {
        get
        {
            List<RouteDraft> humans = _routes.Where(route => !route.IsRobot && route.Count > 0).ToList();
            return humans.Count == 0
                ? "No human route"
                : $"{humans.Count} route(s), {humans.Sum(route => route.Points.Count - 1)} objective(s)";
        }
    }

    /// <summary>
    /// Every route as a drawable visual, for read-only previews such as the validation step recap.
    /// All routes are marked active so the recap draws them with the full opacity used while editing.
    /// </summary>
    public List<OccupancyMapRouteOverlay.RouteVisual> BuildRoutePreviews()
    {
        var previews = new List<OccupancyMapRouteOverlay.RouteVisual>(_routes.Count);
        for (int index = 0; index < _routes.Count; index++)
            previews.Add(new OccupancyMapRouteOverlay.RouteVisual(
                _routes[index].Points,
                RouteColor(index),
                true,
                _routes[index].Id));
        return previews;
    }

    /// <summary>
    /// Every route as plain data, for the dry run of the validation step.
    /// The robot speed lives in the tab controller, so it is passed in for the robot route.
    /// </summary>
    public List<ScenarioDryRun.Route> BuildDryRunRoutes(float robotSpeed)
    {
        var routes = new List<ScenarioDryRun.Route>(_routes.Count);
        foreach (RouteDraft draft in _routes)
        {
            bool skipped = !draft.IsRobot && draft.Count <= 0;
            if (skipped)
                continue;
            routes.Add(new ScenarioDryRun.Route(
                draft.Id,
                draft.Points,
                draft.IsRobot ? robotSpeed : draft.Speed));
        }
        return routes;
    }

    public void Reset()
    {
        _routes.Clear();
        _routes.Add(new RouteDraft
        {
            Id = "Robot route",
            IsRobot = true,
            RouteModified = true,
            Points = { Vector2.zero, new Vector2(2f, 0f) }
        });
        _routes.Add(CreateHumanDraft(1));
        _activeRouteIndex = 0;
        _pendingPointIndex = 0;
        ResetHistory();
        RebuildRouteList();
        RefreshActiveRoute();
    }

    public void Load(ScenarioData scenario)
    {
        Reset();
        if (scenario == null)
            return;

        RouteDraft robot = _routes[0];
        robot.Points.Clear();
        if (TryResolveReference(scenario, scenario.Robot?.StartRef, out Vector2 robotStart))
            robot.Points.Add(robotStart);
        if (scenario.Robot?.WaypointRefs != null)
        {
            foreach (string waypointRef in scenario.Robot.WaypointRefs)
                if (TryResolveReference(scenario, waypointRef, out Vector2 waypoint)) robot.Points.Add(waypoint);
        }
        if (TryResolveReference(scenario, scenario.Robot?.GoalRef, out Vector2 robotGoal))
            robot.Points.Add(robotGoal);
        EnsureMinimumPoints(robot);
        robot.RouteModified = false;

        _routes.RemoveRange(1, _routes.Count - 1);
        if (scenario.Humans != null)
        {
            foreach (HumanScenarioConfig human in scenario.Humans.Where(human => human != null))
            {
                RouteDraft draft = CreateHumanDraft(scenario, human, _routes.Count);
                draft.Id = MakeUniqueRouteId(draft.Id);
                _routes.Add(draft);
            }
        }
        if (_routes.Count == 1)
            _routes.Add(CreateHumanDraft(1));

        _activeRouteIndex = 0;
        _pendingPointIndex = 0;
        ResetHistory();
        RebuildRouteList();
        RefreshActiveRoute();
    }

    public void SetMap(Texture2D texture, Bounds bounds)
    {
        _mapTexture = texture;
        _mapBounds = bounds;
        _mapImage.image = texture;
        _mapPlaceholder.EnableInClassList(HiddenClass, texture != null);
        RefreshOverlay();
    }

    public bool Validate(out string error)
    {
        error = null;
        RouteDraft robot = _routes.FirstOrDefault(route => route.IsRobot);
        if (robot == null || robot.Points.Count < 2)
            error = "The robot route needs a start point and at least one objective.";
        else if (HasRepeatedConsecutivePoint(robot))
            error = "Consecutive robot route points must be different.";
        else
        {
            foreach (RouteDraft human in _routes.Where(route => !route.IsRobot && route.Count > 0))
            {
                if (human.Speed <= 0f)
                {
                    error = $"{human.Id} speed must be greater than zero.";
                    break;
                }
                if (human.Points.Count < 2 && !(human.HasNonSpatialGoal && !human.RouteModified))
                {
                    error = $"{human.Id} needs a start point and at least one objective.";
                    break;
                }
                if (HasRepeatedConsecutivePoint(human) && !(human.HasNonSpatialGoal && !human.RouteModified))
                {
                    error = $"Consecutive points in {human.Id} must be different.";
                    break;
                }
            }
        }

        return error == null;
    }

    public void WriteToScenario(ScenarioData scenario)
    {
        scenario.Points ??= new Dictionary<string, RefPoint>();
        scenario.Robot ??= new RobotScenarioConfig();
        HashSet<string> previousRouteReferences = CollectRouteReferences(scenario);

        RouteDraft robot = _routes.First(route => route.IsRobot);
        string startRef = string.IsNullOrWhiteSpace(scenario.Robot.StartRef) ? "robot_start" : scenario.Robot.StartRef;
        string goalRef = string.IsNullOrWhiteSpace(scenario.Robot.GoalRef) ? "robot_goal" : scenario.Robot.GoalRef;
        WritePoint(scenario, startRef, robot.Points[0]);
        WritePoint(scenario, goalRef, robot.Points[^1]);
        scenario.Robot.StartRef = startRef;
        scenario.Robot.GoalRef = goalRef;

        var waypointRefs = new List<string>();
        for (int index = 1; index < robot.Points.Count - 1; index++)
        {
            string reference = scenario.Robot.WaypointRefs != null && index - 1 < scenario.Robot.WaypointRefs.Count
                ? scenario.Robot.WaypointRefs[index - 1]
                : $"robot_waypoint_{index}";
            waypointRefs.Add(reference);
            WritePoint(scenario, reference, robot.Points[index]);
        }
        scenario.Robot.WaypointRefs = waypointRefs.Count > 0 ? waypointRefs : null;

        NormalizeGroupLayouts();

        var humanConfigs = new List<HumanScenarioConfig>();
        int humanIndex = 1;
        foreach (RouteDraft draft in _routes.Where(route => !route.IsRobot && route.Count > 0))
        {
            HumanScenarioConfig config = draft.Source ?? new HumanScenarioConfig();
            config.Id = string.IsNullOrWhiteSpace(config.Id) ? $"human_route_{humanIndex}" : config.Id;
            config.Count = draft.Count;
            config.Speed = draft.Speed;
            config.EndBehavior = HumanEndBehaviorParser.ToYamlValue(draft.EndBehavior);
            config.Group = string.IsNullOrWhiteSpace(draft.Group) ? null : draft.Group.Trim();
            config.Spawn ??= new SpawnConfig();
            config.Spawn.Formation = string.IsNullOrWhiteSpace(draft.Formation) ? "pair" : draft.Formation.Trim().ToLowerInvariant();
            config.Spawn.Spacing = Mathf.Max(0.4f, draft.GroupSpacing);

            if (draft.Source == null || draft.RouteModified)
            {
                EnsureMinimumPoints(draft);
                config.Spawn ??= new SpawnConfig();
                config.Goal ??= new GoalConfig();
                string safeId = ToFileId(config.Id);
                SetSpawnReference(config.Spawn, $"{safeId}_start");
                SetGoalReference(config.Goal, $"{safeId}_goal_1");
                WritePoint(scenario, config.Spawn.Reference, draft.Points[0]);
                WritePoint(scenario, config.Goal.Reference, draft.Points[1]);

                var additionalGoals = new List<GoalConfig>();
                for (int goalIndex = 2; goalIndex < draft.Points.Count; goalIndex++)
                {
                    GoalConfig goal = config.Goals != null && goalIndex - 2 < config.Goals.Count
                        ? config.Goals[goalIndex - 2]
                        : new GoalConfig();
                    SetGoalReference(goal, $"{safeId}_goal_{goalIndex}");
                    WritePoint(scenario, goal.Reference, draft.Points[goalIndex]);
                    additionalGoals.Add(goal);
                }
                config.Goals = additionalGoals.Count > 0 ? additionalGoals : null;
            }

            humanConfigs.Add(config);
            humanIndex++;
        }
        scenario.Humans = humanConfigs;

        HashSet<string> activeRouteReferences = CollectRouteReferences(scenario);
        foreach (string staleReference in previousRouteReferences.Except(activeRouteReferences))
            scenario.Points.Remove(staleReference);
    }

    /// <summary>
    /// Writes one formation and one spacing per group: the first route of a group defines the layout
    /// and every other route of the same group is aligned on it before the YAML is emitted.
    /// </summary>
    private void NormalizeGroupLayouts()
    {
        var layouts = new Dictionary<string, RouteDraft>(StringComparer.OrdinalIgnoreCase);
        foreach (RouteDraft route in _routes.Where(route => !route.IsRobot && route.Count > 0))
        {
            string groupId = route.Group?.Trim();
            if (string.IsNullOrEmpty(groupId))
                continue;

            if (layouts.TryGetValue(groupId, out RouteDraft reference))
            {
                route.Formation = reference.Formation;
                route.GroupSpacing = reference.GroupSpacing;
            }
            else
            {
                layouts[groupId] = route;
            }
        }
    }

    private void AddHumanRoute()
    {
        PushUndo();
        RouteDraft draft = CreateHumanDraft(NextHumanRouteNumber());
        _routes.Add(draft);
        _activeRouteIndex = _routes.Count - 1;
        _pendingPointIndex = 0;
        RebuildRouteList();
        RefreshActiveRoute();
    }

    private void RemoveActiveHumanRoute()
    {
        RouteDraft active = ActiveRoute;
        if (active == null || active.IsRobot)
            return;

        PushUndo();
        _routes.RemoveAt(_activeRouteIndex);
        if (_routes.Count == 1)
            _routes.Add(CreateHumanDraft(1));
        _activeRouteIndex = Mathf.Clamp(_activeRouteIndex - 1, 0, _routes.Count - 1);
        _pendingPointIndex = 0;
        RebuildRouteList();
        RefreshActiveRoute();
    }

    private void AddObjective()
    {
        RouteDraft active = ActiveRoute;
        if (active == null)
            return;

        PushUndo();
        Vector2 anchor = active.Points.Count > 0 ? active.Points[^1] : Vector2.zero;
        active.Points.Add(anchor + Vector2.right);
        active.RouteModified = true;
        active.HasNonSpatialGoal = false;
        _pendingPointIndex = active.Points.Count - 1;
        RebuildPointRows();
        SetPlacementInstruction();
        RefreshOverlay();
    }

    private void SelectStartForPlacement()
    {
        _pendingPointIndex = 0;
        UpdatePointSelectionHighlight();
        SetPlacementInstruction();
        RefreshOverlay();
    }

    private void ToggleGrid()
    {
        _showGrid = !_showGrid;
        _toggleMapGridButton.EnableInClassList("active", _showGrid);
        _overlay.ShowGrid = _showGrid;
        UpdateGridScaleLabel();
    }

    private void SelectRoute(int index)
    {
        if (index < 0 || index >= _routes.Count || index == _activeRouteIndex)
            return;
        _activeRouteIndex = index;
        _pendingPointIndex = 0;
        RefreshActiveRoute();
    }

    private void RefreshActiveRoute()
    {
        RouteDraft active = ActiveRoute;
        if (active == null)
            return;

        _activeRouteLabel.text = active.Id;
        _removeHumanRouteButton.SetEnabled(!active.IsRobot);
        _robotRouteSettings.EnableInClassList(HiddenClass, !active.IsRobot);
        _humanRouteSettings.EnableInClassList(HiddenClass, active.IsRobot);
        _pendingPointIndex = Mathf.Clamp(_pendingPointIndex, 0, Mathf.Max(0, active.Points.Count - 1));

        _updatingFields = true;
        if (!active.IsRobot)
        {
            _humanCountField.SetValueWithoutNotify(active.Count);
            _humanSpeedField.SetValueWithoutNotify(active.Speed);
            SetEndBehaviorChoices();
            _endBehaviorDropdown.SetValueWithoutNotify(HumanEndBehaviorParser.ToDisplayName(active.EndBehavior));
            _groupField.SetValueWithoutNotify(active.Group ?? string.Empty);
            SetFormationChoices();
            _formationDropdown.SetValueWithoutNotify(FormationToDisplay(active.Formation));
            _groupSpacingField.SetValueWithoutNotify(active.GroupSpacing);
        }
        _updatingFields = false;

        RefreshRouteListSelection();
        RebuildPointRows();
        SetPlacementInstruction();
        RefreshOverlay();
    }

    private void RebuildRouteList()
    {
        _routeList.Clear();
        for (int index = 0; index < _routes.Count; index++)
        {
            int capturedIndex = index;
            RouteDraft route = _routes[index];

            var row = new VisualElement();
            row.AddToClassList("route-selector-row");
            row.EnableInClassList("selected", index == _activeRouteIndex);

            var swatch = new VisualElement();
            swatch.AddToClassList("route-selector-swatch");
            swatch.style.backgroundColor = RouteColor(index);

            var name = new Label(DescribeRoute(route));
            name.AddToClassList("route-selector-name");

            row.Add(swatch);
            row.Add(name);
            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                SelectRoute(capturedIndex);
                evt.StopPropagation();
            });
            _routeList.Add(row);
        }
        RebuildGroupList();
    }

    /// <summary>Recomputes the group rows, then the sentence describing the active formation.</summary>
    private void RefreshGroupList()
    {
        RebuildGroupList();
        UpdateFormationPreviewLabel();
    }

    private void RefreshRouteList()
    {
        for (int index = 0; index < _routeList.childCount && index < _routes.Count; index++)
        {
            if (_routeList[index].childCount < 2)
                continue;
            var name = _routeList[index][1] as Label;
            if (name != null)
                name.text = DescribeRoute(_routes[index]);
        }
        RebuildGroupList();
    }

    private void RefreshRouteListSelection()
    {
        for (int index = 0; index < _routeList.childCount && index < _routes.Count; index++)
            _routeList[index].EnableInClassList("selected", index == _activeRouteIndex);
        RefreshGroupListSelection();
    }

    /// <summary>
    /// One row per group id used by the human routes. The group is where formation and spacing really
    /// live, so it gets its own list instead of being buried in each route's settings.
    /// </summary>
    private void RebuildGroupList()
    {
        _groupList.Clear();
        List<string> groups = CollectGroupIds();
        if (groups.Count == 0)
        {
            var hint = new Label("No group yet. Set a group id on a human route to make agents walk together.");
            hint.AddToClassList("group-empty-hint");
            _groupList.Add(hint);
            return;
        }

        foreach (string groupId in groups)
        {
            string capturedId = groupId;
            var row = new VisualElement();
            row.AddToClassList("group-row");
            row.EnableInClassList("selected", IsActiveRouteInGroup(groupId));

            var main = new VisualElement();
            main.AddToClassList("group-row-main");
            var name = new Label(groupId);
            name.AddToClassList("group-row-name");
            var detail = new Label(DescribeGroup(groupId));
            detail.AddToClassList("group-row-detail");
            main.Add(name);
            main.Add(detail);

            var ungroup = new Button(() => UngroupAll(capturedId)) { text = "×" };
            ungroup.AddToClassList("group-row-ungroup");
            ungroup.tooltip = "Remove this group id from every route";

            row.Add(main);
            row.Add(ungroup);
            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                SelectFirstRouteOfGroup(capturedId);
                evt.StopPropagation();
            });
            _groupList.Add(row);
        }
    }

    private void RefreshGroupListSelection()
    {
        List<string> groups = CollectGroupIds();
        for (int index = 0; index < _groupList.childCount && index < groups.Count; index++)
            _groupList[index].EnableInClassList("selected", IsActiveRouteInGroup(groups[index]));
    }

    /// <summary>Group ids in first-seen route order, so the list stays stable while editing.</summary>
    private List<string> CollectGroupIds()
    {
        var groups = new List<string>();
        foreach (RouteDraft route in _routes)
        {
            string groupId = route.IsRobot ? null : route.Group?.Trim();
            if (string.IsNullOrEmpty(groupId) || groups.Contains(groupId))
                continue;
            groups.Add(groupId);
        }
        return groups;
    }

    private List<RouteDraft> RoutesOfGroup(string groupId) => _routes
        .Where(route => !route.IsRobot &&
                        string.Equals(route.Group?.Trim(), groupId, StringComparison.OrdinalIgnoreCase))
        .ToList();

    private bool IsActiveRouteInGroup(string groupId)
    {
        RouteDraft active = ActiveRoute;
        return active != null && !active.IsRobot &&
               string.Equals(active.Group?.Trim(), groupId, StringComparison.OrdinalIgnoreCase);
    }

    private string DescribeGroup(string groupId)
    {
        List<RouteDraft> routes = RoutesOfGroup(groupId);
        int agents = routes.Sum(route => Mathf.Max(0, route.Count));
        RouteDraft reference = routes.Count > 0 ? routes[0] : null;
        string formation = reference != null
            ? FormationToDisplay(reference.Formation).ToLowerInvariant()
            : "pair";
        float spacing = reference != null ? reference.GroupSpacing : 1.5f;
        return $"{agents} agent{(agents == 1 ? string.Empty : "s")} · {routes.Count} route" +
               $"{(routes.Count == 1 ? string.Empty : "s")} · {formation} · {spacing:0.##} m";
    }

    private void SelectFirstRouteOfGroup(string groupId)
    {
        int index = _routes.FindIndex(route =>
            !route.IsRobot &&
            string.Equals(route.Group?.Trim(), groupId, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index == _activeRouteIndex)
            return;

        SelectRoute(index);
    }

    /// <summary>Detaches every route of a group, which is how a group is removed.</summary>
    private void UngroupAll(string groupId)
    {
        List<RouteDraft> routes = RoutesOfGroup(groupId);
        if (routes.Count == 0)
            return;

        PushUndo();
        foreach (RouteDraft route in routes)
            route.Group = null;

        RefreshActiveRoute();
        _instructionLabel.text = $"Group '{groupId}' removed from every route.";
    }

    private static string DescribeRoute(RouteDraft route)
    {
        if (route.IsRobot)
            return "Robot route";
        int agents = Mathf.Max(0, route.Count);
        return $"{route.Id} - {agents} agent{(agents == 1 ? string.Empty : "s")}";
    }

    private void RebuildPointRows()
    {
        _routePointsContainer.Clear();
        _pointRows.Clear();
        _pointFields.Clear();
        RouteDraft active = ActiveRoute;
        if (active == null)
            return;

        for (int index = 0; index < active.Points.Count; index++)
        {
            int capturedIndex = index;
            Vector2 point = active.Points[index];
            var row = new VisualElement();
            row.AddToClassList("route-point-row");
            row.EnableInClassList("selected", index == _pendingPointIndex);

            var header = new VisualElement();
            header.AddToClassList("route-point-row-header");
            var name = new Label(RouteMapHitTesting.PointLabel(index));
            name.AddToClassList("route-point-name");
            header.Add(name);
            row.Add(header);

            var coordinates = new VisualElement();
            coordinates.AddToClassList("route-point-coordinates");
            FloatField xField = CreateCoordinateField("X", point.x);
            FloatField zField = CreateCoordinateField("Z", point.y);
            xField.RegisterValueChangedCallback(evt => SetPointCoordinate(capturedIndex, true, evt.newValue));
            zField.RegisterValueChangedCallback(evt => SetPointCoordinate(capturedIndex, false, evt.newValue));
            coordinates.Add(xField);
            coordinates.Add(zField);
            row.Add(coordinates);
            _pointFields[index] = new CoordinateFields { X = xField, Z = zField };

            if (index > 0)
            {
                var actions = new VisualElement();
                actions.AddToClassList("route-point-actions");
                Button up = CreateSmallButton("↑", () => MovePoint(capturedIndex, -1));
                Button down = CreateSmallButton("↓", () => MovePoint(capturedIndex, 1));
                Button remove = CreateSmallButton("×", () => RemovePoint(capturedIndex));
                up.SetEnabled(index > 1);
                down.SetEnabled(index < active.Points.Count - 1);
                remove.SetEnabled(active.Points.Count > 2);
                actions.Add(up);
                actions.Add(down);
                actions.Add(remove);
                row.Add(actions);
            }

            row.RegisterCallback<PointerDownEvent>(_ => SelectPoint(capturedIndex));
            _pointRows.Add(row);
            _routePointsContainer.Add(row);
        }
    }

    private void UpdatePointSelectionHighlight()
    {
        for (int index = 0; index < _pointRows.Count; index++)
            _pointRows[index].EnableInClassList("selected", index == _pendingPointIndex);
    }

    private static FloatField CreateCoordinateField(string label, float value)
    {
        var field = new FloatField(label) { value = RoundCoordinate(value) };
        field.AddToClassList("creation-text-field");
        field.AddToClassList("route-coordinate-field");
        return field;
    }

    private static Button CreateSmallButton(string text, Action clicked)
    {
        var button = new Button(clicked) { text = text };
        button.AddToClassList("route-point-small-button");
        return button;
    }

    private void SelectPoint(int index)
    {
        RouteDraft active = ActiveRoute;
        if (active == null || index < 0 || index >= active.Points.Count)
            return;
        _pendingPointIndex = index;
        UpdatePointSelectionHighlight();
        SetPlacementInstruction();
        RefreshOverlay();
    }

    private void SetPointCoordinate(int index, bool isX, float value)
    {
        RouteDraft active = ActiveRoute;
        if (active == null || index < 0 || index >= active.Points.Count)
            return;
        value = RoundCoordinate(value);
        Vector2 point = active.Points[index];
        if (Mathf.Abs((isX ? point.x : point.y) - value) < 0.0001f)
            return;
        PushUndo($"coordinate:{_activeRouteIndex}:{index}:{(isX ? "x" : "z")}");
        active.Points[index] = isX ? new Vector2(value, point.y) : new Vector2(point.x, value);
        active.RouteModified = true;
        active.HasNonSpatialGoal = false;
        RefreshOverlay();
    }

    private void MovePoint(int index, int direction)
    {
        RouteDraft active = ActiveRoute;
        int targetIndex = index + direction;
        if (active == null || index <= 0 || targetIndex <= 0 || targetIndex >= active.Points.Count)
            return;
        PushUndo();
        (active.Points[index], active.Points[targetIndex]) = (active.Points[targetIndex], active.Points[index]);
        active.RouteModified = true;
        _pendingPointIndex = targetIndex;
        RebuildPointRows();
        RefreshOverlay();
    }

    private void RemovePoint(int index)
    {
        RouteDraft active = ActiveRoute;
        if (active == null || index <= 0 || active.Points.Count <= 2)
        {
            _instructionLabel.text = "The start point and one objective must stay on the route.";
            return;
        }
        PushUndo();
        active.Points.RemoveAt(index);
        active.RouteModified = true;
        _pendingPointIndex = Mathf.Clamp(_pendingPointIndex, 0, active.Points.Count - 1);
        RebuildPointRows();
        SetPlacementInstruction();
        RefreshOverlay();
    }

    private void UpdateHumanDraft(Action<RouteDraft> update)
    {
        if (_updatingFields || ActiveRoute == null || ActiveRoute.IsRobot)
            return;
        update(ActiveRoute);
    }

    private void SetEndBehaviorChoices()
    {
        if (_endBehaviorDropdown.choices.Count == EndBehaviorChoices.Length &&
            !_endBehaviorDropdown.choices.Where((choice, index) => choice != EndBehaviorChoices[index]).Any())
            return;
        _endBehaviorDropdown.choices = new List<string>(EndBehaviorChoices);
    }

    private void SetFormationChoices()
    {
        if (_formationDropdown.choices.Count == FormationChoices.Length &&
            !_formationDropdown.choices.Where((choice, index) => choice != FormationChoices[index]).Any())
            return;
        _formationDropdown.choices = new List<string>(FormationChoices);
    }

    /// <summary>
    /// The formation dropdown exposes display labels while the YAML uses short values,
    /// so the two mappings are explicit and unknown values fall back to the default pair.
    /// </summary>
    private static string FormationToDisplay(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            string formation = value.Trim().ToLowerInvariant();
            if (formation == "row" || formation == "abreast" || formation == "side-by-side") return "Row";
            if (formation == "column") return "Column";
            if (formation == "wedge") return "Wedge";
            if (formation == "cluster") return "Cluster";
        }

        return "Pair";
    }

    private static string FormationFromDisplay(string display)
    {
        if (!string.IsNullOrWhiteSpace(display))
        {
            string formation = display.Trim().ToLowerInvariant();
            if (formation == "row" || formation == "abreast" || formation == "side-by-side") return "row";
            if (formation == "column") return "column";
            if (formation == "wedge") return "wedge";
            if (formation == "cluster") return "cluster";
        }

        return "pair";
    }

    /// <summary>
    /// A formation is a property of the group, not of one route: routes sharing a group id keep the
    /// same layout so the YAML never mixes a wedge with a column inside one walking group.
    /// </summary>
    private void ApplyGroupLayoutToPeers(RouteDraft source)
    {
        if (source == null || source.IsRobot)
            return;

        string groupId = source.Group?.Trim();
        if (string.IsNullOrEmpty(groupId))
            return;

        foreach (RouteDraft route in _routes)
        {
            if (route == null || route.IsRobot || route == source)
                continue;
            if (!string.Equals(route.Group?.Trim(), groupId, StringComparison.OrdinalIgnoreCase))
                continue;

            route.Formation = source.Formation;
            route.GroupSpacing = source.GroupSpacing;
        }
    }

    /// <summary>
    /// The dropdown exposes display labels while the YAML uses short values, so the label is
    /// mapped explicitly and the shared parser stays the fallback for anything else.
    /// </summary>
    private static HumanEndBehavior ParseEndBehaviorChoice(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            string label = value.Trim();
            if (string.Equals(label, HumanEndBehaviorParser.ToDisplayName(HumanEndBehavior.Disappear), StringComparison.OrdinalIgnoreCase))
                return HumanEndBehavior.Disappear;
            if (string.Equals(label, HumanEndBehaviorParser.ToDisplayName(HumanEndBehavior.Loop), StringComparison.OrdinalIgnoreCase))
                return HumanEndBehavior.Loop;
            if (string.Equals(label, HumanEndBehaviorParser.ToDisplayName(HumanEndBehavior.Stay), StringComparison.OrdinalIgnoreCase))
                return HumanEndBehavior.Stay;
        }

        return HumanEndBehaviorParser.Parse(value);
    }

    private void OnMapPointerDown(MapPointerState pointer)
    {
        Vector2 localPosition = pointer.LocalPosition;
        _canvas.Focus();
        if (!TryLocalToWorld(localPosition, out Vector2 worldPosition))
            return;

        if (TrySelectPointAt(localPosition))
            return;

        if (TrySelectRouteAt(localPosition))
            return;

        RouteDraft active = ActiveRoute;
        if (active == null || active.Points.Count == 0)
            return;

        _pendingPointIndex = Mathf.Clamp(_pendingPointIndex, 0, active.Points.Count - 1);
        Vector2 placed = ApplyPointerConstraints(worldPosition, pointer, active.Points[_pendingPointIndex]);
        PushUndo();
        active.Points[_pendingPointIndex] = new Vector2(
            RoundCoordinate(placed.x),
            RoundCoordinate(placed.y));
        active.RouteModified = true;
        active.HasNonSpatialGoal = false;
        RebuildPointRows();
        SetPlacementInstruction();
        RefreshOverlay();

        BeginPointDrag(_activeRouteIndex, _pendingPointIndex, localPosition);
        _skipNextDragUndo = true;
    }

    private void OnMapPointerMove(MapPointerState pointer)
    {
        Vector2 localPosition = pointer.LocalPosition;
        bool onMap = TryLocalToWorld(localPosition, out Vector2 worldPosition);
        if (!onMap)
            _cursorCoordinatesLabel.text = "X —  Z —";
        else
        {
            Vector2 shown = _dragActive ? ApplyPointerConstraints(worldPosition, pointer) : worldPosition;
            _cursorCoordinatesLabel.text =
                $"X {shown.x:0.##}  Z {shown.y:0.##}{DescribeModifiers(pointer)}";
        }

        if (!_dragging)
            return;
        if (!_dragActive)
        {
            if (Vector2.Distance(localPosition, _dragOrigin) < DragThreshold)
                return;
            _dragActive = true;
            if (_skipNextDragUndo)
                _skipNextDragUndo = false;
            else
                _dragUndoSnapshot = PushUndo();
        }
        if (onMap)
            MoveDraggedPoint(ApplyPointerConstraints(worldPosition, pointer));
    }

    private void OnMapPointerUp(MapPointerState pointer)
    {
        EndPointDrag();
    }

    private void OnMapPointerLeave()
    {
        if (!_dragging)
            _cursorCoordinatesLabel.text = "X —  Z —";
    }

    private void OnCanvasKeyDown(KeyDownEvent evt)
    {
        if (_mapTexture == null)
            return;

        bool control = evt.ctrlKey || evt.commandKey;
        if (control && evt.keyCode == KeyCode.Z)
        {
            if (evt.shiftKey) Redo();
            else Undo();
            evt.StopPropagation();
            return;
        }
        if (control && evt.keyCode == KeyCode.Y)
        {
            Redo();
            evt.StopPropagation();
            return;
        }
        if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)
        {
            RemovePoint(_pendingPointIndex);
            evt.StopPropagation();
            return;
        }
        if (evt.keyCode == KeyCode.Escape)
        {
            CancelPointDrag();
            evt.StopPropagation();
            return;
        }

        float delta = evt.shiftKey ? 1f : 0.1f;
        Vector2 offset = evt.keyCode switch
        {
            KeyCode.LeftArrow => new Vector2(-delta, 0f),
            KeyCode.RightArrow => new Vector2(delta, 0f),
            KeyCode.UpArrow => new Vector2(0f, -delta),
            KeyCode.DownArrow => new Vector2(0f, delta),
            _ => Vector2.zero
        };
        if (offset != Vector2.zero)
        {
            NudgeSelectedPoint(offset);
            evt.StopPropagation();
        }
    }

    private void NudgeSelectedPoint(Vector2 offset)
    {
        RouteDraft active = ActiveRoute;
        if (active == null || _pendingPointIndex < 0 || _pendingPointIndex >= active.Points.Count)
            return;
        PushUndo("nudge");
        Vector2 point = active.Points[_pendingPointIndex];
        Vector2 moved = new(
            RoundCoordinate(point.x + offset.x),
            RoundCoordinate(point.y + offset.y));
        active.Points[_pendingPointIndex] = moved;
        active.RouteModified = true;
        active.HasNonSpatialGoal = false;
        if (_pointFields.TryGetValue(_pendingPointIndex, out CoordinateFields fields))
        {
            fields.X?.SetValueWithoutNotify(moved.x);
            fields.Z?.SetValueWithoutNotify(moved.y);
        }
        RefreshOverlay();
        SetPlacementInstruction();
    }

    private bool TrySelectPointAt(Vector2 localPosition)
    {
        for (int routeIndex = 0; routeIndex < _routes.Count; routeIndex++)
        {
            RouteDraft route = _routes[routeIndex];
            if (route.Points.Count == 0)
                continue;
            int pointIndex = RouteMapHitTesting.FindPoint(ToCanvasPoints(route.Points), localPosition, PointHitRadius);
            if (pointIndex < 0)
                continue;

            bool routeChanged = routeIndex != _activeRouteIndex;
            _activeRouteIndex = routeIndex;
            _pendingPointIndex = pointIndex;
            if (routeChanged)
                RefreshActiveRoute();
            else
            {
                UpdatePointSelectionHighlight();
                SetPlacementInstruction();
                RefreshOverlay();
            }
            BeginPointDrag(routeIndex, pointIndex, localPosition);
            return true;
        }
        return false;
    }

    private bool TrySelectRouteAt(Vector2 localPosition)
    {
        for (int routeIndex = 0; routeIndex < _routes.Count; routeIndex++)
        {
            if (routeIndex == _activeRouteIndex)
                continue;
            RouteDraft route = _routes[routeIndex];
            if (route.Points.Count < 2)
                continue;
            if (RouteMapHitTesting.FindSegment(ToCanvasPoints(route.Points), localPosition, RouteHitRadius) < 0)
                continue;
            SelectRoute(routeIndex);
            return true;
        }
        return false;
    }

    private void BeginPointDrag(int routeIndex, int pointIndex, Vector2 localPosition)
    {
        _dragging = true;
        _dragActive = false;
        _dragRouteIndex = routeIndex;
        _dragPointIndex = pointIndex;
        _dragOrigin = localPosition;
        _dragStartWorld = routeIndex >= 0 && routeIndex < _routes.Count &&
                          pointIndex >= 0 && pointIndex < _routes[routeIndex].Points.Count
            ? _routes[routeIndex].Points[pointIndex]
            : Vector2.zero;
        _dragUndoSnapshot = null;
        _skipNextDragUndo = false;
    }

    /// <summary>
    /// Blender/Photoshop style helpers while placing or dragging a point:
    /// Ctrl snaps to the visible grid, Shift locks the movement on the dominant axis.
    /// </summary>
    private Vector2 ApplyPointerConstraints(Vector2 worldPosition, MapPointerState pointer, Vector2? reference = null)
    {
        Vector2 origin = reference ?? _dragStartWorld;
        bool lockAxis = pointer.Shift;
        bool freeAxisIsX = true;
        if (lockAxis)
        {
            Vector2 delta = worldPosition - origin;
            freeAxisIsX = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y);
        }

        Vector2 result = worldPosition;
        if (lockAxis)
        {
            if (freeAxisIsX) result.y = origin.y;
            else result.x = origin.x;
        }

        if (pointer.Control)
        {
            float step = GridSnapStep();
            result.x = Mathf.Round(result.x / step) * step;
            result.y = Mathf.Round(result.y / step) * step;
            if (lockAxis)
            {
                if (freeAxisIsX) result.y = origin.y;
                else result.x = origin.x;
            }
        }

        return result;
    }

    private float GridSnapStep() => Mathf.Max(0.05f, _overlay.GridStep);

    private string DescribeModifiers(MapPointerState pointer)
    {
        string hint = string.Empty;
        if (pointer.Control) hint += $"  snap {GridSnapStep():0.##} m";
        if (pointer.Shift) hint += "  axis lock";
        return hint;
    }

    private void EndPointDrag()
    {
        _dragging = false;
        _dragActive = false;
        _dragRouteIndex = -1;
        _dragPointIndex = -1;
        _dragUndoSnapshot = null;
        _skipNextDragUndo = false;
    }

    private void CancelPointDrag()
    {
        if (!_dragging)
            return;
        if (_dragActive && _dragUndoSnapshot != null)
        {
            int index = _undoStack.IndexOf(_dragUndoSnapshot);
            if (index >= 0)
                _undoStack.RemoveAt(index);
            RestoreSnapshot(_dragUndoSnapshot);
            _instructionLabel.text = "Move cancelled.";
        }
        EndPointDrag();
    }

    private void MoveDraggedPoint(Vector2 worldPosition)
    {
        RouteDraft route = _dragRouteIndex >= 0 && _dragRouteIndex < _routes.Count ? _routes[_dragRouteIndex] : null;
        if (route == null || _dragPointIndex < 0 || _dragPointIndex >= route.Points.Count)
            return;

        float x = RoundCoordinate(worldPosition.x);
        float z = RoundCoordinate(worldPosition.y);
        route.Points[_dragPointIndex] = new Vector2(x, z);
        route.RouteModified = true;
        route.HasNonSpatialGoal = false;

        if (_pointFields.TryGetValue(_dragPointIndex, out CoordinateFields fields))
        {
            fields.X?.SetValueWithoutNotify(x);
            fields.Z?.SetValueWithoutNotify(z);
        }
        RefreshOverlay();
        SetPlacementInstruction();
    }

    private List<Vector2> ToCanvasPoints(IReadOnlyList<Vector2> worldPoints)
    {
        var canvasPoints = new List<Vector2>(worldPoints.Count);
        foreach (Vector2 point in worldPoints)
            canvasPoints.Add(WorldToCanvas(point));
        return canvasPoints;
    }

    private bool TryLocalToWorld(Vector2 localPosition, out Vector2 worldPosition)
    {
        worldPosition = default;
        Rect imageRect = GetDisplayedMapRect();
        if (_mapTexture == null || imageRect.width <= 0f || !imageRect.Contains(localPosition))
            return false;
        var imagePosition = new Vector2(
            Mathf.InverseLerp(imageRect.xMin, imageRect.xMax, localPosition.x),
            Mathf.InverseLerp(imageRect.yMin, imageRect.yMax, localPosition.y));
        worldPosition = OccupancyMapCoordinates.ImageNormalizedToWorld(imagePosition, _mapBounds);
        return true;
    }

    private Vector2 WorldToCanvas(Vector2 worldPosition)
    {
        Rect imageRect = GetDisplayedMapRect();
        Vector2 imagePosition = OccupancyMapCoordinates.WorldToImageNormalized(worldPosition, _mapBounds);
        return new Vector2(
            imageRect.xMin + imagePosition.x * imageRect.width,
            imageRect.yMin + imagePosition.y * imageRect.height);
    }

    private void RefreshOverlay()
    {
        Rect imageRect = GetDisplayedMapRect();
        _overlay.SetMap(_mapBounds, imageRect);
        _overlay.ShowGrid = _showGrid;
        _overlay.SetRoutes(_routes.Select((route, index) =>
            new OccupancyMapRouteOverlay.RouteVisual(
                route.Points,
                RouteColor(index),
                index == _activeRouteIndex)));
        _overlay.SetFormations(BuildFormationPreviews());
        UpdateGridScaleLabel();
        UpdateFormationPreviewLabel();
        RefreshPointLabels();
    }

    /// <summary>
    /// Spawn layout of every route that fills more than one agent. It reuses the runtime slot maths
    /// (route start, first objective as heading, formation and spacing) so the map shows exactly where
    /// the group will stand when the scenario starts.
    /// </summary>
    private List<OccupancyMapRouteOverlay.FormationPreview> BuildFormationPreviews()
    {
        _formationPreviews.Clear();
        for (int index = 0; index < _routes.Count; index++)
        {
            RouteDraft route = _routes[index];
            if (route.IsRobot || route.Count <= 1 || route.Points.Count < 2)
                continue;

            List<Vector2> slots = GroupFormation.CreateSlots(route.Count, route.GroupSpacing, route.Formation);
            Vector2 origin = route.Points[0];
            Vector2 direction = route.Points[1] - route.Points[0];
            float heading = direction.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(direction.x, direction.y)
                : 0f;

            var worldSlots = new List<Vector2>(slots.Count);
            foreach (Vector2 slot in slots)
                worldSlots.Add(origin + GroupFormation.Rotate(slot, heading));

            _formationPreviews.Add(new OccupancyMapRouteOverlay.FormationPreview(
                worldSlots,
                RouteColor(index),
                index == _activeRouteIndex));
        }

        return _formationPreviews;
    }

    /// <summary>Explains what the markers drawn around the route start mean.</summary>
    private void UpdateFormationPreviewLabel()
    {
        RouteDraft active = ActiveRoute;
        if (active == null || active.IsRobot)
        {
            _formationPreviewLabel.text = string.Empty;
            return;
        }

        _formationPreviewLabel.text = active.Count <= 1
            ? "One agent: no formation to lay out."
            : $"{active.Count} agents · {FormationToDisplay(active.Formation).ToLowerInvariant()} · " +
              $"{active.GroupSpacing:0.##} m — slots shown on the start point.";
    }

    private void RefreshPointLabels()
    {
        _pointLabelLayer.Clear();
        RouteDraft active = ActiveRoute;
        if (active == null || _mapTexture == null || GetDisplayedMapRect().width <= 0f)
            return;

        for (int index = 0; index < active.Points.Count; index++)
        {
            Vector2 canvasPosition = WorldToCanvas(active.Points[index]);
            var label = new Label(RouteMapHitTesting.PointLabel(index));
            label.AddToClassList("map-point-label");
            label.EnableInClassList("active", index == _pendingPointIndex);
            label.pickingMode = PickingMode.Ignore;
            // Labels sit above the map: their offsets depend on the live pointer mapping.
            label.style.left = canvasPosition.x - PointLabelWidth * 0.5f;
            label.style.top = canvasPosition.y - 30f;
            _pointLabelLayer.Add(label);
        }
    }

    private Rect GetDisplayedMapRect()
    {
        return _mapTexture == null
            ? Rect.zero
            : OccupancyMapLayout.FitRect(_canvas.contentRect, _mapTexture.width, _mapTexture.height);
    }

    private void UpdateGridScaleLabel()
    {
        if (!_showGrid || _mapTexture == null)
        {
            _gridScaleLabel.text = "Grid hidden";
            return;
        }

        _gridScaleLabel.text =
            $"Grid step {_overlay.GridStep:0.##} m · map {_mapBounds.size.x:0.#} × {_mapBounds.size.z:0.#} m";
    }

    private void SetPlacementInstruction()
    {
        RouteDraft active = ActiveRoute;
        if (active == null)
            return;
        _instructionLabel.text =
            $"{active.Id}: {RouteMapHitTesting.PointLabel(_pendingPointIndex)} selected. Click the map or drag the point.";
    }

    private Color RouteColor(int index)
    {
        if (index < 0 || index >= _routes.Count)
            return HumanColors[0];
        return _routes[index].IsRobot
            ? RobotColor
            : HumanColors[Mathf.Max(0, index - 1) % HumanColors.Length];
    }

    private RouteDraft ActiveRoute => _activeRouteIndex >= 0 && _activeRouteIndex < _routes.Count
        ? _routes[_activeRouteIndex]
        : null;

    private void ResetHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _lastEditKey = null;
        _lastEditTime = float.NegativeInfinity;
        EndPointDrag();
    }

    private EditorSnapshot PushUndo(string coalesceKey = null)
    {
        if (coalesceKey != null && coalesceKey == _lastEditKey &&
            Time.realtimeSinceStartup - _lastEditTime < FieldEditCoalesceSeconds)
        {
            _lastEditTime = Time.realtimeSinceStartup;
            return null;
        }

        EditorSnapshot snapshot = CaptureSnapshot();
        _lastEditKey = coalesceKey;
        _lastEditTime = Time.realtimeSinceStartup;
        _undoStack.Add(snapshot);
        if (_undoStack.Count > UndoLimit)
            _undoStack.RemoveAt(0);
        _redoStack.Clear();
        return snapshot;
    }

    private void Undo()
    {
        if (_undoStack.Count == 0)
        {
            _instructionLabel.text = "Nothing left to undo.";
            return;
        }

        EditorSnapshot snapshot = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        _redoStack.Add(CaptureSnapshot());
        RestoreSnapshot(snapshot);
        _instructionLabel.text = "Undone.";
    }

    private void Redo()
    {
        if (_redoStack.Count == 0)
        {
            _instructionLabel.text = "Nothing left to redo.";
            return;
        }

        EditorSnapshot snapshot = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        _undoStack.Add(CaptureSnapshot());
        RestoreSnapshot(snapshot);
        _instructionLabel.text = "Redone.";
    }

    private EditorSnapshot CaptureSnapshot()
    {
        return new EditorSnapshot
        {
            Routes = _routes.Select(route => new RouteSnapshot
            {
                Id = route.Id,
                IsRobot = route.IsRobot,
                Count = route.Count,
                Speed = route.Speed,
                EndBehavior = route.EndBehavior,
                Group = route.Group,
                Formation = route.Formation,
                GroupSpacing = route.GroupSpacing,
                Points = new List<Vector2>(route.Points),
                Source = route.Source,
                RouteModified = route.RouteModified,
                HasNonSpatialGoal = route.HasNonSpatialGoal
            }).ToList(),
            ActiveRouteIndex = _activeRouteIndex,
            PendingPointIndex = _pendingPointIndex
        };
    }

    private void RestoreSnapshot(EditorSnapshot snapshot)
    {
        if (snapshot == null)
            return;

        _routes.Clear();
        foreach (RouteSnapshot route in snapshot.Routes)
        {
            var draft = new RouteDraft
            {
                Id = route.Id,
                IsRobot = route.IsRobot,
                Count = route.Count,
                Speed = route.Speed,
                EndBehavior = route.EndBehavior,
                Group = route.Group,
                Formation = route.Formation,
                GroupSpacing = route.GroupSpacing,
                Source = route.Source,
                RouteModified = route.RouteModified,
                HasNonSpatialGoal = route.HasNonSpatialGoal
            };
            draft.Points.AddRange(route.Points);
            _routes.Add(draft);
        }

        _activeRouteIndex = Mathf.Clamp(snapshot.ActiveRouteIndex, 0, Mathf.Max(0, _routes.Count - 1));
        _pendingPointIndex = Mathf.Max(0, snapshot.PendingPointIndex);
        _lastEditKey = null;
        EndPointDrag();
        RebuildRouteList();
        RefreshActiveRoute();
    }

    private int NextHumanRouteNumber()
    {
        int number = 1;
        while (_routes.Any(route => string.Equals(route.Id, $"Human route {number}", StringComparison.OrdinalIgnoreCase)))
            number++;
        return number;
    }

    private string MakeUniqueRouteId(string preferred)
    {
        string baseName = string.IsNullOrWhiteSpace(preferred) ? $"Human route {NextHumanRouteNumber()}" : preferred;
        string candidate = baseName;
        int suffix = 2;
        while (_routes.Any(route => string.Equals(route.Id, candidate, StringComparison.OrdinalIgnoreCase)))
            candidate = $"{baseName} ({suffix++})";
        return candidate;
    }

    private static RouteDraft CreateHumanDraft(int index)
    {
        return new RouteDraft
        {
            Id = $"Human route {index}",
            Count = 0,
            RouteModified = true,
            Points = { new Vector2(2f, 0f), Vector2.zero }
        };
    }

    private static RouteDraft CreateHumanDraft(ScenarioData scenario, HumanScenarioConfig human, int index)
    {
        var draft = new RouteDraft
        {
            Id = string.IsNullOrWhiteSpace(human.Id) ? $"Human route {index}" : human.Id,
            Count = Mathf.Max(0, human.Count),
            Speed = Mathf.Max(0.01f, human.Speed),
            EndBehavior = HumanEndBehaviorParser.Parse(human.EndBehavior),
            Group = human.Group,
            Formation = string.IsNullOrWhiteSpace(human.Spawn?.Formation) ? "pair" : human.Spawn.Formation.Trim().ToLowerInvariant(),
            GroupSpacing = human.Spawn != null ? Mathf.Max(0.4f, human.Spawn.Spacing) : 1.5f,
            Source = human,
            RouteModified = false
        };

        draft.Points.Add(ResolveSpawnPosition(scenario, human.Spawn));
        if (IsSpatialGoal(human.Goal))
            draft.Points.Add(ResolveGoalPosition(scenario, human.Goal));
        else
            draft.HasNonSpatialGoal = human.Goal != null;
        if (human.Goals != null)
        {
            foreach (GoalConfig goal in human.Goals.Where(IsSpatialGoal))
                draft.Points.Add(ResolveGoalPosition(scenario, goal));
        }
        if (draft.Points.Count < 2)
            draft.Points.Add(draft.Points[0] + Vector2.right * 2f);
        return draft;
    }

    private static bool TryResolveReference(ScenarioData scenario, string reference, out Vector2 position)
    {
        position = default;
        if (string.IsNullOrWhiteSpace(reference) || scenario?.Points == null ||
            !scenario.Points.TryGetValue(reference, out RefPoint point) || point == null)
            return false;
        Vector3 vector = point.ToVector3();
        position = new Vector2(vector.x, vector.z);
        return true;
    }

    private static Vector2 ResolveSpawnPosition(ScenarioData scenario, SpawnConfig spawn)
    {
        if (spawn == null) return Vector2.zero;
        if (TryResolveReference(scenario, spawn.Reference, out Vector2 referenced)) return referenced;
        Vector3 position = spawn.Position?.ToVector3() ?? spawn.Zone?.ToVector3() ?? Vector3.zero;
        return new Vector2(position.x, position.z);
    }

    private static Vector2 ResolveGoalPosition(ScenarioData scenario, GoalConfig goal)
    {
        if (goal == null) return Vector2.zero;
        Debug.Log($"Resolving goal '{goal.Reference}' of type '{goal.Type}' for position.");
        if (TryResolveReference(scenario, goal.Reference, out Vector2 referenced)) return referenced;
        Vector3 position = goal.Position?.ToVector3() ?? goal.Zone?.ToVector3() ?? Vector3.zero;
        Debug.LogWarning($"Goal '{goal.Reference}' has no spatial reference; using position {position}.");
        return new Vector2(position.x, position.z);
    }

    private static bool IsSpatialGoal(GoalConfig goal)
    {
        if (goal == null) return false;
        string type = goal.Type?.ToLowerInvariant();
        return string.IsNullOrWhiteSpace(type) || type == "point" || type == "random";
    }

    private static void EnsureMinimumPoints(RouteDraft draft)
    {
        if (draft.Points.Count == 0) draft.Points.Add(Vector2.zero);
        if (draft.Points.Count == 1) draft.Points.Add(draft.Points[0] + Vector2.right * 2f);
    }

    private static bool HasRepeatedConsecutivePoint(RouteDraft draft)
    {
        for (int index = 1; index < draft.Points.Count; index++)
            if (Vector2.Distance(draft.Points[index - 1], draft.Points[index]) < 0.001f) return true;
        return false;
    }

    private static HashSet<string> CollectRouteReferences(ScenarioData scenario)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);
        AddReference(references, scenario?.Robot?.StartRef);
        AddReference(references, scenario?.Robot?.GoalRef);
        if (scenario?.Robot?.WaypointRefs != null)
        {
            foreach (string reference in scenario.Robot.WaypointRefs)
                AddReference(references, reference);
        }

        if (scenario?.Humans != null)
        {
            foreach (HumanScenarioConfig human in scenario.Humans.Where(human => human != null))
            {
                AddReference(references, human.Spawn?.Reference);
                AddReference(references, human.Goal?.Reference);
                if (human.Goals == null)
                    continue;
                foreach (GoalConfig goal in human.Goals.Where(goal => goal != null))
                    AddReference(references, goal.Reference);
            }
        }

        return references;
    }

    private static void AddReference(ISet<string> references, string reference)
    {
        if (!string.IsNullOrWhiteSpace(reference))
            references.Add(reference);
    }

    private static string FormatRouteSummary(RouteDraft draft)
    {
        if (draft.Points.Count < 2) return "Incomplete route";
        return $"{draft.Points.Count - 1} objective(s): " +
               string.Join(" → ", draft.Points.Select(point => $"({point.x:0.##}, {point.y:0.##})"));
    }

    private static void WritePoint(ScenarioData scenario, string reference, Vector2 point)
    {
        scenario.Points[reference] = new RefPoint
        {
            X = RoundCoordinate(point.x),
            Y = 0f,
            Z = RoundCoordinate(point.y)
        };
    }

    private static float RoundCoordinate(float value) => Mathf.Round(value * 100f) / 100f;

    private static void SetSpawnReference(SpawnConfig spawn, string fallbackReference)
    {
        spawn.Type = "point";
        spawn.Reference = string.IsNullOrWhiteSpace(spawn.Reference) ? fallbackReference : spawn.Reference;
        spawn.Position = null;
        spawn.Zone = null;
    }

    private static void SetGoalReference(GoalConfig goal, string fallbackReference)
    {
        goal.Type = "point";
        goal.Reference = string.IsNullOrWhiteSpace(goal.Reference) ? fallbackReference : goal.Reference;
        goal.Position = null;
        goal.Zone = null;
        goal.Target = null;
    }

    private static string ToFileId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "human_route";
        return new string(value.Trim().ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray()).Trim('_');
    }
}
