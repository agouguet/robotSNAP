using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RobotSNAP.Core
{
    /// <summary>
    /// Single filesystem boundary for user-owned simulation configurations.
    /// </summary>
    public static class ConfigPersistence
    {
        private const string ConfigDirectoryName = "Configs";

        public static string ConfigDirectoryPath => Path.Combine(Application.persistentDataPath, ConfigDirectoryName);

        public static void Save(SimulationConfig config, string fileName)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            Directory.CreateDirectory(ConfigDirectoryPath);
            config.SaveToJson(GetPath(fileName));
        }

        public static bool TryLoad(string fileName, out SimulationConfig config)
        {
            string path = GetPath(fileName);
            if (!File.Exists(path))
            {
                path = Path.Combine(Application.streamingAssetsPath, ConfigDirectoryName, Path.GetFileName(path));
                if (!File.Exists(path))
                {
                    config = null;
                    return false;
                }
            }

            config = SimulationConfig.LoadFromJson(path);
            return config != null;
        }

        public static IReadOnlyList<string> GetAvailableNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddNamesFromDirectory(ConfigDirectoryPath, names);
            AddNamesFromDirectory(Path.Combine(Application.streamingAssetsPath, ConfigDirectoryName), names);
            return new List<string>(names);
        }

        private static void AddNamesFromDirectory(string directory, ISet<string> names)
        {
            if (!Directory.Exists(directory)) return;
            foreach (string file in Directory.GetFiles(directory, "*.json"))
                names.Add(Path.GetFileNameWithoutExtension(file));
        }

        public static string GetPath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("A configuration file name is required.", nameof(fileName));

            string safeName = Path.GetFileName(fileName);
            if (!safeName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) safeName += ".json";
            return Path.Combine(ConfigDirectoryPath, safeName);
        }
    }
}
