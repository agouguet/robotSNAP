using System;
using System.Collections.Generic;
using System.IO;

namespace RobotSNAP.Core.Scenario
{
    /// <summary>
    /// Resolves scenario and dataset paths without depending on Unity scene state.
    /// </summary>
    public static class ScenarioPathResolver
    {
        public static (string ScenariosPath, string MapsPath) ResolvePaths(
            string streamingAssetsPath,
            string scenariosFolder,
            string datasetFolder)
        {
            if (string.IsNullOrWhiteSpace(streamingAssetsPath))
                throw new ArgumentException("A StreamingAssets path is required.", nameof(streamingAssetsPath));

            return (
                Path.Combine(streamingAssetsPath, string.IsNullOrWhiteSpace(scenariosFolder) ? "Scenarios" : scenariosFolder),
                Path.Combine(streamingAssetsPath, string.IsNullOrWhiteSpace(datasetFolder) ? "Dataset" : datasetFolder));
        }

        public static string FindScenarioFile(string scenariosPath, string scenarioName, IEnumerable<string> validExtensions)
        {
            if (string.IsNullOrWhiteSpace(scenariosPath) || string.IsNullOrWhiteSpace(scenarioName) || validExtensions == null)
                return null;

            foreach (string extension in validExtensions)
            {
                string path = Path.Combine(scenariosPath, scenarioName + extension);
                if (File.Exists(path)) return path;
            }

            return null;
        }
    }
}
