using System.Collections.Generic;
using NUnit.Framework;
using RobotSNAP.Core.Scenario;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    public sealed class OccupancyPathPlannerTests
    {
        private const int Size = 21;

        /// <summary>Open room, or room with a full-height wall opened at two cells.</summary>
        private static OccupancyGrid BuildRoom(bool withWall, int gapFrom = 10, int gapTo = 11)
        {
            var walkable = new bool[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    bool wall = withWall && x == 10 && (y < gapFrom || y > gapTo);
                    walkable[y * Size + x] = !wall;
                }
            }

            var bounds = new Bounds(Vector3.zero, new Vector3(Size, 0f, Size));
            return OccupancyGrid.FromMask(Size, Size, bounds, walkable);
        }

        [Test]
        public void Plan_OnAnOpenRoom_IsTheStraightLine()
        {
            OccupancyGrid grid = BuildRoom(withWall: false);
            Vector2 start = grid.CellCenter(1, 10);
            Vector2 goal = grid.CellCenter(19, 10);

            List<Vector2> path = OccupancyPathPlanner.Plan(grid, start, goal);

            Assert.That(path, Is.Not.Null);
            Assert.That(path, Has.Count.EqualTo(2), "A straight walk needs no waypoint.");
            Assert.That(path[0], Is.EqualTo(start));
            Assert.That(path[^1], Is.EqualTo(goal));
        }

        [Test]
        public void Plan_AroundAWall_CrossesTheOpening()
        {
            OccupancyGrid grid = BuildRoom(withWall: true);
            // Both ends stand behind a solid part of the wall: only the opening at y = 10..11 connects them.
            Vector2 start = grid.CellCenter(1, 2);
            Vector2 goal = grid.CellCenter(19, 2);

            List<Vector2> path = OccupancyPathPlanner.Plan(grid, start, goal);

            Assert.That(path, Is.Not.Null, "The wall has an opening, a path must exist.");
            Assert.That(path.Count, Is.GreaterThan(2), "The path has to bend around the wall.");
            Assert.That(path[0], Is.EqualTo(start));
            Assert.That(path[^1], Is.EqualTo(goal));

            foreach (Vector2 point in path)
                Assert.That(grid.IsWorldWalkable(point), Is.True, $"Path point {point} is not walkable.");

            // The drawn polyline may cross the wall column between two waypoints, so the check samples the
            // segments themselves: what matters is that no segment ever crosses a wall, and that at least
            // one of them goes through column 10, where the opening is.
            bool crossedWallColumn = false;
            for (int index = 1; index < path.Count; index++)
            {
                Vector2 previous = path[index - 1];
                Vector2 current = path[index];
                Assert.That(
                    OccupancyPathPlanner.HasLineOfSight(grid, previous, current),
                    Is.True,
                    $"Segment {previous} -> {current} crosses a wall.");

                for (int step = 0; step <= 32; step++)
                {
                    Vector2 sample = Vector2.Lerp(previous, current, step / 32f);
                    if (grid.TryWorldToCell(sample, out int x, out _) && x == 10)
                        crossedWallColumn = true;
                }
            }

            Assert.That(crossedWallColumn, Is.True, "The path must pass through the wall opening.");
        }

        [Test]
        public void Plan_WithAClosedWall_ReturnsNull()
        {
            var walkable = new bool[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                    walkable[y * Size + x] = x != 10;
            }

            OccupancyGrid grid = OccupancyGrid.FromMask(
                Size, Size, new Bounds(Vector3.zero, new Vector3(Size, 0f, Size)), walkable);

            Assert.That(
                OccupancyPathPlanner.Plan(grid, grid.CellCenter(1, 10), grid.CellCenter(19, 10)),
                Is.Null);
        }

        [Test]
        public void Plan_FromAWall_ReturnsNull()
        {
            OccupancyGrid grid = BuildRoom(withWall: true);
            Vector2 insideWall = grid.CellCenter(10, 2);

            Assert.That(grid.IsWorldWalkable(insideWall), Is.False);
            Assert.That(OccupancyPathPlanner.Plan(grid, insideWall, grid.CellCenter(19, 10)), Is.Null);
        }

        [Test]
        public void Plan_HandlesMissingInputs()
        {
            Assert.That(OccupancyPathPlanner.Plan(null, Vector2.zero, Vector2.one), Is.Null);
            Assert.That(OccupancyPathPlanner.Plan(BuildRoom(false), new Vector2(500f, 500f), Vector2.zero), Is.Null);
        }

        [Test]
        public void HasLineOfSight_SeesTheOpeningButNotTheWall()
        {
            OccupancyGrid grid = BuildRoom(withWall: true);

            Assert.That(
                OccupancyPathPlanner.HasLineOfSight(grid, grid.CellCenter(8, 10), grid.CellCenter(12, 10)),
                Is.True, "The opening is at y = 10.");

            Assert.That(
                OccupancyPathPlanner.HasLineOfSight(grid, grid.CellCenter(8, 2), grid.CellCenter(12, 2)),
                Is.False, "The wall is solid at y = 2.");
        }

        [Test]
        public void FromMask_RejectsInconsistentInput()
        {
            var bounds = new Bounds(Vector3.zero, new Vector3(Size, 0f, Size));

            Assert.That(OccupancyGrid.FromMask(0, Size, bounds, new bool[0]), Is.Null);
            Assert.That(OccupancyGrid.FromMask(Size, Size, bounds, new bool[5]), Is.Null);
            Assert.That(OccupancyGrid.FromMask(Size, Size, bounds, null), Is.Null);
        }

        [Test]
        public void PlanRoute_JoinsEveryLegOfTheAuthoredWaypoints()
        {
            OccupancyGrid grid = BuildRoom(withWall: true);
            var waypoints = new List<Vector2>
            {
                grid.CellCenter(2, 10),
                grid.CellCenter(18, 10),
                grid.CellCenter(18, 18)
            };

            List<Vector2> route = OccupancyPathPlanner.PlanRoute(grid, waypoints, out int fallbackSegments);

            Assert.That(fallbackSegments, Is.EqualTo(0));
            Assert.That(route[0], Is.EqualTo(waypoints[0]));
            Assert.That(route[^1], Is.EqualTo(waypoints[2]));

            // Every drawn point stands on a walkable cell: the picture never crosses a wall.
            foreach (Vector2 point in route)
                Assert.That(grid.IsWorldWalkable(point), Is.True, $"{point} is not walkable.");

            // The second waypoint is on the route, which is what makes the drawn line bend around the wall.
            bool passesThroughMiddle = route.Exists(point =>
                Vector2.Distance(point, waypoints[1]) < 0.001f);
            Assert.That(passesThroughMiddle, Is.True);
        }

        [Test]
        public void PlanRoute_WithoutAWalkableGrid_ReturnsTheAuthoredWaypoints()
        {
            var waypoints = new List<Vector2> { new(1f, 2f), new(3f, 4f), new(5f, 6f) };

            List<Vector2> route = OccupancyPathPlanner.PlanRoute(null, waypoints, out int fallbackSegments);

            Assert.That(fallbackSegments, Is.EqualTo(0), "A missing grid is not a routing failure.");
            Assert.That(route, Is.EqualTo(waypoints));
        }

        [Test]
        public void PlanRoute_CountsTheLegsItCannotPlanAndStillDrawsThem()
        {
            OccupancyGrid walled = BuildRoom(withWall: true);
            Vector2 insideWall = walled.CellCenter(10, 2);
            var waypoints = new List<Vector2>
            {
                walled.CellCenter(2, 2),
                insideWall,
                walled.CellCenter(18, 2)
            };

            List<Vector2> route = OccupancyPathPlanner.PlanRoute(walled, waypoints, out int fallbackSegments);

            Assert.That(fallbackSegments, Is.EqualTo(2), "Both legs touch a point standing in the wall.");
            Assert.That(route, Is.EqualTo(waypoints), "Unreachable legs fall back to a straight line.");
        }

        [Test]
        public void PlanRoute_HandlesEmptyAndSinglePointRoutes()
        {
            OccupancyGrid grid = BuildRoom(withWall: false);

            Assert.That(OccupancyPathPlanner.PlanRoute(grid, null, out _), Is.Empty);
            Assert.That(OccupancyPathPlanner.PlanRoute(grid, new List<Vector2>(), out _), Is.Empty);
            Assert.That(
                OccupancyPathPlanner.PlanRoute(grid, new List<Vector2> { Vector2.one }, out int fallback),
                Is.EqualTo(new List<Vector2> { Vector2.one }));
            Assert.That(fallback, Is.EqualTo(0));
        }

        [Test]
        public void Plan_SnapsAWaypointStandingAgainstAWallOntoTheWalkableGrid()
        {
            // A 21 m room sampled at 20 cm: the single blocked cell is a post the author can stand against.
            const int Cells = 105;
            var walkable = new bool[Cells * Cells];
            for (int index = 0; index < walkable.Length; index++)
                walkable[index] = true;
            walkable[52 * Cells + 52] = false;

            OccupancyGrid grid = OccupancyGrid.FromMask(
                Cells,
                Cells,
                new Bounds(Vector3.zero, new Vector3(21f, 0f, 21f)),
                walkable);

            Vector2 onThePost = grid.CellCenter(52, 52);
            Vector2 goal = grid.CellCenter(80, 52);
            Assert.That(grid.IsWorldWalkable(onThePost), Is.False);

            List<Vector2> path = OccupancyPathPlanner.Plan(grid, onThePost, goal);

            Assert.That(path, Is.Not.Null, "A point a few centimetres inside an obstacle must still be routed.");
            Assert.That(path[0], Is.EqualTo(onThePost), "The drawn route keeps the authored point.");
            Assert.That(path[^1], Is.EqualTo(goal));
        }

        [Test]
        public void Plan_DoesNotRescueAWaypointFarInsideAWall()
        {
            OccupancyGrid grid = BuildRoom(withWall: true);
            Vector2 deepInTheWall = grid.CellCenter(10, 2);

            // The nearest walkable cell is a full metre away: this is an authoring mistake, not a nudge.
            Assert.That(grid.IsWorldWalkable(deepInTheWall), Is.False);
            Assert.That(
                OccupancyPathPlanner.Plan(grid, deepInTheWall, grid.CellCenter(2, 2)),
                Is.Null);
        }

        [Test]
        public void FromTexture_DoesNotSampleAwayAOnePixelWall()
        {
            // 40 px sampled at 8 cells means 5 px per cell: the dark line sits on row 21, which used to fall
            // between the sampled corners and the centre of its cell. Sampling five points let a path cross it.
            const int Pixels = 40;
            var texture = new Texture2D(Pixels, Pixels, TextureFormat.RGBA32, false);
            var colors = new Color32[Pixels * Pixels];
            for (int index = 0; index < colors.Length; index++)
                colors[index] = new Color32(255, 255, 255, 255);
            for (int x = 0; x < Pixels; x++)
                colors[21 * Pixels + x] = new Color32(0, 0, 0, 255);
            texture.SetPixels32(colors);
            texture.Apply();

            var bounds = new Bounds(Vector3.zero, new Vector3(10f, 0f, 10f));
            OccupancyGrid grid = OccupancyGrid.FromTexture(texture, bounds, 8, 0f, 0.5f);
            Object.DestroyImmediate(texture);

            Assert.That(grid, Is.Not.Null);
            Assert.That(grid.IsWorldWalkable(new Vector2(0f, 2f)), Is.True, "Above the wall.");
            Assert.That(grid.IsWorldWalkable(new Vector2(0f, -2f)), Is.True, "Below the wall.");
            Assert.That(
                OccupancyPathPlanner.Plan(grid, new Vector2(0f, 2f), new Vector2(0f, -2f)),
                Is.Null,
                "A one pixel wall must block the path instead of being sampled away.");
        }

        [Test]
        public void WithObstaclesInflatedBy_KeepsTheSamplingAndEatsClearance()
        {
            OccupancyGrid raw = BuildRoom(withWall: true);
            OccupancyGrid inflated = raw.WithObstaclesInflatedBy(1f);

            Assert.That(inflated.HasSameSampling(raw), Is.True);
            // Row 2 sits behind a solid part of the wall, so the clearance has to cover the neighbour cell.
            Assert.That(raw.IsWorldWalkable(raw.CellCenter(9, 2)), Is.True);
            Assert.That(
                inflated.IsWorldWalkable(raw.CellCenter(9, 2)),
                Is.False,
                "One metre of clearance must cover the cell next to the wall.");

            // The raw grid itself is untouched: it is the one that validates the authored points.
            Assert.That(raw.IsWalkable(9, 2), Is.True);
        }
    }
}
