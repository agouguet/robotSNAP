using System.Collections.Generic;
using UnityEngine;

namespace RobotSNAP.Core.Scenario
{
    /// <summary>
    /// Walkable grid of the scenario being simulated, shared by the runtime and by the scenario editor.
    ///
    /// The editor draws its trajectories on the occupancy image with <see cref="OccupancyPathPlanner"/>, at
    /// <see cref="Resolution"/> cells and with <see cref="DefaultAgentRadius"/> metres of clearance kept around
    /// obstacles. The simulation builds the very same grid from the very same image with the same constants,
    /// so what the author validates on the map is what the agents actually walk. It is
    /// <see cref="RobotSNAP.Environment.EnvironmentBuilder"/> that builds it, from the image it also uses for
    /// the floor and the walls: one map, one grid. An environment made of a prefab or of an additive Unity
    /// scene has no grid at all, and its agents keep navigating the scene NavMesh.
    /// </summary>
    public static class ScenarioNavigation
    {
        /// <summary>Cell budget of the grid, shared with the editor: both sample the image the same way.</summary>
        public const int Resolution = 320;

        /// <summary>
        /// Body radius kept clear from walls, matched to the pedestrian radius of the simulation
        /// (<see cref="RobotSNAP.Agents.HumanConfig.agentRadius"/>).
        /// </summary>
        public const float DefaultAgentRadius = 0.25f;

        /// <summary>How far a waypoint may sit from walkable space and still be routed.</summary>
        public const float SnapRadius = OccupancyPathPlanner.DefaultSnapRadius;

        /// <summary>
        /// Global searches allowed per frame. A whole crowd replanning in the same frame would spike the frame
        /// time, so the surplus waits for the next frame and keeps its previous path meanwhile. Measured at
        /// 0.75 ms on a 289×289 grid, so the worst case stays under the budget of a 60 FPS frame.
        /// </summary>
        public const int DefaultSearchesPerFrame = 8;

        private static OccupancyGrid _obstacles;
        private static OccupancyGrid _walkable;
        private static OccupancyPathPlanner.Scratch _scratch;
        private static readonly PlanBudget SearchBudget = new PlanBudget(DefaultSearchesPerFrame);

        /// <summary>True when a walkable grid is loaded and can answer path queries.</summary>
        public static bool IsAvailable => _walkable != null && _walkable.IsValid;

        /// <summary>Grid with the agent clearance applied: the one paths are planned on.</summary>
        public static OccupancyGrid Walkable => _walkable;

        /// <summary>Raw obstacle grid, where only the pixels of the image count as walls.</summary>
        public static OccupancyGrid Obstacles => _obstacles;

        /// <summary>Per-frame search allowance, shared by every agent of the scenario.</summary>
        public static PlanBudget Budget => SearchBudget;

        /// <summary>Full searches run since the grid was built.</summary>
        public static int Searches { get; private set; }

        /// <summary>Legs answered by a straight walkable line, without any search.</summary>
        public static int StraightSegments { get; private set; }

        /// <summary>Plan requests postponed to a later frame because the frame budget was spent.</summary>
        public static int DeferredSearches { get; private set; }

        public static void ResetStats()
        {
            Searches = 0;
            StraightSegments = 0;
            DeferredSearches = 0;
        }

        /// <summary>Builds the runtime grid from the occupancy image of a scenario.</summary>
        public static void BuildFrom(Texture2D texture, Bounds bounds, float agentRadius = DefaultAgentRadius)
        {
            Clear();
            if (texture == null)
                return;

            _obstacles = OccupancyGrid.FromTexture(texture, bounds, Resolution);
            if (_obstacles == null)
                return;

            _walkable = _obstacles.WithObstaclesInflatedBy(agentRadius);
            _scratch = new OccupancyPathPlanner.Scratch();
            ResetStats();
        }

        /// <summary>Drops the grid, e.g. when the scenario is unloaded.</summary>
        public static void Clear()
        {
            _obstacles = null;
            _walkable = null;
            _scratch = null;
            SearchBudget.Reset();
        }

        /// <summary>True when a world point stands on walkable pixels, clearance included.</summary>
        public static bool IsWalkable(Vector2 world) => IsAvailable && _walkable.IsWorldWalkable(world);

        /// <summary>
        /// Nearest walkable point within <paramref name="radius"/> metres, or false when there is none.
        /// Used to pull a spawn slot, or a waypoint dropped against a wall, back onto the walkable area.
        /// </summary>
        public static bool TryProjectToWalkable(Vector2 point, float radius, out Vector2 walkable)
        {
            return TryProjectToWalkable(_walkable, point, radius, out walkable);
        }

        /// <summary>
        /// Nearest walkable point of an explicit grid, within <paramref name="radius"/> metres.
        ///
        /// The grid of the scenario keeps a pedestrian clear of the walls; a robot with a wider footprint needs
        /// a grid grown for its own radius, which its caller derives from <see cref="Obstacles"/> and hands
        /// here. One grid at a time, because the ring search is the only place that knows where a body may
        /// stand.
        /// </summary>
        public static bool TryProjectToWalkable(OccupancyGrid grid, Vector2 point, float radius, out Vector2 walkable)
        {
            walkable = point;
            if (grid == null || !grid.IsValid)
                return false;
            if (grid.IsWorldWalkable(point))
                return true;
            if (radius <= 0f)
                return false;
            if (!TryClampedWorldToCell(grid, point, out int originX, out int originY))
                return false;

            float cellSize = Mathf.Max(0.0001f, Mathf.Min(grid.CellSizeX, grid.CellSizeZ));
            int range = Mathf.Max(1, Mathf.CeilToInt(radius / cellSize));
            float best = float.PositiveInfinity;
            bool found = false;

            for (int ring = 1; ring <= range; ring++)
            {
                // A point found on the previous ring is closer than anything the next one can hold.
                if (found && (ring - 1) * cellSize > best)
                    break;

                for (int offsetY = -ring; offsetY <= ring; offsetY++)
                {
                    for (int offsetX = -ring; offsetX <= ring; offsetX++)
                    {
                        // Perimeter of the ring only: the inside was already visited.
                        if (Mathf.Abs(offsetX) != ring && Mathf.Abs(offsetY) != ring)
                            continue;

                        int cellX = originX + offsetX;
                        int cellY = originY + offsetY;
                        if (!grid.IsWalkable(cellX, cellY))
                            continue;

                        Vector2 candidate = grid.CellCenter(cellX, cellY);
                        float distance = Vector2.Distance(point, candidate);
                        if (distance > radius || distance >= best)
                            continue;

                        best = distance;
                        walkable = candidate;
                        found = true;
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Path of the global planner between two world points, honouring the per-frame search budget.
        ///
        /// Both ends are first brought back onto walkable ground, so the returned polyline never starts or ends
        /// inside a wall. The legs a straight walkable line already covers are answered without any search: in a
        /// crowd walking an open corridor this is the overwhelming majority of the requests, and running A* for
        /// a one-metre leg is wasted work.
        ///
        /// Returns false when the grid is missing, the points cannot be brought back onto walkable ground, or
        /// the frame budget is spent — the caller then keeps its previous path and asks again next frame.
        /// </summary>
        public static bool TryPlan(Vector2 from, Vector2 to, List<Vector2> into)
        {
            into?.Clear();
            if (into == null)
                return TryPlan(from, to, out _);
            if (!IsAvailable)
                return false;
            if (!TryProjectToWalkable(from, SnapRadius, out Vector2 start))
                return false;
            if (!TryProjectToWalkable(to, SnapRadius, out Vector2 goal))
                return false;

            if (OccupancyPathPlanner.HasLineOfSight(_walkable, start, goal))
            {
                StraightSegments++;
                into.Add(start);
                into.Add(goal);
                return true;
            }

            if (!SearchBudget.TryConsume(Time.frameCount))
            {
                DeferredSearches++;
                return false;
            }

            List<Vector2> planned = OccupancyPathPlanner.Plan(_walkable, start, goal, _scratch);
            if (planned == null || planned.Count < 2)
                return false;

            Searches++;
            into.AddRange(planned);
            return true;
        }

        /// <summary>Same as <see cref="TryPlan(Vector2,Vector2,List{Vector2})"/>, allocating the result.</summary>
        public static bool TryPlan(Vector2 from, Vector2 to, out List<Vector2> path)
        {
            path = new List<Vector2>();
            if (TryPlan(from, to, path))
                return true;

            path = null;
            return false;
        }

        /// <summary>Path of the global planner, or null when there is none. Ignores the frame budget.</summary>
        public static List<Vector2> Plan(Vector2 from, Vector2 to)
        {
            if (!IsAvailable)
                return null;
            if (!TryProjectToWalkable(from, SnapRadius, out Vector2 start))
                return null;
            if (!TryProjectToWalkable(to, SnapRadius, out Vector2 goal))
                return null;

            return OccupancyPathPlanner.Plan(_walkable, start, goal, _scratch);
        }

        /// <summary>
        /// Whole authored route projected on the walkable grid: each leg is the shortest walkable path, and the
        /// legs are stitched into one polyline. Load-time cost, so it is not budgeted.
        /// </summary>
        public static List<Vector2> PlanRoute(IReadOnlyList<Vector2> waypoints, out int fallbackSegments)
        {
            fallbackSegments = 0;
            var result = new List<Vector2>();
            if (waypoints == null || waypoints.Count == 0)
                return result;
            if (!IsAvailable)
            {
                fallbackSegments = Mathf.Max(0, waypoints.Count - 1);
                result.AddRange(waypoints);
                return result;
            }

            var projected = new List<Vector2>(waypoints.Count);
            foreach (Vector2 waypoint in waypoints)
                projected.Add(TryProjectToWalkable(waypoint, SnapRadius, out Vector2 snapped) ? snapped : waypoint);

            return OccupancyPathPlanner.PlanRoute(_walkable, projected, out fallbackSegments);
        }

        /// <summary>
        /// True while the agent still stands close enough to the polyline it received. A path is only replanned
        /// when the agent has been pushed aside — by avoidance, or by a wall it had to slide along — or when its
        /// destination moved.
        /// </summary>
        public static bool IsPathStillValid(Vector2 position, IReadOnlyList<Vector3> path, float tolerance)
        {
            if (path == null || path.Count < 2)
                return false;

            for (int index = 1; index < path.Count; index++)
            {
                Vector3 previous = path[index - 1];
                Vector3 current = path[index];
                float distance = DistanceToSegment(
                    position,
                    new Vector2(previous.x, previous.z),
                    new Vector2(current.x, current.z));
                if (distance <= tolerance)
                    return true;
            }

            return false;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 from, Vector2 to)
        {
            Vector2 segment = to - from;
            float lengthSqr = segment.sqrMagnitude;
            if (lengthSqr < 0.000001f)
                return Vector2.Distance(point, from);

            float t = Mathf.Clamp(Vector2.Dot(point - from, segment) / lengthSqr, 0f, 1f);
            return Vector2.Distance(point, from + segment * t);
        }

        /// <summary>Cell of a world point, clamped into the map so a point just outside is still usable.</summary>
        private static bool TryClampedWorldToCell(OccupancyGrid grid, Vector2 world, out int x, out int y)
        {
            x = 0;
            y = 0;
            if (grid == null || !grid.IsValid)
                return false;

            if (grid.TryWorldToCell(world, out x, out y))
                return true;

            Bounds bounds = grid.WorldBounds;
            var clamped = new Vector2(
                Mathf.Clamp(world.x, bounds.min.x, bounds.max.x),
                Mathf.Clamp(world.y, bounds.min.z, bounds.max.z));
            return grid.TryWorldToCell(clamped, out x, out y);
        }

        /// <summary>
        /// Frame allowance of searches. One instance per planner: the runtime shares the static one, a tool or a
        /// test can drive its own.
        /// </summary>
        public sealed class PlanBudget
        {
            private int _frame = -1;
            private int _used;

            public PlanBudget(int searchesPerFrame)
            {
                SearchesPerFrame = Mathf.Max(1, searchesPerFrame);
            }

            /// <summary>Searches allowed per frame.</summary>
            public int SearchesPerFrame { get; set; }

            /// <summary>Searches already run in the current frame.</summary>
            public int UsedThisFrame => _used;

            /// <summary>True when a search is still allowed; otherwise records the consumption of one.</summary>
            public bool TryConsume(int frame)
            {
                if (frame != _frame)
                {
                    _frame = frame;
                    _used = 0;
                }

                if (_used >= Mathf.Max(1, SearchesPerFrame))
                    return false;

                _used++;
                return true;
            }

            /// <summary>Forgets the current frame, e.g. after a teleport or a scenario reload.</summary>
            public void Reset()
            {
                _frame = -1;
                _used = 0;
            }
        }
    }
}
