using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RobotSNAP.Core.Scenario;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    public sealed class OccupancyMapImporterTests
    {
        [Test]
        public void TryImport_NormalizesImageAndWritesWorldBounds()
        {
            string testDirectory = Path.Combine(Path.GetTempPath(), "robot_snap_map_import_" + Guid.NewGuid().ToString("N"));
            string mapsDirectory = Path.Combine(testDirectory, "Dataset");
            string sourcePath = Path.Combine(testDirectory, "source.png");
            Directory.CreateDirectory(testDirectory);
            var sourceTexture = new Texture2D(4, 2, TextureFormat.RGBA32, false);

            try
            {
                sourceTexture.SetPixels(new[]
                {
                    Color.black, Color.white, Color.black, Color.white,
                    Color.white, Color.black, Color.white, Color.black
                });
                sourceTexture.Apply();
                File.WriteAllBytes(sourcePath, sourceTexture.EncodeToPNG());

                bool success = OccupancyMapImporter.TryImport(
                    sourcePath,
                    mapsDirectory,
                    "Test Map",
                    0.5f,
                    -1f,
                    2f,
                    out string identifier,
                    out string error);

                Assert.That(success, Is.True, error);
                Assert.That(identifier, Is.EqualTo("custom/test_map"));
                Assert.That(File.Exists(Path.Combine(mapsDirectory, "custom", "png", "test_map.png")), Is.True);

                string jsonPath = Path.Combine(mapsDirectory, "custom", "json", "test_map.json");
                Assert.That(File.Exists(jsonPath), Is.True);
                JObject metadata = JObject.Parse(File.ReadAllText(jsonPath));
                Assert.That((float)metadata["bbox"]["min"][0], Is.EqualTo(-1f));
                Assert.That((float)metadata["bbox"]["min"][1], Is.EqualTo(2f));
                Assert.That((float)metadata["bbox"]["max"][0], Is.EqualTo(1f));
                Assert.That((float)metadata["bbox"]["max"][1], Is.EqualTo(3f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceTexture);
                if (Directory.Exists(testDirectory))
                    Directory.Delete(testDirectory, true);
            }
        }

        [Test]
        public void TryImport_RejectsUnsupportedFileFormat()
        {
            string testDirectory = Path.Combine(Path.GetTempPath(), "robot_snap_map_import_" + Guid.NewGuid().ToString("N"));
            string sourcePath = Path.Combine(testDirectory, "map.gif");
            Directory.CreateDirectory(testDirectory);
            File.WriteAllText(sourcePath, "not-an-image");

            try
            {
                bool success = OccupancyMapImporter.TryImport(
                    sourcePath,
                    Path.Combine(testDirectory, "Dataset"),
                    "unsupported",
                    0.05f,
                    0f,
                    0f,
                    out _,
                    out string error);

                Assert.That(success, Is.False);
                Assert.That(error, Does.Contain("PNG, JPG or JPEG"));
            }
            finally
            {
                Directory.Delete(testDirectory, true);
            }
        }

        [Test]
        public void MapCoordinates_MatchHistoricalRuntimeOrientation()
        {
            var bounds = new Bounds(new Vector3(5f, 0f, -2f), new Vector3(20f, 0f, 10f));

            Vector2 topLeft = OccupancyMapCoordinates.ImageNormalizedToWorld(Vector2.zero, bounds);
            Vector2 bottomRight = OccupancyMapCoordinates.ImageNormalizedToWorld(Vector2.one, bounds);

            Assert.That(topLeft.x, Is.EqualTo(bounds.max.x).Within(0.0001f));
            Assert.That(topLeft.y, Is.EqualTo(bounds.min.z).Within(0.0001f));
            Assert.That(bottomRight.x, Is.EqualTo(bounds.min.x).Within(0.0001f));
            Assert.That(bottomRight.y, Is.EqualTo(bounds.max.z).Within(0.0001f));
        }

        [Test]
        public void MapCoordinates_RoundTripNonCenteredBounds()
        {
            var bounds = new Bounds(new Vector3(5f, 0f, -2f), new Vector3(20f, 0f, 10f));
            var imagePosition = new Vector2(0.25f, 0.75f);

            Vector2 worldPosition = OccupancyMapCoordinates.ImageNormalizedToWorld(imagePosition, bounds);
            Vector2 roundTrip = OccupancyMapCoordinates.WorldToImageNormalized(worldPosition, bounds);

            Assert.That(roundTrip.x, Is.EqualTo(imagePosition.x).Within(0.0001f));
            Assert.That(roundTrip.y, Is.EqualTo(imagePosition.y).Within(0.0001f));
        }

        [Test]
        public void CoverImporter_NormalizesCustomCoverToPng()
        {
            string testDirectory = Path.Combine(Path.GetTempPath(), "robot_snap_cover_import_" + Guid.NewGuid().ToString("N"));
            string sourcePath = Path.Combine(testDirectory, "source.png");
            string destination = Path.Combine(testDirectory, "covers");
            Directory.CreateDirectory(testDirectory);
            var sourceTexture = new Texture2D(3, 2, TextureFormat.RGBA32, false);

            try
            {
                sourceTexture.SetPixels(Enumerable.Repeat(Color.cyan, 6).ToArray());
                sourceTexture.Apply();
                File.WriteAllBytes(sourcePath, sourceTexture.EncodeToPNG());

                bool success = ScenarioCoverImporter.TryImport(
                    sourcePath, destination, "My Scenario", out string fileName, out string error);

                Assert.That(success, Is.True, error);
                Assert.That(fileName, Is.EqualTo("my_scenario.png"));
                Assert.That(File.Exists(Path.Combine(destination, fileName)), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceTexture);
                if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory, true);
            }
        }
    }
}
