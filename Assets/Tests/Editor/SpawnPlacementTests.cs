using NUnit.Framework;
using RobotSNAP.Agents;
using RobotSNAP.Core.Scenario;
using UnityEngine;
using UnityEngine.AI;

namespace RobotSNAP.Tests.Editor
{
    public sealed class SpawnPlacementTests
    {
        private const int TextureSize = 64;
        private const int WallFirstColumn = 28;
        private const int WallLastColumn = 35;
        private const float MapSize = 8f;

        /// <summary>Open floor, about two metres away from the wall band.</summary>
        private static readonly Vector2 OpenFloor = new Vector2(2f, 2f);

        /// <summary>Point standing inside the wall band, which crosses the map at x = 0.</summary>
        private static readonly Vector2 InsideWall = Vector2.zero;

        private Texture2D _map;

        [SetUp]
        public void SetUp()
        {
            ScenarioNavigation.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ScenarioNavigation.Clear();
            if (_map != null)
            {
                Object.DestroyImmediate(_map);
                _map = null;
            }
        }

        [Test]
        public void ProjectWithin_KeepsACandidateAlreadyOnWalkableGround()
        {
            LoadScenarioGrid();

            Assert.That(ScenarioNavigation.IsWalkable(OpenFloor), Is.True, "The test map must be walkable there.");

            Vector2 projected = SpawnPlacement.ProjectWithin(OpenFloor, OpenFloor, 1.5f);

            Assert.That(projected, Is.EqualTo(OpenFloor), "A slot already on walkable ground must not be moved.");
        }

        [Test]
        public void ProjectWithin_PullsACandidateOutOfTheWall()
        {
            LoadScenarioGrid();

            var anchor = new Vector2(1f, 0f);
            const float tolerance = 1.5f;
            Assert.That(ScenarioNavigation.IsWalkable(InsideWall), Is.False, "The candidate must start inside the wall.");
            Assert.That(ScenarioNavigation.IsWalkable(anchor), Is.True, "The anchor must sit on walkable ground.");

            Vector2 projected = SpawnPlacement.ProjectWithin(InsideWall, anchor, tolerance);

            Assert.That(
                ScenarioNavigation.IsWalkable(projected),
                Is.True,
                $"The projected slot {projected} is still inside the wall.");
            Assert.That(
                Vector2.Distance(projected, anchor),
                Is.LessThanOrEqualTo(tolerance),
                "The projection must stay within the tolerance asked by the caller.");
        }

        [Test]
        public void ProjectWithin_CollapsesOntoAWalkableAnchorWhenTheToleranceIsTooTight()
        {
            LoadScenarioGrid();

            // Nothing walkable fits 5 cm around a slot planted in the middle of the wall, so the existing
            // fall back applies: a walkable anchor wins over an authored slot that cannot be saved.
            var anchor = new Vector2(1f, 0f);

            Vector2 projected = SpawnPlacement.ProjectWithin(InsideWall, anchor, 0.05f);

            Assert.That(projected, Is.EqualTo(anchor));
            Assert.That(ScenarioNavigation.IsWalkable(projected), Is.True);
        }

        [Test]
        public void ScenarioNavigation_IsWalkableFollowsThePixelsOfTheMap()
        {
            LoadScenarioGrid();

            Assert.That(ScenarioNavigation.IsWalkable(InsideWall), Is.False, "A black pixel is a wall.");
            Assert.That(ScenarioNavigation.IsWalkable(OpenFloor), Is.True, "A white pixel is walkable.");
        }

        [Test]
        public void SnapToNavMesh_PullsTheAnchorOntoTheGridAndKeepsItsHeight()
        {
            LoadScenarioGrid();

            var anchorInWall = new Vector3(InsideWall.x, 7.5f, InsideWall.y);

            Vector3 snapped = SpawnPlacement.SnapToNavMesh(anchorInWall);

            Assert.That(snapped.y, Is.EqualTo(anchorInWall.y), "The height of the anchor must survive the projection.");
            Assert.That(
                ScenarioNavigation.IsWalkable(new Vector2(snapped.x, snapped.z)),
                Is.True,
                $"The snapped anchor {snapped} is still inside the wall.");

            var anchorOnFloor = new Vector3(OpenFloor.x, 3.25f, OpenFloor.y);
            Assert.That(SpawnPlacement.SnapToNavMesh(anchorOnFloor), Is.EqualTo(anchorOnFloor));
        }

        [Test]
        public void ProjectWithin_WithoutAGrid_KeepsTheHistoricalFallback()
        {
            AssumeNoNavMeshInTestScene();
            Assert.That(ScenarioNavigation.IsAvailable, Is.False, "The scenario grid is cleared for this case.");

            var anchor = new Vector2(1f, -2f);
            var slot = new Vector2(0.25f, -3.05f);

            // Without a grid and without a NavMesh the anchor is not walkable either, so the authored slot
            // is handed back untouched instead of being stacked on the anchor.
            Vector2 projected = SpawnPlacement.ProjectWithin(slot, anchor, 2f);

            Assert.That(projected, Is.EqualTo(slot));
            Assert.That(Vector2.Distance(projected, anchor), Is.LessThanOrEqualTo(2f), "The slot drifted out of range.");
        }

        [Test]
        public void ProjectWithin_KeepsTheAuthoredSlotWhenTheSceneHasNoNavMesh()
        {
            AssumeNoNavMeshInTestScene();

            var anchor = new Vector2(1f, -2f);
            var slot = new Vector2(0.25f, -3.05f);

            Assert.That(SpawnPlacement.ProjectWithin(slot, anchor, 2f), Is.EqualTo(slot));
        }

        [Test]
        public void ProjectWithin_DoesNotPullAnOutOfRangeSlotOntoTheAnchor()
        {
            AssumeNoNavMeshInTestScene();

            var slot = new Vector2(9f, 0f);

            Assert.That(SpawnPlacement.ProjectWithin(slot, Vector2.zero, 2f), Is.EqualTo(slot));
        }

        [Test]
        public void SnapToNavMesh_ReturnsTheAuthoredAnchorWhenTheSceneHasNoNavMesh()
        {
            AssumeNoNavMeshInTestScene();

            var anchor = new Vector3(3f, 0f, -4f);

            Assert.That(SpawnPlacement.SnapToNavMesh(anchor), Is.EqualTo(anchor));
        }

        /// <summary>
        /// Loads the grid the runtime would build: white background, one black wall band crossing the map,
        /// sampled over the same 8 x 8 metres as the simulation. No GameObject and no scene involved.
        /// </summary>
        private void LoadScenarioGrid()
        {
            _map = BuildTestMap();
            ScenarioNavigation.BuildFrom(_map, new Bounds(Vector3.zero, new Vector3(MapSize, 0f, MapSize)));

            Assert.That(ScenarioNavigation.IsAvailable, Is.True, "The scenario grid failed to load.");
        }

        private static Texture2D BuildTestMap()
        {
            Color32 clear = Color.white;
            Color32 wall = Color.black;
            var pixels = new Color32[TextureSize * TextureSize];

            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                    pixels[y * TextureSize + x] = x >= WallFirstColumn && x <= WallLastColumn ? wall : clear;
            }

            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// These behaviours are only defined without a NavMesh: with a baked surface the projection
        /// legitimately moves the point. The Editor test scene has no baked NavMesh, so guard anyway.
        /// </summary>
        private static void AssumeNoNavMeshInTestScene()
        {
            if (NavMesh.SamplePosition(Vector3.zero, out _, 100f, NavMesh.AllAreas))
                Assert.Ignore("The Editor test scene exposes a NavMesh; projection behaviour applies instead.");
        }
    }
}
