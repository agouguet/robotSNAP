using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// Cell budget of the walkable grid sampled from the occupancy image. Taken from the runtime planner, so
    /// the editor and the simulation can never drift apart on the sampling of the map.
    /// </summary>
    private const int NavigationResolution = ScenarioNavigation.Resolution;

    /// <summary>
    /// Coarser grid used while a point is dragged: replanning every mouse move has to stay cheap, and the
    /// exact geometry is rebuilt on the fine grid as soon as the point is released.
    /// </summary>
    private const int InteractiveNavigationResolution = 150;

    /// <summary>
    /// Body radius kept clear from walls, taken from the runtime planner, so the drawn path is exactly one the
    /// simulated agent walks.
    /// </summary>
    private const float NavigationAgentRadius = ScenarioNavigation.DefaultAgentRadius;

    private sealed class RouteDraft
    {
        public string Id;
        public bool IsRobot;
        public int Count;
        public float Speed = 1f;
        public HumanEndBehavior EndBehavior = HumanEndBehavior.Stay;
        public string Group;
        public string MovementController;
        public string Formation = "pair";
        public float GroupSpacing = 1.5f;
        public float FormationParameter;
        public readonly List<Vector2> Points = new();
        /// <summary>True when the whole route is scattered at spawn instead of starting on its first point.</summary>
        public bool SpawnRandom;
        /// <summary>Area the route is scattered in, meaningful when <see cref="SpawnRandom"/> is set.</summary>
        public Rect SpawnZone;
        /// <summary>Arrival area of each point, parallel to <see cref="Points"/>; a null entry is a fixed point.</summary>
        public readonly List<Rect?> PointZones = new();
        public HumanScenarioConfig Source;
        public bool RouteModified;
        public bool HasNonSpatialGoal;

        /// <summary>Arrival area of one point, or null when that point is fixed.</summary>
        public Rect? ZoneAt(int index) =>
            index >= 0 && index < PointZones.Count ? PointZones[index] : null;
    }

    private sealed class RouteSnapshot
    {
        public string Id;
        public bool IsRobot;
        public int Count;
        public float Speed;
        public HumanEndBehavior EndBehavior;
        public string Group;
        public string MovementController;
        public string Formation;
        public float GroupSpacing;
        public float FormationParameter;
        public List<Vector2> Points;
        public bool SpawnRandom;
        public Rect SpawnZone;
        public List<Rect?> PointZones;
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

    /// <summary>Drawable geometry of one route, cached until its points or the map change.</summary>
    private sealed class PlannedGeometry
    {
        public int Epoch;
        public int Signature;
        public bool Interactive;
        public bool Looping;
        public List<Vector2> Points;
        public int FallbackSegments;
        public int CrossingSegments;

        /// <summary>Index in <see cref="Points"/> where the loop return leg starts, or -1 when there is none.</summary>
        public int LoopReturnStart = -1;
    }

    private struct CoordinateFields
    {
        public FloatField X;
        public FloatField Z;
    }

    /// <summary>The four numeric fields of one area, so the spawn and arrival editors share one code path.</summary>
    private sealed class ZoneFields
    {
        public FloatField CenterX;
        public FloatField CenterZ;
        public FloatField SizeX;
        public FloatField SizeZ;
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

    /// <summary>First entry keeps the controller of the HumanConfig asset; the rest pin it for the group.</summary>
    private const string InheritControllerChoice = "Inherit from config";

    /// <summary>
    /// Share of an area that has to be walkable. Below this the area is a drawing mistake rather than a tight
    /// spot: the runtime projects every agent it cannot place, so the crowd lands on one strip.
    /// </summary>
    private const float MinimumAreaCoverage = 0.15f;

    private static readonly string[] MovementControllerChoices =
    {
        InheritControllerChoice,
        HumanMovementControllerParser.ToDisplayName(MovementControllerType.SFM),
        HumanMovementControllerParser.ToDisplayName(MovementControllerType.External)
    };

    private static readonly string[] FormationChoices =
    {
        "Pair",
        "Row",
        "Column",
        "Wedge",
        "Cluster",
        "Independent"
    };

    private readonly List<RouteDraft> _routes = new();
    private readonly List<EditorSnapshot> _undoStack = new();
    private readonly List<EditorSnapshot> _redoStack = new();
    /// <summary>Route copied with Ctrl+C, waiting to be pasted as a new route.</summary>
    private RouteSnapshot _routeClipboard;
    private readonly List<VisualElement> _pointRows = new();
    private readonly Dictionary<int, CoordinateFields> _pointFields = new();
    /// <summary>The four area fields of the points that are areas, keyed by point index (0 is the start).</summary>
    private readonly Dictionary<int, ZoneFields> _pointZoneFields = new();
    /// <summary>The size badge of the points that are areas, so typing a number updates it live.</summary>
    private readonly Dictionary<int, Label> _pointAreaLabels = new();
    private readonly VisualElement _routeList;
    private readonly Button _addHumanRouteButton;
    private readonly Button _duplicateHumanRouteButton;
    private readonly Button _removeHumanRouteButton;
    private readonly Button _setRouteStartButton;
    private readonly Button _addRouteObjectiveButton;
    private readonly Button _toggleMapGridButton;
    private readonly VisualElement _humanRouteSettings;
    private readonly IntegerField _humanCountField;
    private readonly FloatField _humanSpeedField;
    private readonly DropdownField _endBehaviorDropdown;
    private readonly DropdownField _movementControllerDropdown;
    private readonly DropdownField _formationDropdown;
    private readonly FloatField _groupSpacingField;
    private readonly Label _groupSpacingLabel;
    private readonly Label _groupSpacingHelp;
    private readonly FloatField _formationParameterField;
    private readonly Label _formationParameterLabel;
    private readonly Label _formationPreviewLabel;
    private readonly Button _sectionGlobalHeader;
    private readonly Button _sectionGroupHeader;
    private readonly Button _sectionBehaviourHeader;
    private readonly VisualElement _sectionGlobalContent;
    private readonly VisualElement _sectionGroupContent;
    private readonly VisualElement _sectionBehaviourContent;
    private readonly VisualElement _routePointsContainer;
    private readonly VisualElement _canvas;
    private readonly Image _mapImage;
    private readonly Label _mapPlaceholder;
    private readonly Label _instructionLabel;
    private readonly Label _cursorCoordinatesLabel;
    private readonly Label _activeRouteLabel;
    private readonly Label _gridScaleLabel;
    private readonly Label _mapPathStatusLabel;
    private readonly OccupancyMapRouteOverlay _overlay;
    private readonly VisualElement _pointLabelLayer;
    private readonly List<OccupancyMapRouteOverlay.FormationPreview> _formationPreviews = new();
    private readonly List<OccupancyMapRouteOverlay.ZoneVisual> _zoneVisuals = new();
    private readonly Dictionary<int, PlannedGeometry> _plannedGeometry = new();

    private Texture2D _mapTexture;
    private Bounds _mapBounds;
    private OccupancyGrid _navigationGrid;
    private OccupancyGrid _interactiveGrid;
    private OccupancyGrid _occupancyGrid;
    private int _navigationGridEpoch;
    private int _activeRouteIndex;
    private int _pendingPointIndex;
    private bool _updatingFields;

    // Disclosure state of the step-2 sections: the essentials stay on screen, the rest is one click away.
    private bool _globalExpanded = true;
    private bool _groupExpanded = true;
    private bool _behaviourExpanded;

    /// <summary>Index of the point whose area the next two map clicks are drawing, or -1 when none is.</summary>
    private int _zonePickIndex = -1;
    private bool _zoneFirstCornerPlaced;
    private Vector2 _zoneFirstCorner;

    private bool _zoneDragActive;
    /// <summary>Index of the point whose area a drag is moving; 0 is the start of the route.</summary>
    private int _zoneDragIndex;
    private Vector2 _zoneDragGrab;
    private Vector2 _zoneCursorWorld;
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
        _addHumanRouteButton = root.Q<Button>("AddHumanRouteButton");
        _duplicateHumanRouteButton = root.Q<Button>("DuplicateHumanRouteButton");
        _removeHumanRouteButton = root.Q<Button>("RemoveHumanRouteButton");
        _setRouteStartButton = root.Q<Button>("SetRouteStartButton");
        _addRouteObjectiveButton = root.Q<Button>("AddRouteObjectiveButton");
        _toggleMapGridButton = root.Q<Button>("ToggleMapGridButton");
        _humanRouteSettings = root.Q<VisualElement>("HumanRouteSettings");
        _humanCountField = root.Q<IntegerField>("HumanCountField");
        _humanSpeedField = root.Q<FloatField>("HumanSpeedField");
        _endBehaviorDropdown = root.Q<DropdownField>("EndBehaviorDropdown");
        _movementControllerDropdown = root.Q<DropdownField>("MovementControllerDropdown");
        _formationDropdown = root.Q<DropdownField>("FormationDropdown");
        _groupSpacingField = root.Q<FloatField>("GroupSpacingField");
        _groupSpacingLabel = root.Q<Label>("GroupSpacingLabel");
        _groupSpacingHelp = root.Q<Label>("GroupSpacingHelp");
        _formationParameterField = root.Q<FloatField>("FormationParameterField");
        _formationParameterLabel = root.Q<Label>("FormationParameterLabel");
        _formationPreviewLabel = root.Q<Label>("FormationPreviewLabel");
        _sectionGlobalHeader = root.Q<Button>("SectionGlobalHeader");
        _sectionGroupHeader = root.Q<Button>("SectionGroupHeader");
        _sectionBehaviourHeader = root.Q<Button>("SectionBehaviourHeader");
        _sectionGlobalContent = root.Q<VisualElement>("SectionGlobalContent");
        _sectionGroupContent = root.Q<VisualElement>("SectionGroupContent");
        _sectionBehaviourContent = root.Q<VisualElement>("SectionBehaviourContent");
        _routePointsContainer = root.Q<VisualElement>("RoutePointsContainer");
        _canvas = root.Q<VisualElement>("AgentsEnvironmentCanvas");
        _mapImage = root.Q<Image>("AgentsEnvironmentImage");
        _mapPlaceholder = root.Q<Label>("AgentsMapPlaceholder");
        _instructionLabel = root.Q<Label>("PlacementInstructionLabel");
        _cursorCoordinatesLabel = root.Q<Label>("MapCursorCoordinatesLabel");
        _activeRouteLabel = root.Q<Label>("ActiveRouteLabel");
        _gridScaleLabel = root.Q<Label>("GridScaleLabel");
        _mapPathStatusLabel = root.Q<Label>("MapPathStatusLabel");
        VisualElement overlayHost = root.Q<VisualElement>("RouteOverlayHost");

        if (new VisualElement[]
            {
                _routeList, _addHumanRouteButton, _duplicateHumanRouteButton, _removeHumanRouteButton,
                _setRouteStartButton, _addRouteObjectiveButton, _toggleMapGridButton,
                _humanRouteSettings, _humanCountField, _humanSpeedField,
                _endBehaviorDropdown, _formationDropdown, _groupSpacingField,
                _groupSpacingLabel, _groupSpacingHelp,
                _movementControllerDropdown,
                _formationParameterField, _formationParameterLabel,
                _formationPreviewLabel,
                _sectionGlobalHeader, _sectionGroupHeader, _sectionBehaviourHeader,
                _sectionGlobalContent, _sectionGroupContent, _sectionBehaviourContent,
                _routePointsContainer, _canvas, _mapImage, _mapPlaceholder, _instructionLabel,
                _cursorCoordinatesLabel, _activeRouteLabel, _gridScaleLabel, _mapPathStatusLabel,
                overlayHost
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
        _duplicateHumanRouteButton.clicked += DuplicateActiveHumanRoute;
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
        _movementControllerDropdown.choices = new List<string>(MovementControllerChoices);
        _movementControllerDropdown.RegisterValueChangedCallback(evt =>
        {
            if (_updatingFields) return;
            PushUndo($"human-controller:{_activeRouteIndex}");
            UpdateHumanDraft(draft => draft.MovementController = ParseMovementControllerChoice(evt.newValue));
            RefreshFormationPreview();
        });
        _formationDropdown.RegisterValueChangedCallback(evt =>
        {
            if (_updatingFields) return;
            PushUndo($"human-formation:{_activeRouteIndex}");
            UpdateHumanDraft(draft =>
            {
                draft.Formation = FormationFromDisplay(evt.newValue);
                // A single file needs more room than a loose cluster: keep the spacing legal.
                draft.GroupSpacing = Mathf.Clamp(
                    draft.GroupSpacing,
                    GroupFormation.MinSpacing(draft.Formation),
                    3f);
            });
            RefreshActiveRoute();
            RefreshFormationPreview();
        });
        _groupSpacingField.RegisterValueChangedCallback(evt =>
        {
            float floor = ActiveRoute != null
                ? GroupFormation.MinSpacing(ActiveRoute.Formation)
                : 0.4f;
            float value = Mathf.Clamp(evt.newValue, floor, 3f);
            if (!Mathf.Approximately(value, evt.newValue)) _groupSpacingField.SetValueWithoutNotify(value);
            if (_updatingFields) return;
            PushUndo($"human-group-spacing:{_activeRouteIndex}");
            UpdateHumanDraft(draft => draft.GroupSpacing = value);
            RefreshFormationPreview();
        });
        _formationParameterField.RegisterValueChangedCallback(evt =>
        {
            if (_updatingFields) return;
            PushUndo($"human-formation-parameter:{_activeRouteIndex}");
            UpdateHumanDraft(draft => draft.FormationParameter = Mathf.Max(0f, evt.newValue));
            RefreshFormationPreview();
        });

        // Step 2 shows the essentials and keeps the rest one click away.
        _sectionGlobalHeader.userData = _sectionGlobalHeader.text;
        _sectionGroupHeader.userData = _sectionGroupHeader.text;
        _sectionBehaviourHeader.userData = _sectionBehaviourHeader.text;
        _sectionGlobalHeader.clicked += () => { _globalExpanded = !_globalExpanded; ApplySectionState(); };
        _sectionGroupHeader.clicked += () => { _groupExpanded = !_groupExpanded; ApplySectionState(); };
        _sectionBehaviourHeader.clicked += () => { _behaviourExpanded = !_behaviourExpanded; ApplySectionState(); };
        ApplySectionState();
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
        => BuildRouteVisuals(markAllActive: true);

    /// <summary>
    /// One drawable line per route, plus a dimmed second line for the return leg of a looping route: the author
    /// sees that the agents walk back to their start, and that it is not another route.
    /// </summary>
    private List<OccupancyMapRouteOverlay.RouteVisual> BuildRouteVisuals(bool markAllActive)
    {
        var visuals = new List<OccupancyMapRouteOverlay.RouteVisual>(_routes.Count);
        for (int index = 0; index < _routes.Count; index++)
        {
            RouteDraft route = _routes[index];
            List<Vector2> points = GetDisplayPoints(index);
            List<bool> markers = BuildMarkerMask(route, points);
            Color color = RouteColor(index);
            bool active = markAllActive || index == _activeRouteIndex;
            int loopStart = LoopReturnStartOf(index);

            if (loopStart <= 0 || loopStart >= points.Count)
            {
                visuals.Add(new OccupancyMapRouteOverlay.RouteVisual(
                    points,
                    color,
                    active,
                    route.Id,
                    IsPathPlanningActive,
                    markers));
                continue;
            }

            visuals.Add(new OccupancyMapRouteOverlay.RouteVisual(
                points.GetRange(0, loopStart),
                color,
                active,
                route.Id,
                IsPathPlanningActive,
                markers.GetRange(0, loopStart)));

            Color faded = color;
            faded.a *= 0.5f;
            visuals.Add(new OccupancyMapRouteOverlay.RouteVisual(
                points.GetRange(loopStart - 1, points.Count - loopStart + 1),
                faded,
                active,
                $"{route.Id} · loop back",
                IsPathPlanningActive,
                markers.GetRange(loopStart - 1, points.Count - loopStart + 1)));
        }

        return visuals;
    }

    /// <summary>
    /// Which points of a drawn polyline get a marker. A point that falls inside an objective area gets none: the
    /// objective is an area, so the rectangle is what the author has to see, not a dot drawn inside it. The same
    /// holds for a scattered start: an arrival is a point OR an area, and one of the two has to be the visible one.
    /// </summary>
    private static List<bool> BuildMarkerMask(RouteDraft route, IReadOnlyList<Vector2> points)
    {
        var mask = new List<bool>(points.Count);
        for (int index = 0; index < points.Count; index++)
            mask.Add(!IsInsideAnyArea(route, points[index]));
        return mask;
    }

    private static bool IsInsideAnyArea(RouteDraft route, Vector2 point)
    {
        // SpawnZone survives a switch back to a fixed start, so the flag decides, not the leftover rectangle.
        if (route.SpawnRandom && IsUsableZone(route.SpawnZone) && route.SpawnZone.Contains(point))
            return true;

        for (int index = 1; index < route.Points.Count; index++)
        {
            Rect? zone = route.ZoneAt(index);
            if (zone.HasValue && zone.Value.Contains(point))
                return true;
        }

        return false;
    }

    private int LoopReturnStartOf(int routeIndex) =>
        _plannedGeometry.TryGetValue(routeIndex, out PlannedGeometry geometry) &&
        geometry.Epoch == _navigationGridEpoch
            ? geometry.LoopReturnStart
            : -1;

    /// <summary>
    /// Every route as plain data, for the dry run of the validation step.
    /// The robot speed lives in the tab controller, so it is passed in for the robot route.
    /// </summary>
    public List<ScenarioDryRun.Route> BuildDryRunRoutes(float robotSpeed)
    {
        var routes = new List<ScenarioDryRun.Route>(_routes.Count);
        for (int index = 0; index < _routes.Count; index++)
        {
            RouteDraft draft = _routes[index];
            bool skipped = !draft.IsRobot && draft.Count <= 0;
            if (skipped)
                continue;
            // The dry run measures the route the agents will walk, not the straight line between
            // waypoints, so an obstacle detour shows up in the estimated walking time.
            routes.Add(new ScenarioDryRun.Route(
                draft.Id,
                GetDisplayPoints(index),
                draft.IsRobot ? robotSpeed : draft.Speed));
        }
        return routes;
    }

    public void Reset()
    {
        _routes.Clear();
        _plannedGeometry.Clear();
        var robot = new RouteDraft
        {
            Id = "Robot route",
            IsRobot = true,
            RouteModified = true
        };
        AppendPoint(robot, Vector2.zero);
        AppendPoint(robot, new Vector2(2f, 0f));
        _routes.Add(robot);
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
        robot.PointZones.Clear();
        robot.SpawnRandom = false;
        robot.SpawnZone = default;
        if (TryResolveReference(scenario, scenario.Robot?.StartRef, out Vector2 robotStart))
            AppendPoint(robot, robotStart);
        if (scenario.Robot?.WaypointRefs != null)
        {
            foreach (string waypointRef in scenario.Robot.WaypointRefs)
                if (TryResolveReference(scenario, waypointRef, out Vector2 waypoint)) AppendPoint(robot, waypoint);
        }
        if (TryResolveReference(scenario, scenario.Robot?.GoalRef, out Vector2 robotGoal))
            AppendPoint(robot, robotGoal);
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
        bool changed = _mapTexture != texture || _mapBounds != bounds;
        _mapTexture = texture;
        _mapBounds = bounds;
        if (changed)
            RebuildNavigationGrids();

        _mapImage.image = texture;
        _mapPlaceholder.EnableInClassList(HiddenClass, texture != null);
        RefreshOverlay();
    }

    /// <summary>True when the drawn routes are planned on the walkable pixels instead of straight lines.</summary>
    public bool IsPathPlanningActive => _navigationGrid != null && _navigationGrid.IsValid;

    private void InvalidateNavigationGrid()
    {
        _navigationGridEpoch++;
        _plannedGeometry.Clear();
        _navigationGrid = null;
        _interactiveGrid = null;
        _occupancyGrid = null;
    }

    /// <summary>
    /// Samples the occupancy image once into the raw grid (walls, used to validate authored points), then
    /// derives the fine planning grid and the coarse one used during a drag from it.
    /// </summary>
    private void RebuildNavigationGrids()
    {
        InvalidateNavigationGrid();
        if (_mapTexture == null)
            return;

        // The pixel mask makes the validation exact: a point five centimetres from a wall is valid, even though
        // the coarse planning cell that contains it is marked as an obstacle.
        _occupancyGrid = OccupancyGrid.FromTexture(
            _mapTexture,
            _mapBounds,
            NavigationResolution,
            keepPixelMask: true);
        if (_occupancyGrid == null)
            return;

        _navigationGrid = _occupancyGrid.WithObstaclesInflatedBy(NavigationAgentRadius);

        OccupancyGrid coarse = OccupancyGrid.FromTexture(
            _mapTexture,
            _mapBounds,
            InteractiveNavigationResolution);
        _interactiveGrid = coarse?.WithObstaclesInflatedBy(NavigationAgentRadius);
    }

    /// <summary>
    /// Drawable geometry of a route: the authored waypoints joined by the shortest walkable path, so the
    /// map shows the route the global planner will produce rather than a line running through a wall.
    ///
    /// While a point is being dragged the raw waypoints are used, because replanning on every mouse move
    /// would make the drag stutter; the planned path returns as soon as the point is released.
    /// </summary>
    private List<Vector2> GetDisplayPoints(int routeIndex)
    {
        if (routeIndex < 0 || routeIndex >= _routes.Count)
            return new List<Vector2>();

        RouteDraft route = _routes[routeIndex];
        // While a point is dragged the coarse grid is used, so the trajectory follows the mouse instead of
        // staying frozen until the point is released; the exact geometry is rebuilt on the fine grid then.
        bool interactive = _dragActive && routeIndex == _dragRouteIndex && _interactiveGrid != null;
        OccupancyGrid grid = interactive ? _interactiveGrid : _navigationGrid;
        bool looping = IsLooping(route);
        if (grid == null || !grid.IsValid)
            return BuildUnplannedGeometry(route, routeIndex, looping);

        int signature = PointsSignature(route.Points);
        if (_plannedGeometry.TryGetValue(routeIndex, out PlannedGeometry cached) &&
            cached.Epoch == _navigationGridEpoch &&
            cached.Signature == signature &&
            cached.Interactive == interactive &&
            cached.Looping == looping)
            return cached.Points;

        List<Vector2> planned = OccupancyPathPlanner.PlanRoute(
            grid,
            route.Points,
            out int fallbackSegments);
        int loopReturnStart = AppendLoopReturn(grid, route, planned, looping);
        _plannedGeometry[routeIndex] = new PlannedGeometry
        {
            Epoch = _navigationGridEpoch,
            Signature = signature,
            Interactive = interactive,
            Looping = looping,
            Points = planned,
            FallbackSegments = fallbackSegments,
            CrossingSegments = CountWallsCrossed(planned),
            LoopReturnStart = loopReturnStart
        };
        return planned;
    }

    /// <summary>True when the route walks back to its first point once it reached the last one.</summary>
    private static bool IsLooping(RouteDraft route) =>
        route != null && route.EndBehavior == HumanEndBehavior.Loop && route.Points.Count >= 2;

    /// <summary>
    /// Adds the leg that brings a looping route back to its start, planned on the same grid as the rest, so the
    /// editor shows the whole trip the agents will walk instead of stopping at the last objective.
    /// Returns the index in <paramref name="points"/> where that leg begins, or -1 when there is none.
    /// </summary>
    private static int AppendLoopReturn(
        OccupancyGrid grid,
        RouteDraft route,
        List<Vector2> points,
        bool looping)
    {
        if (!looping || points.Count < 2)
            return -1;

        Vector2 start = route.Points[0];
        Vector2 end = route.Points[^1];
        if ((start - end).sqrMagnitude < 0.0001f)
            return -1;

        int loopStart = points.Count - 1;
        List<Vector2> back = OccupancyPathPlanner.Plan(grid, end, start);
        if (back == null || back.Count < 2)
        {
            AppendPoint(points, start);
            return loopStart;
        }

        for (int index = 1; index < back.Count; index++)
            AppendPoint(points, back[index]);

        return loopStart;
    }

    /// <summary>Geometry of a route when no walkable grid is available: straight legs, plus the loop return.</summary>
    private List<Vector2> BuildUnplannedGeometry(RouteDraft route, int routeIndex, bool looping)
    {
        var points = new List<Vector2>(route.Points);
        int loopStart = -1;
        if (looping && (route.Points[0] - route.Points[^1]).sqrMagnitude > 0.0001f)
        {
            loopStart = points.Count - 1;
            points.Add(route.Points[0]);
        }

        _plannedGeometry[routeIndex] = new PlannedGeometry
        {
            Epoch = _navigationGridEpoch,
            Signature = PointsSignature(route.Points),
            Points = points,
            Looping = looping,
            LoopReturnStart = loopStart
        };
        return points;
    }

    private static void AppendPoint(List<Vector2> points, Vector2 point)
    {
        if (points.Count > 0 && (points[^1] - point).sqrMagnitude < 0.000001f)
            return;

        points.Add(point);
    }

    /// <summary>
    /// How many drawn segments cross a dark pixel of the occupancy image. The clearance grid keeps agents off
    /// the walls while planning, but a leg the planner could not solve is drawn straight, and an authored
    /// point standing on an obstacle starts its leg inside one: both have to be visible to the author.
    /// </summary>
    private int CountWallsCrossed(IReadOnlyList<Vector2> points)
    {
        if (_occupancyGrid == null || !_occupancyGrid.IsValid || points == null || points.Count < 2)
            return 0;

        int crossings = 0;
        for (int index = 1; index < points.Count; index++)
        {
            if (!OccupancyPathPlanner.HasLineOfSight(_occupancyGrid, points[index - 1], points[index]))
                crossings++;
        }

        return crossings;
    }

    private static int PointsSignature(IReadOnlyList<Vector2> points)
    {
        unchecked
        {
            int hash = 17;
            for (int index = 0; index < points.Count; index++)
            {
                hash = hash * 31 + Mathf.RoundToInt(points[index].x * 100f);
                hash = hash * 31 + Mathf.RoundToInt(points[index].y * 100f);
            }

            return hash;
        }
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

        // A point standing on an obstacle is the mistake that shows up as an agent spawning inside a wall:
        // the occupancy grid can catch it before the scenario is saved.
        error ??= FindPointOffWalkableGround();

        // Checking only the anchor is not enough: a five-agent wedge around a valid anchor can put three of
        // them inside the wall next to it.
        error ??= FindSpawnSlotOnWall();

        // A random placement area the runtime cannot sample would silently fall back to the nearest walkable
        // pixel, which is not where the author drew it.
        error ??= FindAreaWithoutWalkableGround();

        return error == null;
    }

    /// <summary>
    /// Every area an author can draw must contain walkable ground, or the agents would all be projected onto the
    /// nearest walkable pixel from an area that has none.
    /// </summary>
    private string FindAreaWithoutWalkableGround()
    {
        if (_occupancyGrid == null || !_occupancyGrid.IsValid)
            return null;

        foreach (RouteDraft route in _routes)
        {
            if (route.IsRobot || route.Count <= 0)
                continue;

            if (route.SpawnRandom && IsUsableZone(route.SpawnZone) &&
                DescribeAreaCoverage(route.Id, "start", route.SpawnZone) is string startProblem)
                return startProblem;

            for (int index = 1; index < route.Points.Count; index++)
            {
                Rect? zone = route.ZoneAt(index);
                if (zone.HasValue && IsUsableZone(zone.Value) &&
                    DescribeAreaCoverage(route.Id, RouteMapHitTesting.PointLabel(index), zone.Value)
                        is string pointProblem)
                    return pointProblem;
            }
        }

        return null;
    }

    /// <summary>
    /// Why an area cannot host the agents it promises, or null when it can. An area drawn over a wall is the
    /// mistake that turns a crowd into a heap: every agent the runtime cannot place is projected onto the nearest
    /// free pixel, so a zone that is mostly wall collapses its whole crowd onto the same strip.
    /// </summary>
    private string DescribeAreaCoverage(string routeId, string what, Rect zone)
    {
        float coverage = AreaWalkableCoverage(zone);
        if (coverage <= 0f)
            return $"{routeId} {what} area contains no walkable ground.";
        if (coverage < MinimumAreaCoverage)
            return $"{routeId} {what} area is mostly wall ({coverage:P0} walkable): the agents will be pushed " +
                   "onto the free strip instead of spreading over the area you drew.";

        return null;
    }

    /// <summary>Fraction of a coarse grid across an area that an agent can stand on.</summary>
    private float AreaWalkableCoverage(Rect zone)
    {
        const int Samples = 5;
        int walkable = 0;
        for (int row = 0; row < Samples; row++)
        {
            for (int column = 0; column < Samples; column++)
            {
                var sample = new Vector2(
                    Mathf.Lerp(zone.xMin, zone.xMax, column / (Samples - 1f)),
                    Mathf.Lerp(zone.yMin, zone.yMax, row / (Samples - 1f)));
                if (GroundAt(sample) == PointGround.Walkable)
                    walkable++;
            }
        }

        return (float)walkable / (Samples * Samples);
    }

    /// <summary>
    /// Every spawn slot the scenario will lay out, in world space, exactly as the runtime computes them: one
    /// formation per ungrouped route, and one shared formation for all the routes of a group.
    /// </summary>
    private List<(string Label, Vector2 Point)> BuildSpawnSlots()
    {
        var slots = new List<(string Label, Vector2 Point)>();
        var groupsDone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < _routes.Count; index++)
        {
            RouteDraft route = _routes[index];
            if (route.IsRobot || route.Count <= 0 || route.Points.Count < 2)
                continue;

            string groupId = route.Group?.Trim();
            // Independent agents draw their own point inside the start area, so there is no slot to check here;
            // the start area itself is validated like every other area.
            if (GroupFormation.IsScatter(route.Formation))
                continue;

            if (string.IsNullOrEmpty(groupId))
            {
                AppendSlots(slots, route.Id, route.Points[0], route, index, route.Count, 1);
                continue;
            }

            if (!groupsDone.Add(groupId))
                continue;

            // A group walks as one formation, so its members share the layout of the first route declaring it.
            int total = RoutesOfGroup(groupId).Sum(member => Mathf.Max(0, member.Count));
            AppendSlots(slots, $"group {groupId}", route.Points[0], route, index, total, 1);
        }

        return slots;
    }

    /// <summary>Adds the slots of one formation, including the anchor itself, to the validation list.</summary>
    private void AppendSlots(
        List<(string Label, Vector2 Point)> slots,
        string label,
        Vector2 origin,
        RouteDraft route,
        int routeIndex,
        int total,
        int firstAgentNumber)
    {
        if (total <= 1)
        {
            slots.Add(($"{label} agent {firstAgentNumber}", origin));
            return;
        }

        List<Vector2> offsets = GroupFormation.CreateSlots(
            total,
            route.GroupSpacing,
            route.Formation,
            route.FormationParameter);
        Vector2 heading = InitialDirection(GetDisplayPoints(routeIndex), route.Points, origin);
        float angle = heading.sqrMagnitude > 0.0001f ? Mathf.Atan2(heading.x, heading.y) : 0f;

        for (int index = 0; index < offsets.Count; index++)
            slots.Add(($"{label} agent {index + 1}", origin + GroupFormation.Rotate(offsets[index], angle)));
    }

    /// <summary>First spawn slot standing on an obstacle, as a message, or null when the formations fit.</summary>
    private string FindSpawnSlotOnWall()
    {
        if (_occupancyGrid == null || !_occupancyGrid.IsValid)
            return null;

        foreach ((string label, Vector2 point) in BuildSpawnSlots())
        {
            if (GroundAt(point) != PointGround.Wall)
                continue;

            return $"{label} spawns on a wall ({point.x:0.##}, {point.y:0.##}). " +
                   "Move the start of the route or pick a tighter formation.";
        }

        return null;
    }

    private int CountSpawnSlotsOffWalkableGround()
    {
        if (_occupancyGrid == null || !_occupancyGrid.IsValid)
            return 0;

        int count = 0;
        foreach ((string _, Vector2 point) in BuildSpawnSlots())
            if (GroundAt(point) != PointGround.Walkable)
                count++;

        return count;
    }

    /// <summary>
    /// First route point that is not on walkable space, as a message the editor can show, or null when every
    /// point stands on walkable pixels. Routes without a readable occupancy grid are not judged.
    /// </summary>
    private string FindPointOffWalkableGround()
    {
        if (_occupancyGrid == null || !_occupancyGrid.IsValid)
            return null;

        foreach (RouteDraft route in _routes)
        {
            if (!route.IsRobot && route.Count <= 0)
                continue;
            for (int index = 0; index < route.Points.Count; index++)
            {
                Vector2 point = route.Points[index];
                PointGround ground = GroundAt(point);
                if (ground == PointGround.Walkable)
                    continue;

                string label = RouteMapHitTesting.PointLabel(index);
                string place = ground == PointGround.Wall ? "is on a wall" : "is outside the map";
                return $"{route.Id}: {label} ({point.x:0.##}, {point.y:0.##}) {place}. " +
                       "Move it onto the walkable area.";
            }
        }

        return null;
    }

    /// <summary>How a world point sits on the map, judged on the exact pixels of the occupancy image.</summary>
    private enum PointGround
    {
        Walkable,
        Wall,
        OutsideMap
    }

    private PointGround GroundAt(Vector2 point)
    {
        if (_occupancyGrid == null || !_occupancyGrid.IsValid)
            return PointGround.Walkable;
        if (_occupancyGrid.IsPixelWalkable(point))
            return PointGround.Walkable;

        return _occupancyGrid.TryWorldToCell(point, out _, out _)
            ? PointGround.Wall
            : PointGround.OutsideMap;
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
            config.Spawn.Spacing = Mathf.Clamp(
                draft.GroupSpacing,
                GroupFormation.MinSpacing(config.Spawn.Formation),
                3f);
            config.Spawn.FormationParameter = Mathf.Max(0f, draft.FormationParameter);
            // An inherited controller is written as an absent block, so the HumanConfig asset keeps deciding.
            config.MovementController = string.IsNullOrWhiteSpace(draft.MovementController)
                ? null
                : new MovementControllerConfig { Type = draft.MovementController.Trim() };

            if (draft.Source == null || draft.RouteModified)
            {
                EnsureMinimumPoints(draft);
                NormalizeZones(draft);
                config.Spawn ??= new SpawnConfig();
                config.Goal ??= new GoalConfig();
                string safeId = ToFileId(config.Id);

                if (draft.SpawnRandom && IsUsableZone(draft.SpawnZone))
                    WriteZoneSpawn(config.Spawn, draft.SpawnZone);
                else
                {
                    SetSpawnReference(config.Spawn, $"{safeId}_start");
                    WritePoint(scenario, config.Spawn.Reference, draft.Points[0]);
                }

                WriteGoal(scenario, config.Goal, $"{safeId}_goal_1", draft.Points[1], draft.ZoneAt(1));

                var additionalGoals = new List<GoalConfig>();
                for (int goalIndex = 2; goalIndex < draft.Points.Count; goalIndex++)
                {
                    GoalConfig goal = config.Goals != null && goalIndex - 2 < config.Goals.Count
                        ? config.Goals[goalIndex - 2]
                        : new GoalConfig();
                    WriteGoal(scenario, goal, $"{safeId}_goal_{goalIndex}", draft.Points[goalIndex], draft.ZoneAt(goalIndex));
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
                route.FormationParameter = reference.FormationParameter;
                route.MovementController = reference.MovementController;
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

    /// <summary>
    /// Adds a copy of the active route. A crowd is built by tuning one route and repeating it — the same start
    /// area crossed from another side, a second flow through the same corridor — so the copy has to carry the
    /// whole route: points, areas, formation, end behaviour and speed.
    /// </summary>
    private void DuplicateActiveHumanRoute()
    {
        RouteDraft active = ActiveRoute;
        if (active == null || active.IsRobot)
            return;

        InsertRouteCopy(CaptureRoute(active));
    }

    /// <summary>Keeps the active route on the editor clipboard; Ctrl+V turns it into a new route.</summary>
    private void CopyActiveRoute()
    {
        RouteDraft active = ActiveRoute;
        if (active == null || active.IsRobot)
            return;

        _routeClipboard = CaptureRoute(active);
        _instructionLabel.text = $"{active.Id} copied. Ctrl+V adds a copy.";
    }

    private void PasteRoute()
    {
        if (_routeClipboard == null)
        {
            _instructionLabel.text = "Nothing to paste: copy a route with Ctrl+C first.";
            return;
        }

        InsertRouteCopy(_routeClipboard);
    }

    private void InsertRouteCopy(RouteSnapshot source)
    {
        if (source == null)
            return;

        PushUndo();
        RouteDraft copy = CreateDraftFromSnapshot(source);
        copy.Id = NextRouteCopyId(source.Id);
        // The copy starts as a new route: saving it as a fresh entry is the point of duplicating.
        copy.RouteModified = true;
        copy.Source = null;
        _routes.Add(copy);
        _activeRouteIndex = _routes.Count - 1;
        _pendingPointIndex = 0;
        RebuildRouteList();
        RefreshActiveRoute();
    }

    /// <summary>
    /// Name of the copy: "human_route_1" gives "human_route_1_copy", then "_copy2". A copy of a copy keeps one
    /// suffix instead of stacking them, and the new id is checked against the routes already in the editor.
    /// </summary>
    private string NextRouteCopyId(string sourceId)
    {
        string root = Regex.Replace(string.IsNullOrWhiteSpace(sourceId) ? "human_route" : sourceId.Trim(),
            "_copy\\d*$", string.Empty, RegexOptions.IgnoreCase);
        string candidate = root + "_copy";
        int suffix = 2;
        while (_routes.Any(route => string.Equals(route.Id, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{root}_copy{suffix}";
            suffix++;
        }

        return candidate;
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
        AppendPoint(active, anchor + Vector2.right);
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
        _duplicateHumanRouteButton.SetEnabled(!active.IsRobot);
        _humanRouteSettings.EnableInClassList(HiddenClass, active.IsRobot);
        _pendingPointIndex = Mathf.Clamp(_pendingPointIndex, 0, Mathf.Max(0, active.Points.Count - 1));

        _updatingFields = true;
        if (!active.IsRobot)
        {
            _humanCountField.SetValueWithoutNotify(active.Count);
            _humanSpeedField.SetValueWithoutNotify(active.Speed);
            SetEndBehaviorChoices();
            _endBehaviorDropdown.SetValueWithoutNotify(HumanEndBehaviorParser.ToDisplayName(active.EndBehavior));
            _movementControllerDropdown.SetValueWithoutNotify(MovementControllerToDisplay(active.MovementController));
            SetFormationChoices();
            _formationDropdown.SetValueWithoutNotify(FormationToDisplay(active.Formation));
            _groupSpacingField.SetValueWithoutNotify(active.GroupSpacing);
            UpdateFormationParameterField(active);
        }
        _updatingFields = false;

        RefreshRouteListSelection();
        RebuildPointRows();
        SetPlacementInstruction();
        RefreshOverlay();
    }

    // ==========================================
    //   STEP 2 DISCLOSURE AND POINT AREAS
    // ==========================================

    /// <summary>
    /// A collapsed section is one click away rather than gone: the chevron and the header carry the state, so
    /// the author always knows there is more to see.
    /// </summary>
    private void ApplySectionState()
    {
        ApplySection(_sectionGlobalHeader, _sectionGlobalContent, _globalExpanded);
        ApplySection(_sectionGroupHeader, _sectionGroupContent, _groupExpanded);
        ApplySection(_sectionBehaviourHeader, _sectionBehaviourContent, _behaviourExpanded);
    }

    private static void ApplySection(Button header, VisualElement content, bool expanded)
    {
        if (header == null || content == null)
            return;

        content.EnableInClassList(HiddenClass, !expanded);
        header.EnableInClassList("expanded", expanded);
        string title = header.userData as string ?? header.text;
        header.text = expanded ? $"▾  {title}" : $"▸  {title}";
    }

    private static void WriteZoneFields(ZoneFields fields, Rect zone)
    {
        fields.CenterX?.SetValueWithoutNotify(RoundCoordinate(zone.center.x));
        fields.CenterZ?.SetValueWithoutNotify(RoundCoordinate(zone.center.y));
        fields.SizeX?.SetValueWithoutNotify(RoundCoordinate(zone.width));
        fields.SizeZ?.SetValueWithoutNotify(RoundCoordinate(zone.height));
    }

    /// <summary>Keeps the numeric fields in step with an area being dragged on the map.</summary>
    private void SyncZoneFieldsNoNotify()
    {
        RouteDraft active = ActiveRoute;
        if (active == null)
            return;

        int index = _zoneDragIndex;
        if (_pointZoneFields.TryGetValue(index, out ZoneFields fields))
            WriteZoneFields(fields, PointArea(active, index) ?? default);
        UpdateAreaBadge(index);
    }

    /// <summary>
    /// The area of one point, or null when that point is fixed. Index 0 is the start of the route, so its area is
    /// the spawn area, and every other index is the arrival area of one objective. A point is one or the other and
    /// never both, which is why one accessor answers for a whole row.
    /// </summary>
    private static Rect? PointArea(RouteDraft route, int index)
    {
        if (route == null || index < 0 || index >= route.Points.Count)
            return null;

        return index == 0
            ? (route.SpawnRandom ? route.SpawnZone : (Rect?)null)
            : route.ZoneAt(index);
    }

    /// <summary>Writes an area back to its point, or turns that point back into a fixed point.</summary>
    private static void SetPointArea(RouteDraft route, int index, Rect? area)
    {
        if (index == 0)
        {
            route.SpawnRandom = area.HasValue;
            if (area.HasValue)
                route.SpawnZone = area.Value;
        }
        else
        {
            NormalizeZones(route);
            route.PointZones[index] = area;
        }

        // A point is a point OR an area: while it is an area the route runs to the centre of that area.
        if (area.HasValue)
            route.Points[index] = area.Value.center;
    }

    private void RegisterZoneFields(int index, ZoneFields fields)
    {
        void OnChanged(ChangeEvent<float> evt)
        {
            if (_updatingFields) return;
            PushUndo($"zone:{_activeRouteIndex}:{index}");
            ApplyZoneFields(index);
        }

        fields.CenterX?.RegisterValueChangedCallback(OnChanged);
        fields.CenterZ?.RegisterValueChangedCallback(OnChanged);
        fields.SizeX?.RegisterValueChangedCallback(OnChanged);
        fields.SizeZ?.RegisterValueChangedCallback(OnChanged);
    }

    /// <summary>Rebuilds one area from its four numeric fields, so typing a value moves exactly its edge.</summary>
    private void ApplyZoneFields(int index)
    {
        RouteDraft active = ActiveRoute;
        if (active == null || active.IsRobot)
            return;

        if (!_pointZoneFields.TryGetValue(index, out ZoneFields fields))
            return;

        Rect zone = ZoneFromCenterAndSize(
            new Vector2(fields.CenterX.value, fields.CenterZ.value),
            new Vector2(fields.SizeX.value, fields.SizeZ.value));

        SetPointArea(active, index, zone);
        UpdateAreaBadge(index);

        active.RouteModified = true;
        CancelZonePick();
        RefreshOverlay();
    }

    private void UpdateAreaBadge(int index)
    {
        if (!_pointAreaLabels.TryGetValue(index, out Label badge))
            return;

        Rect? area = PointArea(ActiveRoute, index);
        badge.text = area.HasValue && IsUsableZone(area.Value)
            ? DescribeArea(area.Value)
            : "Draw the area on the map";
    }

    private static string DescribeArea(Rect area) => $"Area {area.width:0.#} x {area.height:0.#} m";

    /// <summary>Starts the two-click gesture that draws the area of one point.</summary>
    private void BeginZonePick(int index)
    {
        RouteDraft active = ActiveRoute;
        if (active == null || active.IsRobot || index < 0 || index >= active.Points.Count)
            return;

        _pendingPointIndex = index;
        UpdatePointSelectionHighlight();
        _zonePickIndex = index;
        _zoneFirstCornerPlaced = false;
        _instructionLabel.text =
            $"{RouteMapHitTesting.PointLabel(index)}: click the first corner of the area, then the opposite one. Esc cancels.";
        RefreshOverlay();
    }

    private void CancelZonePick()
    {
        _zonePickIndex = -1;
        _zoneFirstCornerPlaced = false;
    }

    /// <summary>Applies one corner of the area being drawn; the second one closes the rectangle.</summary>
    private void HandleZonePick(Vector2 worldPosition)
    {
        RouteDraft active = ActiveRoute;
        if (active == null || _zonePickIndex < 0)
            return;

        if (!_zoneFirstCornerPlaced)
        {
            _zoneFirstCorner = worldPosition;
            _zoneFirstCornerPlaced = true;
            _instructionLabel.text = "Now click the opposite corner of the area. Esc cancels.";
            RefreshOverlay();
            return;
        }

        Rect zone = ZoneFromCorners(_zoneFirstCorner, worldPosition);
        if (!IsUsableZone(zone))
        {
            _instructionLabel.text = "That area is too small: click two corners at least half a metre apart.";
            _zoneFirstCornerPlaced = false;
            return;
        }

        PushUndo($"zone-draw:{_activeRouteIndex}");
        SetPointArea(active, _zonePickIndex, zone);

        active.RouteModified = true;
        CancelZonePick();
        RefreshActiveRoute();
    }

    /// <summary>
    /// Grabbing the inside of an area drags the whole area: the author moves it where the agents should appear
    /// instead of retyping four numbers.
    /// </summary>
    private bool TryBeginZoneDrag(Vector2 worldPosition)
    {
        RouteDraft active = ActiveRoute;
        if (active == null || active.IsRobot)
            return false;

        int index = _zonePickIndex >= 0 ? _zonePickIndex : _pendingPointIndex;
        Rect? area = PointArea(active, index);
        if (!area.HasValue || !IsUsableZone(area.Value) || !area.Value.Contains(worldPosition))
            return false;

        _zoneDragActive = true;
        _zoneDragIndex = index;
        _zoneDragGrab = worldPosition;
        _dragUndoSnapshot = PushUndo($"zone-move:{_activeRouteIndex}");
        return true;
    }

    private void MoveDraggedZone(Vector2 worldPosition)
    {
        RouteDraft active = ActiveRoute;
        if (active == null)
            return;

        Vector2 delta = worldPosition - _zoneDragGrab;
        Rect? area = PointArea(active, _zoneDragIndex);
        if (!area.HasValue)
            return;

        SetPointArea(
            active,
            _zoneDragIndex,
            new Rect(area.Value.x + delta.x, area.Value.y + delta.y, area.Value.width, area.Value.height));

        _zoneDragGrab = worldPosition;
        active.RouteModified = true;
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
    }

    /// <summary>Recomputes the sentence describing the active formation, then the map that draws its slots.</summary>
    private void RefreshFormationPreview()
    {
        UpdateFormationPreviewLabel();
        // The map draws the formation slots, so a formation or spacing change must repaint it
        // immediately instead of waiting for the next point move.
        RefreshOverlay();
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
    }

    private void RefreshRouteListSelection()
    {
        for (int index = 0; index < _routeList.childCount && index < _routes.Count; index++)
            _routeList[index].EnableInClassList("selected", index == _activeRouteIndex);
    }

    /// <summary>Routes that share a group id walk as one formation, which the spawn preview has to respect.</summary>
    private List<RouteDraft> RoutesOfGroup(string groupId) => _routes
        .Where(route => !route.IsRobot &&
                        string.Equals(route.Group?.Trim(), groupId, StringComparison.OrdinalIgnoreCase))
        .ToList();

    private static string DescribeRoute(RouteDraft route)
    {
        if (route.IsRobot)
            return "Robot route";
        int agents = Mathf.Max(0, route.Count);
        int objectives = Mathf.Max(0, route.Points.Count - 1);
        int areas = 0;
        for (int index = 1; index < route.Points.Count; index++)
            if (route.ZoneAt(index).HasValue)
                areas++;

        var parts = new List<string>(4)
        {
            route.Id,
            $"{agents} agent{(agents == 1 ? string.Empty : "s")}",
            $"{objectives} objective{(objectives == 1 ? string.Empty : "s")}"
        };
        if (areas > 0)
            parts.Add($"{areas} area{(areas == 1 ? string.Empty : "s")}");

        return string.Join(" · ", parts);
    }

    private void RebuildPointRows()
    {
        _routePointsContainer.Clear();
        _pointRows.Clear();
        _pointFields.Clear();
        _pointZoneFields.Clear();
        _pointAreaLabels.Clear();
        RouteDraft active = ActiveRoute;
        if (active == null)
            return;

        for (int index = 0; index < active.Points.Count; index++)
        {
            int capturedIndex = index;
            Vector2 point = active.Points[index];
            Rect? area = PointArea(active, index);
            var row = new VisualElement();
            row.AddToClassList("route-point-row");
            row.EnableInClassList("selected", index == _pendingPointIndex);

            var header = new VisualElement();
            header.AddToClassList("route-point-row-header");
            var name = new Label(RouteMapHitTesting.PointLabel(index));
            name.AddToClassList("route-point-name");
            header.Add(name);

            // The two states of a point live side by side here: the button names the one it will switch to.
            var zoneButton = new Button(() => TogglePointArea(capturedIndex))
            {
                text = area.HasValue ? "Area" : "Point"
            };
            zoneButton.AddToClassList("route-point-zone-button");
            zoneButton.EnableInClassList("on", area.HasValue);
            zoneButton.tooltip = area.HasValue
                ? "Turn this area back into one exact point"
                : "Let each agent draw its own point inside an area";
            header.Add(zoneButton);
            row.Add(header);

            // Coordinates and row actions share one line, so a route of six points still fits without a scrollbar.
            var line = new VisualElement();
            line.AddToClassList("route-point-line");
            if (area.HasValue)
            {
                // An area shows its size instead of the coordinates of a point the author no longer has.
                var summary = new Label(IsUsableZone(area.Value) ? DescribeArea(area.Value) : "Draw the area on the map");
                summary.AddToClassList("route-point-area-summary");
                _pointAreaLabels[index] = summary;
                line.Add(summary);
            }
            else
            {
                FloatField xField = CreateCoordinateField("X", point.x);
                FloatField zField = CreateCoordinateField("Z", point.y);
                xField.RegisterValueChangedCallback(evt => SetPointCoordinate(capturedIndex, true, evt.newValue));
                zField.RegisterValueChangedCallback(evt => SetPointCoordinate(capturedIndex, false, evt.newValue));
                line.Add(xField);
                line.Add(zField);
                _pointFields[index] = new CoordinateFields { X = xField, Z = zField };
            }

            var actions = new VisualElement();
            actions.AddToClassList("route-point-actions");
            if (index > 0)
            {
                Button up = CreateSmallButton("↑", () => MovePoint(capturedIndex, -1));
                Button down = CreateSmallButton("↓", () => MovePoint(capturedIndex, 1));
                Button remove = CreateSmallButton("×", () => RemovePoint(capturedIndex));
                up.SetEnabled(index > 1);
                down.SetEnabled(index < active.Points.Count - 1);
                remove.SetEnabled(active.Points.Count > 2);
                actions.Add(up);
                actions.Add(down);
                actions.Add(remove);
            }
            line.Add(actions);
            row.Add(line);

            if (area.HasValue)
            {
                // The exact numbers stay available for an author who wants a precise area; the map gesture is one
                // click away. Both used to live in the left panel, away from the point they describe.
                var fields = new ZoneFields
                {
                    CenterX = CreateZoneField("Center X"),
                    CenterZ = CreateZoneField("Center Z"),
                    SizeX = CreateZoneField("Size X"),
                    SizeZ = CreateZoneField("Size Z")
                };
                var grid = new VisualElement();
                grid.AddToClassList("route-point-zone-grid");
                grid.Add(fields.CenterX);
                grid.Add(fields.CenterZ);
                grid.Add(fields.SizeX);
                grid.Add(fields.SizeZ);
                _pointZoneFields[index] = fields;
                RegisterZoneFields(index, fields);
                WriteZoneFields(fields, area.Value);
                row.Add(grid);

                var draw = new Button(() => BeginZonePick(capturedIndex)) { text = "Draw area on map" };
                draw.AddToClassList("route-point-draw-button");
                row.Add(draw);
            }

            row.RegisterCallback<PointerDownEvent>(_ => SelectPoint(capturedIndex));
            _pointRows.Add(row);
            _routePointsContainer.Add(row);
        }
    }

    /// <summary>
    /// Turns one point into an area around it, or back into the exact point at the centre of that area. A point is
    /// one or the other, so the two states never show up together on the map.
    /// </summary>
    private void TogglePointArea(int index)
    {
        RouteDraft active = ActiveRoute;
        if (active == null || active.IsRobot || index < 0 || index >= active.Points.Count)
            return;

        PushUndo($"point-area:{_activeRouteIndex}:{index}");
        Rect? area = PointArea(active, index);
        if (area.HasValue)
        {
            // Back to a point, on the centre of the area the author was looking at, so it does not jump.
            active.Points[index] = area.Value.center;
            SetPointArea(active, index, null);
        }
        else
        {
            Rect created = DefaultZoneAround(active.Points[index]);
            active.Points[index] = created.center;
            SetPointArea(active, index, created);
        }

        active.RouteModified = true;
        active.HasNonSpatialGoal = false;
        _pendingPointIndex = index;
        CancelZonePick();
        RefreshActiveRoute();
        RefreshRouteList();
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

    private static FloatField CreateZoneField(string label)
    {
        var field = new FloatField(label);
        field.AddToClassList("creation-text-field");
        field.AddToClassList("route-zone-field");
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
            SetPointPosition(active, index, isX ? new Vector2(value, point.y) : new Vector2(point.x, value));
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
        SwapPoints(active, index, targetIndex);
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
        RemovePointAt(active, index);
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
    /// Exposes the one value the selected formation tunes — a wedge opening, a row stagger, a cluster
    /// radius, a pair front spacing — and hides the field for the formations that have nothing to tune.
    /// An empty field (zero) means "use the natural default", which the label spells out.
    /// </summary>
    private void UpdateFormationParameterField(RouteDraft active)
    {
        bool hasParameter = active != null && GroupFormation.HasParameter(active.Formation);
        _formationParameterLabel.EnableInClassList(HiddenClass, !hasParameter);
        _formationParameterField.EnableInClassList(HiddenClass, !hasParameter);
        if (!hasParameter)
            return;

        _formationParameterLabel.text = GroupFormation.DescribeParameter(active.Formation, active.GroupSpacing);
        _formationParameterField.SetValueWithoutNotify(active.FormationParameter);
    }

    /// <summary>
    /// The controller dropdown exposes display labels while the YAML uses short values; "Inherit from config"
    /// is written as an absent value so the HumanConfig asset keeps deciding.
    /// </summary>
    private static string MovementControllerToDisplay(string value)
    {
        return HumanMovementControllerParser.TryParse(value, out MovementControllerType controller)
            ? HumanMovementControllerParser.ToDisplayName(controller)
            : InheritControllerChoice;
    }

    private static string ParseMovementControllerChoice(string display)
    {
        if (string.IsNullOrWhiteSpace(display) ||
            string.Equals(display.Trim(), InheritControllerChoice, StringComparison.OrdinalIgnoreCase))
            return null;

        return HumanMovementControllerParser.TryParse(display, out MovementControllerType controller)
            ? HumanMovementControllerParser.ToYamlValue(controller)
            : null;
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
            if (formation == "scatter" || formation == "none" || formation == "independent") return "Independent";
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
            if (formation == "independent") return GroupFormation.ScatterFormation;
        }

        return "pair";
    }

    /// <summary>
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

        // While an area is being drawn, every click belongs to that area.
        if (_zonePickIndex >= 0)
        {
            HandleZonePick(worldPosition);
            return;
        }

        // A point or a route wins over the area it may sit in: the small targets must stay reachable, and only a
        // click on empty space inside an area grabs the area itself.
        if (TrySelectPointAt(localPosition))
            return;

        if (TrySelectRouteAt(localPosition))
            return;

        if (TryBeginZoneDrag(worldPosition))
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

        // While an area is being drawn, the rectangle from the first corner to the cursor follows the pointer.
        if (onMap && _zonePickIndex >= 0 && _zoneFirstCornerPlaced)
        {
            _zoneCursorWorld = worldPosition;
            RefreshOverlay();
        }

        if (!_dragging)
        {
            if (_zoneDragActive && onMap)
            {
                MoveDraggedZone(worldPosition);
                SyncZoneFieldsNoNotify();
                RefreshOverlay();
            }
            return;
        }
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
        _zoneDragActive = false;
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
        if (control && evt.keyCode == KeyCode.C)
        {
            CopyActiveRoute();
            evt.StopPropagation();
            return;
        }
        if (control && evt.keyCode == KeyCode.V)
        {
            PasteRoute();
            evt.StopPropagation();
            return;
        }
        if (control && evt.keyCode == KeyCode.D)
        {
            DuplicateActiveHumanRoute();
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
            if (_zoneDragActive)
                _zoneDragActive = false;
            CancelZonePick();
            SetPlacementInstruction();
            RefreshOverlay();
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
        SetPointPosition(active, _pendingPointIndex, moved);
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
        SetPointPosition(route, _dragPointIndex, new Vector2(x, z));
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
        _overlay.SetRoutes(BuildRouteVisuals(markAllActive: false));
        _overlay.SetFormations(BuildFormationPreviews());
        // Built once and shared: the overlay paints the rectangles, the label layer names them.
        List<OccupancyMapRouteOverlay.ZoneVisual> zones = BuildZoneVisuals();
        _overlay.SetZones(zones);
        UpdateGridScaleLabel();
        UpdatePathStatusLabel();
        UpdateFormationPreviewLabel();
        RefreshPointLabels(zones);
    }

    /// <summary>
    /// Tells the author whether the drawn trajectories are planned on the walkable pixels or straight
    /// lines, and reports the routes the grid cannot connect.
    /// </summary>
    private void UpdatePathStatusLabel()
    {
        if (_navigationGrid == null || !_navigationGrid.IsValid)
        {
            _mapPathStatusLabel.text = "Paths: straight lines (no readable occupancy grid)";
            _mapPathStatusLabel.EnableInClassList("warning", false);
            return;
        }

        int blocked = 0;
        int crossing = 0;
        for (int index = 0; index < _routes.Count; index++)
        {
            if (_routes[index].Points.Count < 2)
                continue;
            if (!_plannedGeometry.TryGetValue(index, out PlannedGeometry geometry) ||
                geometry.Epoch != _navigationGridEpoch)
                continue;

            if (geometry.FallbackSegments > 0)
                blocked++;
            if (geometry.CrossingSegments > 0)
                crossing++;
        }

        int onWalls = CountPointsOffWalkableGround();
        int wallSlots = CountSpawnSlotsOffWalkableGround();
        var notes = new List<string>(2);
        if (onWalls > 0)
            notes.Add($"{onWalls} point{(onWalls == 1 ? string.Empty : "s")} on a wall");
        if (wallSlots > 0)
            notes.Add($"{wallSlots} spawn slot{(wallSlots == 1 ? string.Empty : "s")} off walkable space");
        string wallNote = notes.Count == 0 ? string.Empty : " · " + string.Join(" · ", notes);
        string pathNote;
        if (blocked > 0)
            pathNote = $"Paths: {blocked} route(s) cannot reach a point on the walkable grid";
        else if (crossing > 0)
            pathNote = $"Paths: {crossing} route(s) cross a wall";
        else
            pathNote = "Paths: shortest walkable route (walls avoided)";

        _mapPathStatusLabel.text = pathNote + wallNote;
        _mapPathStatusLabel.EnableInClassList(
            "warning",
            blocked > 0 || crossing > 0 || onWalls > 0 || wallSlots > 0);
    }

    private int CountPointsOffWalkableGround()
    {
        if (_occupancyGrid == null || !_occupancyGrid.IsValid)
            return 0;

        int count = 0;
        foreach (RouteDraft route in _routes)
        {
            if (!route.IsRobot && route.Count <= 0)
                continue;
            foreach (Vector2 point in route.Points)
                if (GroundAt(point) != PointGround.Walkable)
                    count++;
        }

        return count;
    }

    /// <summary>
    /// The placement areas of the active route, as the overlay draws them: the spawn area of the route, the
    /// arrival areas of its objectives, and the rubber band the author is currently dragging.
    /// </summary>
    private List<OccupancyMapRouteOverlay.ZoneVisual> BuildZoneVisuals()
    {
        _zoneVisuals.Clear();
        RouteDraft active = ActiveRoute;
        if (active == null || active.IsRobot || _mapTexture == null)
            return _zoneVisuals;

        Color color = RouteColor(_activeRouteIndex);
        if (active.SpawnRandom && IsUsableZone(active.SpawnZone))
        {
            _zoneVisuals.Add(new OccupancyMapRouteOverlay.ZoneVisual(
                active.SpawnZone,
                color,
                active: true,
                label: "Spawn area"));
        }

        for (int index = 1; index < active.Points.Count; index++)
        {
            Rect? zone = active.ZoneAt(index);
            if (!zone.HasValue || !IsUsableZone(zone.Value))
                continue;

            _zoneVisuals.Add(new OccupancyMapRouteOverlay.ZoneVisual(
                zone.Value,
                color,
                active: index == _pendingPointIndex,
                label: $"{RouteMapHitTesting.PointLabel(index)} area"));
        }

        if (_zonePickIndex >= 0 && _zoneFirstCornerPlaced)
        {
            Rect preview = ZoneFromCorners(_zoneFirstCorner, _zoneCursorWorld);
            if (IsUsableZone(preview))
                _zoneVisuals.Add(new OccupancyMapRouteOverlay.ZoneVisual(preview, color, active: true, label: string.Empty));
        }

        return _zoneVisuals;
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

            // Independent agents have no slots to draw: where they appear is drawn from the zone at run time.
            if (GroupFormation.IsScatter(route.Formation))
                continue;

            List<Vector2> slots = GroupFormation.CreateSlots(
                route.Count,
                route.GroupSpacing,
                route.Formation,
                route.FormationParameter);
            Vector2 origin = route.Points[0];
            Vector2 direction = InitialDirection(GetDisplayPoints(index), route.Points, origin);
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

    /// <summary>
    /// Heading the group faces at spawn: the first leg of the drawn path, so a group standing in front of
    /// a wall faces the direction it will actually walk instead of pointing through the wall.
    /// </summary>
    private static Vector2 InitialDirection(
        IReadOnlyList<Vector2> drawn,
        IReadOnlyList<Vector2> authored,
        Vector2 origin)
    {
        if (drawn != null)
        {
            for (int index = 1; index < drawn.Count; index++)
            {
                Vector2 candidate = drawn[index] - origin;
                if (candidate.sqrMagnitude > 0.0025f)
                    return candidate;
            }
        }

        return authored != null && authored.Count > 1 ? authored[1] - origin : Vector2.zero;
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

        bool independent = GroupFormation.IsScatter(active.Formation);

        // Independent agents keep no shape, so the two fields that describe a shape say what they do instead.
        _groupSpacingLabel.text = independent ? "Minimum spacing (m)" : "Spacing (m)";
        _groupSpacingHelp.text = independent
            ? "Shortest distance kept between two independent agents when they appear."
            : "Distance kept between members of the same formation.";

        if (active.Count <= 1)
        {
            _formationPreviewLabel.text = independent
                ? "One agent: it draws its own start and arrival inside the areas."
                : "One agent: no formation to lay out.";
            return;
        }

        if (independent)
        {
            _formationPreviewLabel.text = active.SpawnRandom
                ? $"{active.Count} independent agents · each one draws its own point in the start area, " +
                  $"and its own arrival in every goal area, at least {active.GroupSpacing:0.##} m apart."
                : $"{active.Count} independent agents share the start point: give the start an area so each " +
                  "one draws its own point.";
            return;
        }

        _formationPreviewLabel.text =
            $"{active.Count} agents · {FormationToDisplay(active.Formation).ToLowerInvariant()} · " +
            $"{active.GroupSpacing:0.##} m — slots shown on the start point" +
            DescribeActiveParameter(active) + ".";
    }

    /// <summary>Names the tuned value of the formation, so the picture and the numbers agree.</summary>
    private static string DescribeActiveParameter(RouteDraft active)
    {
        float resolved = GroupFormation.ResolveParameter(
            active.Formation,
            active.GroupSpacing,
            active.FormationParameter);
        string label = GroupFormation.DescribeParameter(active.Formation, active.GroupSpacing);
        if (label.StartsWith("Single file", StringComparison.Ordinal))
            return string.Empty;

        string unit = label.StartsWith("Opening angle", StringComparison.Ordinal) ? "°" : " m";
        return $", {resolved:0.##}{unit}";
    }

    private void RefreshPointLabels(IReadOnlyList<OccupancyMapRouteOverlay.ZoneVisual> zones)
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

        // Placement areas get their name from the same layer: Painter2D draws no text, so the overlay paints the
        // rectangle and the label layer names it.
        foreach (OccupancyMapRouteOverlay.ZoneVisual zone in zones)
        {
            if (string.IsNullOrEmpty(zone.Label))
                continue;

            Vector2 corner = WorldToCanvas(new Vector2(zone.WorldRect.xMin, zone.WorldRect.yMax));
            var label = new Label(zone.Label);
            label.AddToClassList("map-zone-label");
            label.EnableInClassList("active", zone.Active);
            label.pickingMode = PickingMode.Ignore;
            label.style.left = corner.x + 4f;
            label.style.top = corner.y + 4f;
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

        if (_zonePickIndex >= 0)
        {
            _instructionLabel.text = _zoneFirstCornerPlaced
                ? $"{RouteMapHitTesting.PointLabel(_zonePickIndex)}: click the opposite corner of the area. Esc cancels."
                : $"{RouteMapHitTesting.PointLabel(_zonePickIndex)}: click the first corner of the area. Esc cancels.";
            return;
        }

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
            Routes = _routes.Select(CaptureRoute).ToList(),
            ActiveRouteIndex = _activeRouteIndex,
            PendingPointIndex = _pendingPointIndex
        };
    }

    private static RouteSnapshot CaptureRoute(RouteDraft route)
    {
        return new RouteSnapshot
        {
            Id = route.Id,
            IsRobot = route.IsRobot,
            Count = route.Count,
            Speed = route.Speed,
            EndBehavior = route.EndBehavior,
            Group = route.Group,
            MovementController = route.MovementController,
            Formation = route.Formation,
            GroupSpacing = route.GroupSpacing,
            FormationParameter = route.FormationParameter,
            Points = new List<Vector2>(route.Points),
            SpawnRandom = route.SpawnRandom,
            SpawnZone = route.SpawnZone,
            PointZones = new List<Rect?>(route.PointZones),
            Source = route.Source,
            RouteModified = route.RouteModified,
            HasNonSpatialGoal = route.HasNonSpatialGoal
        };
    }

    private static RouteDraft CreateDraftFromSnapshot(RouteSnapshot route)
    {
        var draft = new RouteDraft
        {
            Id = route.Id,
            IsRobot = route.IsRobot,
            Count = route.Count,
            Speed = route.Speed,
            EndBehavior = route.EndBehavior,
            Group = route.Group,
            MovementController = route.MovementController,
            Formation = route.Formation,
            GroupSpacing = route.GroupSpacing,
            FormationParameter = route.FormationParameter,
            SpawnRandom = route.SpawnRandom,
            SpawnZone = route.SpawnZone,
            Source = route.Source,
            RouteModified = route.RouteModified,
            HasNonSpatialGoal = route.HasNonSpatialGoal
        };
        draft.Points.AddRange(route.Points);
        draft.PointZones.AddRange(route.PointZones ?? new List<Rect?>());
        NormalizeZones(draft);
        return draft;
    }

    private void RestoreSnapshot(EditorSnapshot snapshot)
    {
        if (snapshot == null)
            return;

        _routes.Clear();
        foreach (RouteSnapshot route in snapshot.Routes)
            _routes.Add(CreateDraftFromSnapshot(route));

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
        var draft = new RouteDraft
        {
            Id = $"Human route {index}",
            Count = 0,
            RouteModified = true
        };
        AppendPoint(draft, new Vector2(2f, 0f));
        AppendPoint(draft, Vector2.zero);
        return draft;
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
            MovementController = human.MovementController?.Type,
            Formation = string.IsNullOrWhiteSpace(human.Spawn?.Formation) ? "pair" : human.Spawn.Formation.Trim().ToLowerInvariant(),
            GroupSpacing = human.Spawn != null ? Mathf.Max(0.4f, human.Spawn.Spacing) : 1.5f,
            FormationParameter = human.Spawn != null ? Mathf.Max(0f, human.Spawn.FormationParameter) : 0f,
            SpawnRandom = IsRandomSpawn(human.Spawn),
            SpawnZone = ReadZone(human.Spawn?.Zone) ?? default,
            Source = human,
            RouteModified = false
        };

        // The start of the route stays the point the author sees; the area around it is what the runtime scatters
        // the group in, so a zone-shaped spawn falls back to its centre here.
        AppendPoint(draft, ResolveSpawnPosition(scenario, human.Spawn));
        if (IsSpatialGoal(human.Goal))
            AppendPoint(draft, ResolveGoalPosition(scenario, human.Goal), GoalZone(human.Goal));
        else
            draft.HasNonSpatialGoal = human.Goal != null;
        if (human.Goals != null)
        {
            foreach (GoalConfig goal in human.Goals.Where(IsSpatialGoal))
                AppendPoint(draft, ResolveGoalPosition(scenario, goal), GoalZone(goal));
        }
        if (draft.Points.Count < 2)
            AppendPoint(draft, draft.Points[0] + Vector2.right * 2f);
        NormalizeZones(draft);
        return draft;
    }

    private static Rect? GoalZone(GoalConfig goal) => IsRandomGoal(goal) ? ReadZone(goal?.Zone) : null;

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

    private static bool IsRandomGoal(GoalConfig goal) =>
        goal != null && string.Equals(goal.Type?.Trim(), "random", StringComparison.OrdinalIgnoreCase);

    private static bool IsRandomSpawn(SpawnConfig spawn) =>
        string.Equals(spawn?.Type?.Trim(), "random", StringComparison.OrdinalIgnoreCase);

    // ==========================================
    //          POINT AND AREA BOOKKEEPING
    // ==========================================

    /// <summary>
    /// Every mutation of the point list goes through these helpers: <see cref="RouteDraft.PointZones"/> is
    /// parallel to <see cref="RouteDraft.Points"/>, and an area landing on the wrong objective would be a
    /// silent authoring bug rather than a visible one.
    /// </summary>
    private static void AppendPoint(RouteDraft draft, Vector2 point, Rect? zone = null)
    {
        draft.Points.Add(point);
        draft.PointZones.Add(zone);
    }

    private static void InsertPointAt(RouteDraft draft, int index, Vector2 point, Rect? zone = null)
    {
        index = Mathf.Clamp(index, 0, draft.Points.Count);
        draft.Points.Insert(index, point);
        draft.PointZones.Insert(index, zone);
    }

    private static void RemovePointAt(RouteDraft draft, int index)
    {
        if (index < 0 || index >= draft.Points.Count)
            return;

        draft.Points.RemoveAt(index);
        if (index < draft.PointZones.Count)
            draft.PointZones.RemoveAt(index);
    }

    private static void SwapPoints(RouteDraft draft, int first, int second)
    {
        (draft.Points[first], draft.Points[second]) = (draft.Points[second], draft.Points[first]);
        if (first < draft.PointZones.Count && second < draft.PointZones.Count)
        {
            (draft.PointZones[first], draft.PointZones[second]) =
                (draft.PointZones[second], draft.PointZones[first]);
        }
    }

    /// <summary>Brings the area list back to the length of the point list after a load or an undo.</summary>
    private static void NormalizeZones(RouteDraft draft)
    {
        while (draft.PointZones.Count < draft.Points.Count)
            draft.PointZones.Add(null);

        if (draft.PointZones.Count > draft.Points.Count)
            draft.PointZones.RemoveRange(draft.Points.Count, draft.PointZones.Count - draft.Points.Count);
    }

    /// <summary>World XZ rectangle of an authored area, or null when the reference is a plain point.</summary>
    private static Rect? ReadZone(RefPoint reference)
    {
        if (reference == null || !reference.IsBounds)
            return null;

        Bounds bounds = reference.ToBounds();
        if (bounds.size.x <= 0f || bounds.size.z <= 0f)
            return null;

        return Rect.MinMaxRect(
            bounds.min.x, bounds.min.z,
            bounds.max.x, bounds.max.z);
    }

    /// <summary>Writes a world XZ rectangle back as the centre and size a scenario stores.</summary>
    private static RefPoint WriteZone(Rect zone)
    {
        var center = new Vector3(zone.center.x, 0f, zone.center.y);
        var size = new Vector3(zone.width, 0f, zone.height);
        return RefPoint.FromBounds(center, size);
    }

    /// <summary>Normalised rectangle from two corners the author clicked on the map.</summary>
    private static Rect ZoneFromCorners(Vector2 first, Vector2 second) =>
        Rect.MinMaxRect(
            Mathf.Min(first.x, second.x), Mathf.Min(first.y, second.y),
            Mathf.Max(first.x, second.x), Mathf.Max(first.y, second.y));

    /// <summary>A square area around a point, used when an objective is switched to a random arrival.</summary>
    private static Rect DefaultZoneAround(Vector2 center, float size = 4f) =>
        Rect.MinMaxRect(center.x - size * 0.5f, center.y - size * 0.5f,
                        center.x + size * 0.5f, center.y + size * 0.5f);

    /// <summary>An area the runtime can actually sample: a zero-size rectangle would pin the agent again.</summary>
    private static bool IsUsableZone(Rect zone) => zone.width >= 0.5f && zone.height >= 0.5f;

    /// <summary>Builds a positive-size rectangle from the centre and size an author typed.</summary>
    private static Rect ZoneFromCenterAndSize(Vector2 center, Vector2 size)
    {
        float width = Mathf.Max(0.5f, Mathf.Abs(size.x));
        float depth = Mathf.Max(0.5f, Mathf.Abs(size.y));
        return Rect.MinMaxRect(
            center.x - width * 0.5f, center.y - depth * 0.5f,
            center.x + width * 0.5f, center.y + depth * 0.5f);
    }

    /// <summary>
    /// Moves one route point and drags its arrival area along, so an area can never be left behind on the
    /// map by the point it belongs to.
    /// </summary>
    private static void SetPointPosition(RouteDraft draft, int index, Vector2 position)
    {
        if (index < 0 || index >= draft.Points.Count)
            return;

        Vector2 delta = position - draft.Points[index];
        draft.Points[index] = position;
        if (index < draft.PointZones.Count && draft.PointZones[index].HasValue)
        {
            Rect zone = draft.PointZones[index].Value;
            draft.PointZones[index] = new Rect(zone.x + delta.x, zone.y + delta.y, zone.width, zone.height);
        }
    }

    private static void EnsureMinimumPoints(RouteDraft draft)
    {
        if (draft.Points.Count == 0) AppendPoint(draft, Vector2.zero);
        if (draft.Points.Count == 1) AppendPoint(draft, draft.Points[0] + Vector2.right * 2f);
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

    /// <summary>
    /// Writes one objective. A fixed point keeps its named reference in the scenario's point table; an area
    /// becomes a random goal carrying its zone inline, and its reference is dropped so the two can never
    /// disagree about where the agent is going.
    /// </summary>
    private static void WriteGoal(
        ScenarioData scenario,
        GoalConfig goal,
        string fallbackReference,
        Vector2 point,
        Rect? zone)
    {
        if (zone.HasValue)
        {
            goal.Type = "random";
            goal.Zone = WriteZone(zone.Value);
            goal.Position = null;
            goal.Target = null;
            goal.Reference = null;
            return;
        }

        SetGoalReference(goal, fallbackReference);
        WritePoint(scenario, goal.Reference, point);
    }

    /// <summary>Writes the spawn as an area: the runtime draws one walkable point inside it per run.</summary>
    private static void WriteZoneSpawn(SpawnConfig spawn, Rect zone)
    {
        spawn.Type = "random";
        spawn.Zone = WriteZone(zone);
        spawn.Position = null;
        spawn.Reference = null;
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
}
