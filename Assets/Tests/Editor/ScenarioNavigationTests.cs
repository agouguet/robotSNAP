using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using RobotSNAP.Core.Scenario;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// The simulation has to walk the very trajectories the scenario editor drew. These tests pin the two
    /// halves of that promise: the runtime grid is sampled exactly like the editor's grid, and the planner
    /// returns the same polyline. They also cover the scale guards — the straight-leg shortcut and the
    /// per-frame search budget — that keep a large crowd from replanning everything in one frame.
    /// </summary>
    public sealed class ScenarioNavigationTests
    {
        private const int TextureSize = 64;
        private static readonly Bounds MapBounds = new Bounds(Vector3.zero, new Vector3(8f, 0f, 8f));

        [TearDown]
        public void TearDown()
        {
            ScenarioNavigation.Clear();
            ScenarioNavigation.Budget.SearchesPerFrame = ScenarioNavigation.DefaultSearchesPerFrame;
        }

        /// <summary>White map with a black band that stops before the far edge, so the band can be walked around.</summary>
        private static Texture2D BuildBandMap()
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            var pixels = new Color32[TextureSize * TextureSize];
            for (int row = 0; row < TextureSize; row++)
            {
                for (int column = 0; column < TextureSize; column++)
                {
                    bool wall = column >= TextureSize / 2 - 2 && column < TextureSize / 2 + 2 &&
                                row < TextureSize * 3 / 4;
                    pixels[row * TextureSize + column] = wall ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D BuildOpenMap()
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            var pixels = new Color32[TextureSize * TextureSize];
            for (int index = 0; index < pixels.Length; index++)
                pixels[index] = new Color32(255, 255, 255, 255);

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static OccupancyGrid BuildEditorGrid(Texture2D texture) =>
            OccupancyGrid.FromTexture(texture, MapBounds, ScenarioNavigation.Resolution)
                .WithObstaclesInflatedBy(ScenarioNavigation.DefaultAgentRadius);

        private static List<Vector2> WalkableCenters(OccupancyGrid grid)
        {
            var centers = new List<Vector2>();
            for (int y = 0; y < grid.Height; y += 2)
            {
                for (int x = 0; x < grid.Width; x += 2)
                {
                    if (grid.IsWalkable(x, y))
                        centers.Add(grid.CellCenter(x, y));
                }
            }

            return centers;
        }

        /// <summary>Two walkable points the wall separates: the interesting case for a planner.</summary>
        private static bool TryFindSeparatedPair(OccupancyGrid grid, out Vector2 from, out Vector2 to)
        {
            List<Vector2> centers = WalkableCenters(grid);
            for (int first = 0; first < centers.Count; first += 3)
            {
                for (int second = centers.Count - 1; second > first; second -= 3)
                {
                    if (OccupancyPathPlanner.HasLineOfSight(grid, centers[first], centers[second]))
                        continue;

                    from = centers[first];
                    to = centers[second];
                    return true;
                }
            }

            from = Vector2.zero;
            to = Vector2.zero;
            return false;
        }

        [Test]
        public void BuildFrom_SamplesTheImageLikeTheEditor()
        {
            Texture2D texture = BuildBandMap();
            try
            {
                ScenarioNavigation.BuildFrom(texture, MapBounds);
                Assert.That(ScenarioNavigation.IsAvailable, Is.True);

                OccupancyGrid editor = BuildEditorGrid(texture);
                OccupancyGrid runtime = ScenarioNavigation.Walkable;

                Assert.That(runtime.HasSameSampling(editor), Is.True,
                    "Editor and runtime must sample the occupancy image on the same cells.");
                Assert.That(runtime.Width, Is.EqualTo(editor.Width));
                Assert.That(runtime.Height, Is.EqualTo(editor.Height));

                for (int y = 0; y < editor.Height; y += 3)
                {
                    for (int x = 0; x < editor.Width; x += 3)
                    {
                        Assert.That(
                            runtime.IsWalkable(x, y),
                            Is.EqualTo(editor.IsWalkable(x, y)),
                            $"Cell ({x}, {y}) is judged differently by the editor and the runtime.");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void Plan_ReturnsTheExactEditorPolyline()
        {
            Texture2D texture = BuildBandMap();
            try
            {
                ScenarioNavigation.BuildFrom(texture, MapBounds);
                OccupancyGrid editor = BuildEditorGrid(texture);
                Assert.That(TryFindSeparatedPair(editor, out Vector2 from, out Vector2 to), Is.True,
                    "The map must hold two walkable points the wall separates.");

                List<Vector2> runtimePath = ScenarioNavigation.Plan(from, to);
                List<Vector2> editorPath = OccupancyPathPlanner.Plan(editor, from, to);

                Assert.That(runtimePath, Is.Not.Null);
                Assert.That(runtimePath, Is.EqualTo(editorPath),
                    "The runtime must return the very polyline the editor drew.");
                Assert.That(runtimePath.Count, Is.GreaterThan(2), "The wall forces the path to bend.");
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void PlanRoute_WalksEveryLegOfAGroupRoute()
        {
            Texture2D texture = BuildBandMap();
            try
            {
                ScenarioNavigation.BuildFrom(texture, MapBounds);
                OccupancyGrid editor = BuildEditorGrid(texture);
                Assert.That(TryFindSeparatedPair(editor, out Vector2 from, out Vector2 to), Is.True);
                List<Vector2> waypoints = new List<Vector2> { from, to, from };

                List<Vector2> route = ScenarioNavigation.PlanRoute(waypoints, out int fallbackSegments);

                Assert.That(fallbackSegments, Is.EqualTo(0), "Every leg is reachable.");
                Assert.That(route, Is.Not.Empty);
                Assert.That(route[0], Is.EqualTo(from));
                Assert.That(route[^1], Is.EqualTo(from));
                foreach (Vector2 point in route)
                    Assert.That(ScenarioNavigation.IsWalkable(point), Is.True, $"{point} is not walkable.");
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void TryPlan_PrefersAStraightLineWithoutSearching()
        {
            Texture2D texture = BuildOpenMap();
            try
            {
                ScenarioNavigation.BuildFrom(texture, MapBounds);
                int searchesBefore = ScenarioNavigation.Searches;

                Assert.That(ScenarioNavigation.TryPlan(new Vector2(-3f, 0f), new Vector2(3f, 0f), out List<Vector2> path), Is.True);

                Assert.That(path, Is.Not.Null);
                Assert.That(path.Count, Is.EqualTo(2), "An open corridor is a straight walk.");
                Assert.That(ScenarioNavigation.Searches, Is.EqualTo(searchesBefore), "No A* search is needed.");
                Assert.That(ScenarioNavigation.StraightSegments, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void TryPlan_ProjectsBothEndsOntoWalkableGround()
        {
            Texture2D texture = BuildBandMap();
            try
            {
                ScenarioNavigation.BuildFrom(texture, MapBounds);
                OccupancyGrid grid = ScenarioNavigation.Walkable;
                Assert.That(TryFindSeparatedPair(grid, out Vector2 from, out Vector2 to), Is.True);

                // Aim at a cell of the wall itself: the planner has to pull the endpoint back onto the map.
                Vector2 inTheWall = Vector2.zero;
                bool foundWall = false;
                for (int y = 0; y < grid.Height && !foundWall; y++)
                {
                    for (int x = 0; x < grid.Width; x++)
                    {
                        if (grid.IsWalkable(x, y))
                            continue;

                        inTheWall = grid.CellCenter(x, y);
                        foundWall = true;
                        break;
                    }
                }

                Assert.That(foundWall, Is.True);
                Assert.That(ScenarioNavigation.TryProjectToWalkable(inTheWall, ScenarioNavigation.SnapRadius, out Vector2 projected), Is.True);
                Assert.That(ScenarioNavigation.IsWalkable(projected), Is.True);
                Assert.That(Vector2.Distance(projected, inTheWall), Is.LessThanOrEqualTo(ScenarioNavigation.SnapRadius));

                Assert.That(ScenarioNavigation.TryPlan(from, inTheWall, out List<Vector2> path), Is.True);
                Assert.That(ScenarioNavigation.IsWalkable(path[0]), Is.True);
                Assert.That(ScenarioNavigation.IsWalkable(path[^1]), Is.True,
                    "A path must never end inside a wall, or the agent walks into it.");
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void TryPlan_PostponesTheSurplusWhenTheFrameBudgetIsSpent()
        {
            Texture2D texture = BuildBandMap();
            try
            {
                ScenarioNavigation.BuildFrom(texture, MapBounds);
                ScenarioNavigation.Budget.SearchesPerFrame = 2;
                OccupancyGrid grid = ScenarioNavigation.Walkable;

                // Only the legs the wall blocks reach the search stage; the straight ones cost nothing.
                List<Vector2> centers = WalkableCenters(grid);
                int requests = 0;
                for (int index = 0; index < centers.Count && requests < 6; index++)
                {
                    Vector2 from = centers[index];
                    Vector2 to = centers[centers.Count - 1 - index];
                    if (OccupancyPathPlanner.HasLineOfSight(grid, from, to))
                        continue;

                    requests++;
                    ScenarioNavigation.TryPlan(from, to, out _);
                }

                Assert.That(requests, Is.GreaterThan(2), "The map holds several legs the wall blocks.");
                Assert.That(ScenarioNavigation.Searches, Is.LessThanOrEqualTo(2),
                    "A single frame must not run more searches than its budget allows.");
                Assert.That(ScenarioNavigation.DeferredSearches, Is.GreaterThan(0),
                    "The surplus requests must be reported as postponed, not silently dropped.");
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void IsPathStillValid_FollowsThePolylineAndItsTolerance()
        {
            var path = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(10f, 0f, 0f)
            };

            Assert.That(ScenarioNavigation.IsPathStillValid(new Vector2(5f, 0.2f), path, 0.6f), Is.True);
            Assert.That(ScenarioNavigation.IsPathStillValid(new Vector2(5f, 2f), path, 0.6f), Is.False,
                "An agent pushed two metres aside needs its path replanned.");
        }

        [Test]
        public void IsPathStillValid_RejectsAnEmptyPath()
        {
            Assert.That(ScenarioNavigation.IsPathStillValid(Vector2.zero, null, 0.6f), Is.False);
            Assert.That(ScenarioNavigation.IsPathStillValid(Vector2.zero, new List<Vector3>(), 0.6f), Is.False);
        }

        /// <summary>
        /// Scale guard: a crowd of a hundred agents refreshing its path several times a second is the load the
        /// planner has to absorb. Short legs must cost nothing at all, and the searches that remain must stay
        /// affordable.
        /// </summary>
        [Test]
        public void CrowdScale_ShortLegsAreFreeAndSearchesStayAffordable()
        {
            Texture2D texture = BuildBandMap();
            try
            {
                ScenarioNavigation.BuildFrom(texture, MapBounds);
                OccupancyGrid grid = ScenarioNavigation.Walkable;
                List<Vector2> walkable = WalkableCenters(grid);
                Assert.That(walkable.Count, Is.GreaterThan(50));

                // 100 agents walking a one-metre leg in the open: the straight-line shortcut absorbs them all.
                var stopwatch = Stopwatch.StartNew();
                int straightLegs = 0;
                for (int index = 0; index < 100; index++)
                {
                    Vector2 start = walkable[index % walkable.Count];
                    Vector2 goal = start + new Vector2(1.0f, 0f);
                    if (ScenarioNavigation.TryPlan(start, goal, out _))
                        straightLegs++;
                }

                stopwatch.Stop();
                TestContext.WriteLine(
                    $"100 short legs: {straightLegs} resolved, {ScenarioNavigation.Searches} search(es), " +
                    $"{stopwatch.ElapsedMilliseconds} ms.");
                Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(2000), "Short legs must not be expensive.");

                // The legs that do need the planner: measure the search cost itself.
                ScenarioNavigation.Budget.SearchesPerFrame = int.MaxValue;
                int searched = 0;
                stopwatch.Restart();
                for (int index = 0; index < 200 && index < walkable.Count; index++)
                {
                    Vector2 from = walkable[index];
                    Vector2 to = walkable[walkable.Count - 1 - index % walkable.Count];
                    if (ScenarioNavigation.Plan(from, to) != null)
                        searched++;
                }

                stopwatch.Stop();
                TestContext.WriteLine(
                    $"200 full searches: {searched} planned in {stopwatch.ElapsedMilliseconds} ms " +
                    $"({stopwatch.ElapsedMilliseconds / 200.0:0.00} ms per search).");
                Assert.That(searched, Is.GreaterThan(0));
                Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(10000),
                    "200 grid searches have to stay in the hundreds of milliseconds, not seconds.");
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
