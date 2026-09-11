using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace RobotSNAP.Core.Scenario
{
    /// <summary>
    /// Imports an occupancy-grid image into the dataset layout consumed by ScenarioLoader.
    /// Source JPG files are normalized to PNG so every imported map follows the same runtime path.
    /// </summary>
    public static class OccupancyMapImporter
    {
        private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg" };

        public static bool TryImport(
            string sourcePath,
            string mapsRoot,
            string requestedName,
            float resolution,
            float originX,
            float originZ,
            out string mapIdentifier,
            out string error)
        {
            mapIdentifier = null;
            error = null;

            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                error = "The image file could not be found.";
                return false;
            }

            string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (!SupportedExtensions.Contains(extension))
            {
                error = "Unsupported format. Use PNG, JPG or JPEG.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(mapsRoot))
            {
                error = "The environment folder is not configured.";
                return false;
            }

            if (!float.IsFinite(resolution) || resolution <= 0f)
            {
                error = "Resolution must be greater than zero.";
                return false;
            }

            string baseName = ToFileId(string.IsNullOrWhiteSpace(requestedName)
                ? Path.GetFileNameWithoutExtension(sourcePath)
                : requestedName);
            if (string.IsNullOrWhiteSpace(baseName))
                baseName = "environment";

            string pngDirectory = Path.Combine(mapsRoot, "custom", "png");
            string jsonDirectory = Path.Combine(mapsRoot, "custom", "json");
            Directory.CreateDirectory(pngDirectory);
            Directory.CreateDirectory(jsonDirectory);

            string uniqueName = GetUniqueName(baseName, pngDirectory, jsonDirectory);
            string pngPath = Path.Combine(pngDirectory, uniqueName + ".png");
            string jsonPath = Path.Combine(jsonDirectory, uniqueName + ".json");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(sourcePath)))
                {
                    error = "The image could not be decoded.";
                    return false;
                }

                byte[] pngBytes = texture.EncodeToPNG();
                if (pngBytes == null || pngBytes.Length == 0)
                {
                    error = "Image conversion to PNG failed.";
                    return false;
                }

                float maxX = originX + texture.width * resolution;
                float maxZ = originZ + texture.height * resolution;
                var metadata = new
                {
                    id = $"custom/{uniqueName}",
                    resolution,
                    width = texture.width,
                    height = texture.height,
                    origin = new[] { originX, originZ },
                    bbox = new
                    {
                        min = new[] { originX, originZ },
                        max = new[] { maxX, maxZ }
                    }
                };

                string pngTemporaryPath = pngPath + ".tmp";
                string jsonTemporaryPath = jsonPath + ".tmp";
                File.WriteAllBytes(pngTemporaryPath, pngBytes);
                File.WriteAllText(jsonTemporaryPath, JsonConvert.SerializeObject(metadata, Formatting.Indented));
                File.Move(pngTemporaryPath, pngPath);
                File.Move(jsonTemporaryPath, jsonPath);

                mapIdentifier = $"custom/{uniqueName}";
                return true;
            }
            catch (Exception exception)
            {
                TryDelete(pngPath);
                TryDelete(jsonPath);
                TryDelete(pngPath + ".tmp");
                TryDelete(jsonPath + ".tmp");
                error = $"Import failed: {exception.Message}";
                return false;
            }
            finally
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(texture);
                else
                    UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static string GetUniqueName(string baseName, string pngDirectory, string jsonDirectory)
        {
            string candidate = baseName;
            int suffix = 2;
            while (File.Exists(Path.Combine(pngDirectory, candidate + ".png")) ||
                   File.Exists(Path.Combine(jsonDirectory, candidate + ".json")))
            {
                candidate = $"{baseName}_{suffix++}";
            }

            return candidate;
        }

        private static string ToFileId(string value)
        {
            char[] characters = value.Trim().ToLowerInvariant()
                .Select(character => char.IsLetterOrDigit(character) ? character : '_')
                .ToArray();
            return new string(characters).Trim('_');
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Preserve the original import error.
            }
        }
    }
}
