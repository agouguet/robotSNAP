using NUnit.Framework;
using RobotSNAP.Core.Scenario;
using RobotSNAP.Environment;
using UnityEditor;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// A scenario map is built in one place: the environment builder turns the occupancy image into the floor,
    /// the walls and the walkable grid. These tests pin that contract, because a grid built from a second copy
    /// of the image — or not built at all — is exactly how the editor and the simulation drift apart.
    /// </summary>
    public sealed class EnvironmentBuilderNavigationTests
    {
        private const int Pixels = 32;
        private static readonly Bounds MapBounds = new Bounds(Vector3.zero, new Vector3(8f, 0f, 8f));

        private GameObject _host;
        private EnvironmentBuilder _builder;
        private Material _floorMaterial;
        private Material _wallMaterial;
        private Texture2D _texture;

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
                Object.DestroyImmediate(_host);
            if (_texture != null)
                Object.DestroyImmediate(_texture);
            if (_floorMaterial != null)
                Object.DestroyImmediate(_floorMaterial);
            if (_wallMaterial != null)
                Object.DestroyImmediate(_wallMaterial);

            ScenarioNavigation.Clear();
        }

        /// <summary>
        /// White map with a black band that stops before the far edge, so the grid is not trivially uniform and
        /// a path can still walk around the band.
        /// </summary>
        private Texture2D BuildMap()
        {
            var texture = new Texture2D(Pixels, Pixels, TextureFormat.RGBA32, false);
            var colors = new Color32[Pixels * Pixels];
            for (int row = 0; row < Pixels; row++)
            {
                for (int column = 0; column < Pixels; column++)
                {
                    bool wall = column >= Pixels / 2 - 1 && column < Pixels / 2 + 1 &&
                                row < Pixels * 3 / 4;
                    colors[row * Pixels + column] = wall
                        ? new Color32(0, 0, 0, 255)
                        : new Color32(255, 255, 255, 255);
                }
            }

            texture.SetPixels32(colors);
            texture.Apply();
            return texture;
        }

        private static Shader FindStandInShader()
        {
            return Shader.Find("Universal Render Pipeline/Lit")
                   ?? Shader.Find("Sprites/Default")
                   ?? Shader.Find("Standard");
        }

        /// <summary>An environment builder with the two materials it needs to raise a floor and walls.</summary>
        private void CreateBuilder()
        {
            Shader shader = FindStandInShader();
            if (shader == null)
                Assert.Ignore("No shader available in this project to build a stand-in environment.");

            _floorMaterial = new Material(shader);
            _wallMaterial = new Material(shader);

            _host = new GameObject("EnvironmentBuilder");
            _builder = _host.AddComponent<EnvironmentBuilder>();

            var serialized = new SerializedObject(_builder);
            serialized.FindProperty("floorMaterial").objectReferenceValue = _floorMaterial;
            serialized.FindProperty("wallMaterial").objectReferenceValue = _wallMaterial;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void BuildFromTexture_PublishesTheWalkableGridOfTheSameImage()
        {
            CreateBuilder();
            _texture = BuildMap();
            ScenarioNavigation.Clear();

            _builder.BuildFromTexture(_texture, MapBounds);

            Assert.That(ScenarioNavigation.IsAvailable, Is.True,
                "Building an image map has to publish the grid the agents plan on.");

            OccupancyGrid editorGrid = OccupancyGrid
                .FromTexture(_texture, MapBounds, ScenarioNavigation.Resolution)
                .WithObstaclesInflatedBy(ScenarioNavigation.DefaultAgentRadius);

            Assert.That(ScenarioNavigation.Walkable.HasSameSampling(editorGrid), Is.True,
                "The scene and the editor must sample the image on the same cells.");
            Assert.That(ScenarioNavigation.Walkable.Width, Is.EqualTo(editorGrid.Width));
            Assert.That(ScenarioNavigation.Walkable.Height, Is.EqualTo(editorGrid.Height));
        }

        [Test]
        public void BuildFromTexture_PlansOnTheGridItJustBuilt()
        {
            CreateBuilder();
            _texture = BuildMap();
            ScenarioNavigation.Clear();

            _builder.BuildFromTexture(_texture, MapBounds);

            OccupancyGrid grid = ScenarioNavigation.Walkable;
            Vector2 from = grid.CellCenter(1, 1);
            Vector2 to = grid.CellCenter(grid.Width - 2, grid.Height - 2);
            Assert.That(ScenarioNavigation.IsWalkable(from), Is.True);
            Assert.That(ScenarioNavigation.IsWalkable(to), Is.True);

            Assert.That(ScenarioNavigation.Plan(from, to), Is.Not.Null);
        }

        [Test]
        public void BuildEnvironment_WithoutAMap_LeavesNoGridBehind()
        {
            CreateBuilder();
            _texture = BuildMap();
            _builder.BuildFromTexture(_texture, MapBounds);
            Assert.That(ScenarioNavigation.IsAvailable, Is.True);

            // A prefab or an additive scene brings its own NavMesh: the previous grid must not survive it.
            System.Collections.IEnumerator routine = _builder.BuildEnvironment(string.Empty);
            while (routine.MoveNext())
            {
            }

            Assert.That(ScenarioNavigation.IsAvailable, Is.False,
                "An environment without an occupancy image must not keep the grid of the previous one.");
        }
    }
}
