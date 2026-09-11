using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RobotSNAP.Core.Scenario
{
    /// <summary>Normalizes a user-selected scenario cover to a Resources-compatible PNG.</summary>
    public static class ScenarioCoverImporter
    {
        private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg" };

        public static bool TryImport(
            string sourcePath,
            string destinationDirectory,
            string requestedName,
            out string fileName,
            out string error)
        {
            fileName = null;
            error = null;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                error = "The cover image could not be found.";
                return false;
            }
            if (!SupportedExtensions.Contains(Path.GetExtension(sourcePath).ToLowerInvariant()))
            {
                error = "Unsupported cover format. Use PNG, JPG or JPEG.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                error = "The cover destination folder is not configured.";
                return false;
            }

            string baseName = ToFileId(requestedName);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "scenario";
            fileName = baseName + ".png";
            string destinationPath = Path.Combine(destinationDirectory, fileName);
            string temporaryPath = destinationPath + ".tmp";
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(sourcePath)))
                {
                    error = "The cover image could not be decoded.";
                    return false;
                }
                byte[] png = texture.EncodeToPNG();
                if (png == null || png.Length == 0)
                {
                    error = "The cover image could not be converted to PNG.";
                    return false;
                }
                Directory.CreateDirectory(destinationDirectory);
                File.WriteAllBytes(temporaryPath, png);
                if (File.Exists(destinationPath)) File.Delete(destinationPath);
                File.Move(temporaryPath, destinationPath);
                return true;
            }
            catch (Exception exception)
            {
                TryDelete(temporaryPath);
                error = $"Cover import failed: {exception.Message}";
                return false;
            }
            finally
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
                else UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static string ToFileId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "scenario";
            return new string(value.Trim().ToLowerInvariant()
                .Select(character => char.IsLetterOrDigit(character) ? character : '_')
                .ToArray()).Trim('_');
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Preserve the original import error.
            }
        }
    }
}
