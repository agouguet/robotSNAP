using NUnit.Framework;
using RobotSNAP.Core.Scenario;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// How far an agent sees along one ray, which is what the minimap draws its vision cones with. The query
    /// has to answer at the wall and not at the sensor's nominal range: a cone that reached through a wall
    /// would claim an agent sees what stands behind it, and the whole point of the map is to show the
    /// opposite.
    /// </summary>
    public sealed class OccupancyGridVisionTests
    {
        private const int Cells = 10;

        /// <summary>
        /// A ten-metre square grid sampled in one-metre cells, where every cell is walkable unless the
        /// predicate refuses the world position of its centre.
        /// </summary>
        private static OccupancyGrid BuildGrid(System.Func<Vector2, bool> walkableAt)
        {
            var bounds = new Bounds(Vector3.zero, new Vector3(Cells, 0f, Cells));
            var walkable = new bool[Cells * Cells];
            float cellSize = bounds.size.x / Cells;

            for (int y = 0; y < Cells; y++)
            {
                for (int x = 0; x < Cells; x++)
                {
                    // The image runs +x towards world -X and +y towards world +Z, so a cell is walked the other
                    // way round to name the world point it covers.
                    float worldX = bounds.max.x - (x + 0.5f) * cellSize;
                    float worldZ = bounds.min.z + (y + 0.5f) * cellSize;
                    walkable[y * Cells + x] = walkableAt(new Vector2(worldX, worldZ));
                }
            }

            return OccupancyGrid.FromMask(Cells, Cells, bounds, walkable, 1);
        }

        /// <summary>A grid whose east half, past x = 2, is wall.</summary>
        private static OccupancyGrid GridWithAWallAtTwo() => BuildGrid(point => point.x <= 2f);

        [Test]
        public void DistanceToWall_StopsAtTheWallInTheWay()
        {
            OccupancyGrid grid = GridWithAWallAtTwo();

            float distance = grid.DistanceToWall(new Vector2(-4f, 0f), Vector2.right, 20f);

            // The wall starts at x = 2, so six metres away; a cell of slack is the sampling of the grid.
            Assert.That(distance, Is.EqualTo(6f).Within(1f),
                "A ray has to stop at the wall, not carry on to the range it was given.");
        }

        [Test]
        public void DistanceToWall_AnswersTheFullRangeWhenNothingIsInTheWay()
        {
            OccupancyGrid grid = GridWithAWallAtTwo();

            // Away from the wall and across the map: nothing but open ground ahead of the ray.
            float distance = grid.DistanceToWall(new Vector2(-4f, 0f), Vector2.up, 3f);

            Assert.That(distance, Is.EqualTo(3f).Within(0.0001f),
                "Nothing between the ray and its range means the whole range is visible.");
        }

        [Test]
        public void DistanceToWall_StopsAtTheEdgeOfTheMap()
        {
            OccupancyGrid grid = GridWithAWallAtTwo();

            float distance = grid.DistanceToWall(new Vector2(-4f, 0f), Vector2.left, 20f);

            Assert.That(distance, Is.EqualTo(1f).Within(1f),
                "Past the edge of the map there is nothing to see, so the cone is cut there.");
        }

        [Test]
        public void DistanceToWall_IsZeroForAnAgentBuriedInAWall()
        {
            OccupancyGrid grid = GridWithAWallAtTwo();

            float distance = grid.DistanceToWall(new Vector2(4f, 0f), Vector2.right, 20f);

            Assert.That(distance, Is.EqualTo(0f).Within(0.0001f),
                "An agent inside a wall has nothing in front of it to look at.");
        }

        [Test]
        public void DistanceToWall_AnswersTheRangeForARayWithNoDirection_AndNothingForNoRange()
        {
            OccupancyGrid grid = GridWithAWallAtTwo();

            Assert.That(grid.DistanceToWall(Vector2.zero, Vector2.zero, 4f), Is.EqualTo(4f).Within(0.0001f),
                "A ray with no direction cannot be cut short by anything.");
            Assert.That(grid.DistanceToWall(Vector2.zero, Vector2.right, 0f), Is.EqualTo(0f).Within(0.0001f),
                "A range of zero is a range of zero.");
        }
    }
}
