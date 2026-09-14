using System.Collections.Generic;
using UnityEngine;

namespace RobotSNAP.Core.Scenario
{
    /// <summary>
    /// Plans a walkable path between two world points on an <see cref="OccupancyGrid"/>.
    ///
    /// This is the same contract the runtime global planner will need: eight-way A* over the walkable
    /// cells, no corner cutting, then a line-of-sight simplification so the drawn route follows walls
    /// instead of stair-stepping along the grid.
    /// </summary>
    public static class OccupancyPathPlanner
    {
        private const float StraightCost = 1f;
        private static readonly float DiagonalCost = Mathf.Sqrt(2f);

        /// <summary>How far a waypoint may sit from walkable space and still be routed.</summary>
        public const float DefaultSnapRadius = 0.6f;

        /// <summary>
        /// Shortest walkable path from <paramref name="start"/> to <paramref name="goal"/>.
        /// Returns null when no path exists — the caller reports it.
        ///
        /// A waypoint dropped within <paramref name="snapRadius"/> metres of walkable space (against a wall,
        /// or inside the band the agent radius reserves) is nudged onto the nearest walkable cell, while the
        /// returned polyline keeps the authored point exactly. A point further inside an obstacle is a real
        /// authoring mistake and still fails, so the caller can flag it.
        /// </summary>
        public static List<Vector2> Plan(
            OccupancyGrid grid,
            Vector2 start,
            Vector2 goal,
            bool simplify = true,
            bool snapToWalkable = true,
            float snapRadius = DefaultSnapRadius)
            => Plan(grid, start, goal, null, simplify, snapToWalkable, snapRadius);

        /// <summary>
        /// Reusable search buffers. A runtime agent replans every few hundred milliseconds, and allocating the
        /// four cell arrays of a 300×300 grid each time would produce megabytes of garbage per second.
        /// </summary>
        public sealed class Scratch
        {
            internal float[] G = System.Array.Empty<float>();
            internal float[] F = System.Array.Empty<float>();
            internal int[] CameFrom = System.Array.Empty<int>();
            internal byte[] State = System.Array.Empty<byte>();
            internal int Capacity;
            // Private: only the enclosing planner touches the search heap.
            private readonly MinHeap _open = new MinHeap();

            internal MinHeap Open => _open;

            /// <summary>Number of cells the buffers can hold: they grow once and are then reused.</summary>
            public int BufferCapacity => Capacity;
        }

        /// <summary>Same search as <see cref="Plan(OccupancyGrid,Vector2,Vector2,bool,bool,float)"/>, reusing buffers.</summary>
        public static List<Vector2> Plan(
            OccupancyGrid grid,
            Vector2 start,
            Vector2 goal,
            Scratch scratch,
            bool simplify = true,
            bool snapToWalkable = true,
            float snapRadius = DefaultSnapRadius)
        {
            if (grid == null || !grid.IsValid)
                return null;
            if (!grid.TryWorldToCell(start, out int startX, out int startY))
                return null;
            if (!grid.TryWorldToCell(goal, out int goalX, out int goalY))
                return null;

            if (snapToWalkable)
            {
                if (!grid.IsWalkable(startX, startY) &&
                    !TryFindWalkableNear(grid, start, snapRadius, out startX, out startY))
                    return null;
                if (!grid.IsWalkable(goalX, goalY) &&
                    !TryFindWalkableNear(grid, goal, snapRadius, out goalX, out goalY))
                    return null;
            }
            else if (!grid.IsWalkable(startX, startY) || !grid.IsWalkable(goalX, goalY))
                return null;

            int width = grid.Width;
            int count = width * grid.Height;
            scratch ??= new Scratch();
            if (scratch.Capacity < count)
            {
                scratch.G = new float[count];
                scratch.F = new float[count];
                scratch.CameFrom = new int[count];
                scratch.State = new byte[count];
                scratch.Capacity = count;
            }

            float[] gScore = scratch.G;
            float[] fScore = scratch.F;
            int[] cameFrom = scratch.CameFrom;
            byte[] state = scratch.State; // 0 unseen, 1 open, 2 closed
            System.Array.Clear(state, 0, count);
            for (int index = 0; index < count; index++)
            {
                gScore[index] = float.PositiveInfinity;
                fScore[index] = float.PositiveInfinity;
                cameFrom[index] = -1;
            }

            int startIndex = startY * width + startX;
            int goalIndex = goalY * width + goalX;
            MinHeap open = scratch.Open;
            open.Reset();
            gScore[startIndex] = 0f;
            fScore[startIndex] = Heuristic(startX, startY, goalX, goalY);
            open.Push(startIndex, fScore[startIndex]);
            state[startIndex] = 1;

            while (open.Count > 0)
            {
                int current = open.Pop(out float priority);
                if (priority > fScore[current])
                    continue; // stale entry, a better path to this cell was found meanwhile
                if (current == goalIndex)
                    return BuildPath(grid, cameFrom, current, start, goal, simplify);

                state[current] = 2;
                int currentX = current % width;
                int currentY = current / width;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;

                        int neighbourX = currentX + dx;
                        int neighbourY = currentY + dy;
                        if (!grid.IsWalkable(neighbourX, neighbourY))
                            continue;

                        // No corner cutting: a diagonal step needs both orthogonal cells free,
                        // otherwise the path grazes the corner of a wall.
                        if (dx != 0 && dy != 0 &&
                            (!grid.IsWalkable(currentX + dx, currentY) || !grid.IsWalkable(currentX, currentY + dy)))
                            continue;

                        int neighbour = neighbourY * width + neighbourX;
                        if (state[neighbour] == 2)
                            continue;

                        float step = dx != 0 && dy != 0 ? DiagonalCost : StraightCost;
                        float tentative = gScore[current] + step;
                        if (tentative >= gScore[neighbour])
                            continue;

                        cameFrom[neighbour] = current;
                        gScore[neighbour] = tentative;
                        fScore[neighbour] = tentative + Heuristic(neighbourX, neighbourY, goalX, goalY);
                        open.Push(neighbour, fScore[neighbour]);
                        state[neighbour] = 1;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Plans a whole ordered route: every consecutive pair of author points is joined by its own
        /// shortest walkable path, and the legs are stitched into a single polyline.
        ///
        /// <paramref name="fallbackSegments"/> counts the legs that could not be planned — a point standing
        /// in a wall, or two areas the walkable grid does not connect — so the caller can warn the author
        /// instead of silently drawing a line through a wall. A leg that fails is drawn straight.
        ///
        /// When the grid is unusable the waypoints are returned unchanged, so a map without readable
        /// pixels still shows the authored route.
        /// </summary>
        public static List<Vector2> PlanRoute(
            OccupancyGrid grid,
            IReadOnlyList<Vector2> waypoints,
            out int fallbackSegments,
            bool simplify = true)
        {
            fallbackSegments = 0;
            var result = new List<Vector2>();
            if (waypoints == null || waypoints.Count == 0)
                return result;

            result.Add(waypoints[0]);
            if (waypoints.Count == 1)
                return result;

            if (grid == null || !grid.IsValid)
            {
                for (int index = 1; index < waypoints.Count; index++)
                    AppendIfDifferent(result, waypoints[index]);
                return result;
            }

            for (int index = 1; index < waypoints.Count; index++)
            {
                List<Vector2> leg = Plan(grid, waypoints[index - 1], waypoints[index], simplify);
                if (leg == null || leg.Count < 2)
                {
                    fallbackSegments++;
                    AppendIfDifferent(result, waypoints[index]);
                    continue;
                }

                for (int point = 1; point < leg.Count; point++)
                    AppendIfDifferent(result, leg[point]);
            }

            return result;
        }

        private static void AppendIfDifferent(List<Vector2> points, Vector2 point)
        {
            if (points.Count > 0 && (points[^1] - point).sqrMagnitude < 0.000001f)
                return;

            points.Add(point);
        }

        /// <summary>
        /// Nearest walkable cell around a point, within <paramref name="radius"/> metres. It lets a waypoint
        /// standing against a wall, or inside the clearance the agent radius reserves, still be routed instead
        /// of drawing a straight line through the obstacle.
        /// </summary>
        private static bool TryFindWalkableNear(
            OccupancyGrid grid,
            Vector2 point,
            float radius,
            out int x,
            out int y)
        {
            x = 0;
            y = 0;
            if (radius <= 0f || !grid.TryWorldToCell(point, out int originX, out int originY))
                return false;

            int rangeX = Mathf.Max(1, Mathf.CeilToInt(radius / Mathf.Max(0.0001f, grid.CellSizeX)));
            int rangeY = Mathf.Max(1, Mathf.CeilToInt(radius / Mathf.Max(0.0001f, grid.CellSizeZ)));
            float best = float.PositiveInfinity;
            bool found = false;

            for (int offsetY = -rangeY; offsetY <= rangeY; offsetY++)
            {
                for (int offsetX = -rangeX; offsetX <= rangeX; offsetX++)
                {
                    int cellX = originX + offsetX;
                    int cellY = originY + offsetY;
                    if (!grid.IsWalkable(cellX, cellY))
                        continue;

                    float distance = Vector2.Distance(point, grid.CellCenter(cellX, cellY));
                    if (distance > radius || distance >= best)
                        continue;

                    best = distance;
                    x = cellX;
                    y = cellY;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Turns the reconstructed cells into a world-space polyline: exact user points at both ends,
        /// cell centres in between, and as few of them as the line of sight allows.
        /// </summary>
        private static List<Vector2> BuildPath(
            OccupancyGrid grid,
            int[] cameFrom,
            int goalIndex,
            Vector2 start,
            Vector2 goal,
            bool simplify)
        {
            var cells = new List<int>();
            for (int node = goalIndex; node >= 0 && cells.Count <= grid.CellCount; node = cameFrom[node])
            {
                cells.Add(node);
                if (cameFrom[node] < 0)
                    break;
            }
            cells.Reverse();

            var points = new List<Vector2>(cells.Count + 2) { start };
            for (int index = 1; index < cells.Count - 1; index++)
            {
                int node = cells[index];
                points.Add(grid.CellCenter(node % grid.Width, node / grid.Width));
            }
            points.Add(goal);

            if (!simplify)
                return points;

            return Simplify(grid, points);
        }

        /// <summary>
        /// Drops every waypoint that is not needed: collinear steps first, then any point the previous
        /// anchor can reach in a straight walkable line.
        /// </summary>
        private static List<Vector2> Simplify(OccupancyGrid grid, IReadOnlyList<Vector2> points)
        {
            var straight = new List<Vector2> { points[0] };
            for (int index = 1; index < points.Count - 1; index++)
            {
                Vector2 previous = straight[^1];
                Vector2 current = points[index];
                Vector2 next = points[index + 1];
                Vector2 first = (current - previous).normalized;
                Vector2 second = (next - current).normalized;
                if (Vector2.Dot(first, second) < 0.999f)
                    straight.Add(current);
            }
            straight.Add(points[^1]);

            var result = new List<Vector2> { straight[0] };
            int anchor = 0;
            for (int index = 2; index < straight.Count; index++)
            {
                if (HasLineOfSight(grid, straight[anchor], straight[index]))
                    continue;

                result.Add(straight[index - 1]);
                anchor = index - 1;
            }
            result.Add(straight[^1]);
            return result;
        }

        /// <summary>
        /// True when a straight segment stays on walkable cells the whole way.
        ///
        /// Cells are visited one by one, including both neighbours when the segment runs exactly through the
        /// corner shared by them. A plain line rasterisation skips that corner cell, which is how a trajectory
        /// could still shave a diagonal obstacle even though both its ends were walkable.
        /// </summary>
        public static bool HasLineOfSight(OccupancyGrid grid, Vector2 from, Vector2 to)
        {
            if (grid == null || !grid.TryWorldToCellF(from, out Vector2 startCell))
                return false;
            if (!grid.TryWorldToCellF(to, out Vector2 goalCell))
                return false;

            int x = Mathf.FloorToInt(startCell.x);
            int y = Mathf.FloorToInt(startCell.y);
            int goalX = Mathf.Clamp(Mathf.FloorToInt(goalCell.x), 0, grid.Width - 1);
            int goalY = Mathf.Clamp(Mathf.FloorToInt(goalCell.y), 0, grid.Height - 1);
            if (!grid.IsWalkable(x, y) || !grid.IsWalkable(goalX, goalY))
                return false;

            float deltaX = goalCell.x - startCell.x;
            float deltaY = goalCell.y - startCell.y;
            int stepX = deltaX >= 0f ? 1 : -1;
            int stepY = deltaY >= 0f ? 1 : -1;
            float stepDeltaX = Mathf.Approximately(deltaX, 0f) ? float.PositiveInfinity : Mathf.Abs(1f / deltaX);
            float stepDeltaY = Mathf.Approximately(deltaY, 0f) ? float.PositiveInfinity : Mathf.Abs(1f / deltaY);
            float nextX = NextBoundary(startCell.x, deltaX);
            float nextY = NextBoundary(startCell.y, deltaY);

            int guard = grid.CellCount + 4;
            while (guard-- > 0)
            {
                if (x == goalX && y == goalY)
                    return true;

                if (!float.IsInfinity(nextX) && Mathf.Abs(nextX - nextY) < 1e-6f)
                {
                    // The segment crosses the corner itself: the two orthogonal neighbours are touched as well
                    // as the diagonal one, and an agent cannot squeeze through that gap. Same rule as the search,
                    // which refuses to cut a corner.
                    if (!grid.IsWalkable(x + stepX, y + stepY) ||
                        !grid.IsWalkable(x + stepX, y) ||
                        !grid.IsWalkable(x, y + stepY))
                        return false;

                    x += stepX;
                    y += stepY;
                    nextX += stepDeltaX;
                    nextY += stepDeltaY;
                }
                else if (nextX < nextY)
                {
                    x += stepX;
                    nextX += stepDeltaX;
                }
                else
                {
                    y += stepY;
                    nextY += stepDeltaY;
                }

                if (!grid.IsWalkable(x, y))
                    return false;
            }

            return false;
        }

        /// <summary>
        /// Parameter at which the segment leaves the cell it currently stands in, along one axis. A point
        /// sitting exactly on a boundary belongs to the next cell, so the following boundary is used.
        /// </summary>
        private static float NextBoundary(float coordinate, float delta)
        {
            if (Mathf.Approximately(delta, 0f))
                return float.PositiveInfinity;

            float boundary = delta > 0f ? Mathf.Floor(coordinate) + 1f : Mathf.Floor(coordinate);
            if (Mathf.Abs(boundary - coordinate) < 1e-6f)
                boundary += delta > 0f ? 1f : -1f;

            return (boundary - coordinate) / delta;
        }

        private static float Heuristic(int x, int y, int goalX, int goalY)
        {
            int dx = Mathf.Abs(goalX - x);
            int dy = Mathf.Abs(goalY - y);
            // Octile distance: exact cost of the free diagonal moves plus the straight remainder.
            return StraightCost * (dx + dy) + (DiagonalCost - 2f * StraightCost) * Mathf.Min(dx, dy);
        }

        /// <summary>Binary min-heap of cell indices keyed by their f-score.</summary>
        internal sealed class MinHeap
        {
            private readonly List<int> _nodes = new();
            private readonly List<float> _priorities = new();

            public int Count => _nodes.Count;

            /// <summary>Empties the heap for the next search without releasing its buffers.</summary>
            public void Reset()
            {
                _nodes.Clear();
                _priorities.Clear();
            }

            public void Push(int node, float priority)
            {
                _nodes.Add(node);
                _priorities.Add(priority);
                int index = _nodes.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (_priorities[parent] <= _priorities[index])
                        break;
                    Swap(parent, index);
                    index = parent;
                }
            }

            public int Pop(out float priority)
            {
                int node = _nodes[0];
                priority = _priorities[0];
                int last = _nodes.Count - 1;
                _nodes[0] = _nodes[last];
                _priorities[0] = _priorities[last];
                _nodes.RemoveAt(last);
                _priorities.RemoveAt(last);

                int index = 0;
                while (true)
                {
                    int left = index * 2 + 1;
                    int right = left + 1;
                    int smallest = index;
                    if (left < _nodes.Count && _priorities[left] < _priorities[smallest])
                        smallest = left;
                    if (right < _nodes.Count && _priorities[right] < _priorities[smallest])
                        smallest = right;
                    if (smallest == index)
                        break;
                    Swap(index, smallest);
                    index = smallest;
                }

                return node;
            }

            private void Swap(int first, int second)
            {
                (_nodes[first], _nodes[second]) = (_nodes[second], _nodes[first]);
                (_priorities[first], _priorities[second]) = (_priorities[second], _priorities[first]);
            }
        }
    }
}
