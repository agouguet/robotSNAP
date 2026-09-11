using System;
using System.Collections.Generic;
using System.Linq;
using RobotSNAP.Core.Scenario;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Owns the multi-route editing state for the scenario wizard.
/// One human route can represent one or several humans sharing the same ordered goals.
/// </summary>
public sealed class ScenarioRouteEditor
{
    private const string HiddenClass = "route-editor-hidden";

    private sealed class RouteDraft
    {
        public string Id;
        public bool IsRobot;
        public int Count;
        public float Speed = 1f;
        public string Behavior = "normal";
        public string Controller = "SFM";
        public readonly List<Vector2> Points = new();
        public HumanScenarioConfig Source;
        public bool RouteModified;
        public bool HasNonSpatialGoal;
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

    private readonly List<RouteDraft> _routes = new();
    private readonly DropdownField _routeSelector;
    private readonly Button _addHumanRouteButton;
    private readonly Button _removeHumanRouteButton;
    private readonly Button _setRouteStartButton;
    private readonly Button _addRouteObjectiveButton;
    private readonly Button _toggleMapGridButton;
    private readonly VisualElement _humanRouteSettings;
    private readonly IntegerField _humanCountField;
    private readonly FloatField _humanSpeedField;
    private readonly DropdownField _humanBehaviorDropdown;
    private readonly DropdownField _movementControllerDropdown;
    private readonly VisualElement _routePointsContainer;
    private readonly VisualElement _canvas;
    private readonly Image _mapImage;
    private readonly Label _mapPlaceholder;
    private readonly Label _instructionLabel;
    private readonly Label _cursorCoordinatesLabel;
    private readonly Label _activeRouteLabel;
    private readonly Label _gridScaleLabel;
    private readonly OccupancyMapRouteOverlay _overlay;

    private Texture2D _mapTexture;
    private Bounds _mapBounds;
    private int _activeRouteIndex;
    private int _pendingPointIndex;
    private bool _updatingFields;
    private bool _showGrid = true;

    public ScenarioRouteEditor(VisualElement root)
    {
        _routeSelector = root.Q<DropdownField>("RouteSelectorDropdown");
        _addHumanRouteButton = root.Q<Button>("AddHumanRouteButton");
        _removeHumanRouteButton = root.Q<Button>("RemoveHumanRouteButton");
        _setRouteStartButton = root.Q<Button>("SetRouteStartButton");
        _addRouteObjectiveButton = root.Q<Button>("AddRouteObjectiveButton");
        _toggleMapGridButton = root.Q<Button>("ToggleMapGridButton");
        _humanRouteSettings = root.Q<VisualElement>("HumanRouteSettings");
        _humanCountField = root.Q<IntegerField>("HumanCountField");
        _humanSpeedField = root.Q<FloatField>("HumanSpeedField");
        _humanBehaviorDropdown = root.Q<DropdownField>("HumanBehaviorDropdown");
        _movementControllerDropdown = root.Q<DropdownField>("MovementControllerDropdown");
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
                _routeSelector, _addHumanRouteButton, _removeHumanRouteButton,
                _setRouteStartButton, _addRouteObjectiveButton, _toggleMapGridButton,
                _humanRouteSettings, _humanCountField, _humanSpeedField,
                _humanBehaviorDropdown, _movementControllerDropdown, _routePointsContainer,
                _canvas, _mapImage, _mapPlaceholder, _instructionLabel,
                _cursorCoordinatesLabel, _activeRouteLabel, _gridScaleLabel, overlayHost
            }.Any(element => element == null))
        {
            throw new InvalidOperationException("The scenario route editor UI is incomplete.");
        }

        _overlay = new OccupancyMapRouteOverlay();
        overlayHost.Add(_overlay);
        _mapImage.scaleMode = ScaleMode.ScaleToFit;
        _canvas.AddManipulator(new OccupancyMapPlacementManipulator(
            OnMapPointerDown,
            OnMapPointerMove,
            () => _cursorCoordinatesLabel.text = "X —  Z —"));
        _canvas.RegisterCallback<GeometryChangedEvent>(_ => RefreshOverlay());

        _routeSelector.RegisterValueChangedCallback(_ => SelectRouteByName(_routeSelector.value));
        _addHumanRouteButton.clicked += AddHumanRoute;
        _removeHumanRouteButton.clicked += RemoveActiveHumanRoute;
        _setRouteStartButton.clicked += SelectStartForPlacement;
        _addRouteObjectiveButton.clicked += AddObjective;
        _toggleMapGridButton.clicked += ToggleGrid;

        _humanCountField.RegisterValueChangedCallback(evt =>
        {
            int value = Mathf.Max(0, evt.newValue);
            if (value != evt.newValue) _humanCountField.SetValueWithoutNotify(value);
            UpdateHumanDraft(draft => draft.Count = value);
        });
        _humanSpeedField.RegisterValueChangedCallback(evt =>
        {
            float value = Mathf.Max(0.01f, evt.newValue);
            if (!Mathf.Approximately(value, evt.newValue)) _humanSpeedField.SetValueWithoutNotify(value);
            UpdateHumanDraft(draft => draft.Speed = value);
        });
        _humanBehaviorDropdown.RegisterValueChangedCallback(evt => UpdateHumanDraft(draft => draft.Behavior = evt.newValue));
        _movementControllerDropdown.RegisterValueChangedCallback(evt => UpdateHumanDraft(draft => draft.Controller = evt.newValue));
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
        RebuildRouteSelector();
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
        RebuildRouteSelector();
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

        var humanConfigs = new List<HumanScenarioConfig>();
        int humanIndex = 1;
        foreach (RouteDraft draft in _routes.Where(route => !route.IsRobot && route.Count > 0))
        {
            HumanScenarioConfig config = draft.Source ?? new HumanScenarioConfig();
            config.Id = string.IsNullOrWhiteSpace(config.Id) ? $"human_route_{humanIndex}" : config.Id;
            config.Count = draft.Count;
            config.Speed = draft.Speed;
            config.Behavior = draft.Behavior;
            config.MovementController ??= new MovementControllerConfig();
            config.MovementController.Type = draft.Controller;

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

    private void AddHumanRoute()
    {
        RouteDraft draft = CreateHumanDraft(NextHumanRouteNumber());
        _routes.Add(draft);
        _activeRouteIndex = _routes.Count - 1;
        _pendingPointIndex = 0;
        RebuildRouteSelector();
        RefreshActiveRoute();
    }

    private void RemoveActiveHumanRoute()
    {
        RouteDraft active = ActiveRoute;
        if (active == null || active.IsRobot)
            return;

        _routes.RemoveAt(_activeRouteIndex);
        if (_routes.Count == 1)
            _routes.Add(CreateHumanDraft(1));
        _activeRouteIndex = Mathf.Clamp(_activeRouteIndex - 1, 0, _routes.Count - 1);
        _pendingPointIndex = 0;
        RebuildRouteSelector();
        RefreshActiveRoute();
    }

    private void AddObjective()
    {
        RouteDraft active = ActiveRoute;
        if (active == null)
            return;

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
        SetPlacementInstruction();
        RebuildPointRows();
    }

    private void ToggleGrid()
    {
        _showGrid = !_showGrid;
        _toggleMapGridButton.EnableInClassList("active", _showGrid);
        _overlay.ShowGrid = _showGrid;
        UpdateGridScaleLabel();
    }

    private void SelectRouteByName(string routeName)
    {
        int index = _routes.FindIndex(route => route.Id == routeName);
        if (index < 0 || index == _activeRouteIndex)
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

        _routeSelector.SetValueWithoutNotify(active.Id);
        _activeRouteLabel.text = active.Id;
        _removeHumanRouteButton.SetEnabled(!active.IsRobot);
        _humanRouteSettings.EnableInClassList(HiddenClass, active.IsRobot);

        _updatingFields = true;
        if (!active.IsRobot)
        {
            _humanCountField.SetValueWithoutNotify(active.Count);
            _humanSpeedField.SetValueWithoutNotify(active.Speed);
            EnsureChoice(_humanBehaviorDropdown, active.Behavior, "normal");
            EnsureChoice(_movementControllerDropdown, active.Controller, "SFM");
        }
        _updatingFields = false;

        RebuildPointRows();
        SetPlacementInstruction();
        RefreshOverlay();
    }

    private void RebuildRouteSelector()
    {
        _routeSelector.choices = _routes.Select(route => route.Id).ToList();
        if (_routes.Count > 0)
            _routeSelector.SetValueWithoutNotify(_routes[Mathf.Clamp(_activeRouteIndex, 0, _routes.Count - 1)].Id);
    }

    private void RebuildPointRows()
    {
        _routePointsContainer.Clear();
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
            var name = new Label(index == 0 ? "Start" : $"Objective {index}");
            name.AddToClassList("route-point-name");
            var placeButton = new Button(() => SelectPointForPlacement(capturedIndex)) { text = "Place" };
            placeButton.AddToClassList("route-point-place-button");
            header.Add(name);
            header.Add(placeButton);
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

            _routePointsContainer.Add(row);
        }
    }

    private static FloatField CreateCoordinateField(string label, float value)
    {
        var field = new FloatField(label) { value = value };
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

    private void SelectPointForPlacement(int index)
    {
        _pendingPointIndex = index;
        RebuildPointRows();
        SetPlacementInstruction();
    }

    private void SetPointCoordinate(int index, bool isX, float value)
    {
        RouteDraft active = ActiveRoute;
        if (active == null || index < 0 || index >= active.Points.Count)
            return;
        Vector2 point = active.Points[index];
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
            return;
        active.Points.RemoveAt(index);
        active.RouteModified = true;
        _pendingPointIndex = Mathf.Clamp(_pendingPointIndex, 0, active.Points.Count - 1);
        RebuildPointRows();
        RefreshOverlay();
    }

    private void UpdateHumanDraft(Action<RouteDraft> update)
    {
        if (_updatingFields || ActiveRoute == null || ActiveRoute.IsRobot)
            return;
        update(ActiveRoute);
    }

    private void OnMapPointerDown(Vector2 localPosition)
    {
        if (!TryLocalToWorld(localPosition, out Vector2 worldPosition) || ActiveRoute == null)
            return;
        _pendingPointIndex = Mathf.Clamp(_pendingPointIndex, 0, ActiveRoute.Points.Count - 1);
        ActiveRoute.Points[_pendingPointIndex] = worldPosition;
        ActiveRoute.RouteModified = true;
        ActiveRoute.HasNonSpatialGoal = false;
        RebuildPointRows();
        RefreshOverlay();
    }

    private void OnMapPointerMove(Vector2 localPosition)
    {
        _cursorCoordinatesLabel.text = TryLocalToWorld(localPosition, out Vector2 worldPosition)
            ? $"X {worldPosition.x:0.##}  Z {worldPosition.y:0.##}"
            : "X —  Z —";
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

    private void RefreshOverlay()
    {
        Rect imageRect = GetDisplayedMapRect();
        _overlay.SetMap(_mapBounds, imageRect);
        _overlay.ShowGrid = _showGrid;
        _overlay.SetRoutes(_routes.Select((route, index) =>
            new OccupancyMapRouteOverlay.RouteVisual(
                route.Points,
                route.IsRobot ? RobotColor : HumanColors[Mathf.Max(0, index - 1) % HumanColors.Length],
                index == _activeRouteIndex)));
        UpdateGridScaleLabel();
    }

    private Rect GetDisplayedMapRect()
    {
        Rect canvasRect = _canvas.contentRect;
        if (_mapTexture == null || canvasRect.width <= 0f || canvasRect.height <= 0f)
            return Rect.zero;
        float imageAspect = (float)_mapTexture.width / _mapTexture.height;
        float canvasAspect = canvasRect.width / canvasRect.height;
        if (imageAspect > canvasAspect)
        {
            float height = canvasRect.width / imageAspect;
            return new Rect(0f, (canvasRect.height - height) * 0.5f, canvasRect.width, height);
        }
        float width = canvasRect.height * imageAspect;
        return new Rect((canvasRect.width - width) * 0.5f, 0f, width, canvasRect.height);
    }

    private void UpdateGridScaleLabel()
    {
        _gridScaleLabel.text = _showGrid && _mapTexture != null
            ? $"Grid and scale: {_overlay.GridStep:0.##} m"
            : "Grid hidden";
    }

    private void SetPlacementInstruction()
    {
        RouteDraft active = ActiveRoute;
        if (active == null)
            return;
        string pointName = _pendingPointIndex == 0 ? "start point" : $"objective {_pendingPointIndex}";
        _instructionLabel.text = $"Click the map to place the {pointName} for {active.Id}.";
    }

    private RouteDraft ActiveRoute => _activeRouteIndex >= 0 && _activeRouteIndex < _routes.Count
        ? _routes[_activeRouteIndex]
        : null;

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
            Behavior = string.IsNullOrWhiteSpace(human.Behavior) ? "normal" : human.Behavior,
            Controller = string.IsNullOrWhiteSpace(human.MovementController?.Type) ? "SFM" : human.MovementController.Type,
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
        if (TryResolveReference(scenario, goal.Reference, out Vector2 referenced)) return referenced;
        Vector3 position = goal.Position?.ToVector3() ?? goal.Zone?.ToVector3() ?? Vector3.zero;
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
        scenario.Points[reference] = new RefPoint { X = point.x, Y = 0f, Z = point.y };
    }

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

    private static void EnsureChoice(DropdownField dropdown, string value, string fallback)
    {
        string selected = string.IsNullOrWhiteSpace(value) ? fallback : value;
        if (!dropdown.choices.Contains(selected))
            dropdown.choices = dropdown.choices.Concat(new[] { selected }).ToList();
        dropdown.SetValueWithoutNotify(selected);
    }
}
