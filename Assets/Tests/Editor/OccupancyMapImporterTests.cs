using System;
using System.IO;
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
                Assert.That(error, Does.Contain("PNG, JPG ou JPEG"));
            }
            finally
            {
                Directory.Delete(testDirectory, true);
            }
        }
    }
}
