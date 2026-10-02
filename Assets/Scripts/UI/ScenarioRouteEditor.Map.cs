using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RobotSNAP.Agents;
using RobotSNAP.Core.Scenario;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The map surface: the overlay it draws, the labels it places, the zones an author draws on
/// it, and every pointer, drag and keyboard gesture the surface understands.</summary>
public sealed partial class ScenarioRouteEditor
{

    /// <summary>Starts the two-click gesture that draws the area of one point.</summary>
    private void BeginZonePick(int index)
    {
        RouteDraft active = ActiveRoute;
        if (active == null || index < 0 || index >= active.Points.Count)
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
    /// The area a press at that world point grabs, with the point that owns it. A press outside every usable area
    /// of the active route grabs nothing, so the caller can still move a point or place the pending one.
    /// </summary>
    private bool TryGetZoneAt(Vector2 worldPosition, out int index, out Rect area)
    {
        index = -1;
        area = default;

        RouteDraft active = ActiveRoute;
        if (active == null)
            return false;

        int candidate = _zonePickIndex >= 0 ? _zonePickIndex : _pendingPointIndex;
        Rect? found = PointArea(active, candidate);
        if (!found.HasValue || !IsUsableZone(found.Value) || !found.Value.Contains(worldPosition))
            return false;

        index = candidate;
        area = found.Value;
        return true;
    }

    /// <summary>
    /// Grabbing the inside of an area drags the whole area: the author moves it where the agents should appear
    /// instead of retyping four numbers. The active route and the selected point stay as they are, so a press can
    /// select a point for the settings panel and then hand the drag over to the area that point belongs to.
    /// </summary>
    private bool TryBeginZoneDrag(Vector2 worldPosition)
    {
        if (!TryGetZoneAt(worldPosition, out int index, out Rect area))
            return false;

        BeginZoneDrag(index, area, worldPosition);
        return true;
    }

    private void BeginZoneDrag(int index, Rect area, Vector2 grabPoint)
    {
        _zoneDragActive = true;
        _zoneDragIndex = index;
        _zoneDragGrab = grabPoint;
        _zoneDragStartArea = area;
        _dragUndoSnapshot = PushUndo($"zone-move:{_activeRouteIndex}");
    }

    /// <summary>
    /// Moves the grabbed area with the same Blender/Photoshop helpers the points use: Ctrl rounds the centre of the
    /// rectangle onto the visible grid, Shift keeps only the dominant axis of the drag. The centre is what gets
    /// snapped because an area has no single authored point, and moving the centre keeps the drawn rectangle the
    /// exact size it had, which is what the author reads on screen.
    /// </summary>
    private void MoveDraggedZone(Vector2 worldPosition, MapPointerState pointer)
    {
        RouteDraft active = ActiveRoute;
        if (active == null)
            return;

        // The candidate is measured from the press rather than from the previous frame: a delta accumulated frame
        // by frame stays below one grid step, and the snapped centre would then never leave the step it sits on.
        Vector2 centre = _zoneDragStartArea.center;
        Vector2 target = centre + (worldPosition - _zoneDragGrab);
        Vector2 moved = ApplyPointerConstraints(target, pointer, centre);

        SetPointArea(
            active,
            _zoneDragIndex,
            new Rect(
                moved.x - _zoneDragStartArea.width * 0.5f,
                moved.y - _zoneDragStartArea.height * 0.5f,
                _zoneDragStartArea.width,
                _zoneDragStartArea.height));

        active.RouteModified = true;
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
        // click on empty space inside an area grabs the area itself. One exception: the anchor of an area sits on
        // that area's centre, so a press on it hits the point first even though the author only sees the
        // rectangle. That press keeps selecting the point for the settings panel and moves the area, exactly like
        // a press anywhere else inside the rectangle.
        if (TrySelectPointAt(localPosition))
        {
            if (TryGetZoneAt(worldPosition, out int zoneIndex, out Rect zoneArea))
            {
                EndPointDrag();
                BeginZoneDrag(zoneIndex, zoneArea, worldPosition);
            }
            return;
        }

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
            // The readout runs the same constraints as the movement it describes, so the "snap" and "axis lock"
            // hints tell where the grabbed thing is going; a zone drag measures its delta from the press point.
            Vector2 shown = worldPosition;
            if (_dragActive)
                shown = ApplyPointerConstraints(worldPosition, pointer);
            else if (_zoneDragActive)
                shown = ApplyPointerConstraints(worldPosition, pointer, _zoneDragGrab);

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
                MoveDraggedZone(worldPosition, pointer);
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
            DuplicateActiveRoute();
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
    /// Blender/Photoshop style helpers while placing or dragging a point, and while dragging an area:
    /// Ctrl snaps to the visible grid, Shift locks the movement on the dominant axis. The optional reference is
    /// the anchor the dominant axis is measured from and the coordinate the locked axis keeps; it defaults to the
    /// world position a point drag started at.
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
        if (active == null || _mapTexture == null)
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
}
