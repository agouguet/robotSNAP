using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RobotSNAP.Agents;
using RobotSNAP.Core.Scenario;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Where a route may walk and what the wizard refuses to save: the occupancy grids, the
/// planned geometry of a route, and the validation of a scenario before it is written.</summary>
public sealed partial class ScenarioRouteEditor
{

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
    /// Each route carries its own speed, robots included, because a scenario with several robots drives them at
    /// the speed its author gave each one.
    /// </summary>
    public List<ScenarioDryRun.Route> BuildDryRunRoutes()
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
                draft.Speed));
        }
        return routes;
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
        List<RouteDraft> robots = _routes.Where(route => route.IsRobot).ToList();
        if (robots.Count == 0)
            error = "The scenario needs at least one robot.";
        else
        {
            foreach (RouteDraft robot in robots)
            {
                if (robot.Points.Count < 2)
                {
                    error = $"{robot.Id} needs a start point and at least one objective.";
                    break;
                }
                if (robot.Speed <= 0f)
                {
                    error = $"{robot.Id} speed must be greater than zero.";
                    break;
                }
                if (HasRepeatedConsecutivePoint(robot))
                {
                    error = $"Consecutive points of {robot.Id} must be different.";
                    break;
                }
            }

            foreach (RouteDraft human in _routes.Where(route => !route.IsRobot && CarriesAgents(route)))
            {
                if (error != null)
                    break;
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
            // A robot route has no agent count to speak of, and it draws inside its areas exactly like a crowd.
            if (!route.IsRobot && route.Count <= 0)
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
}
