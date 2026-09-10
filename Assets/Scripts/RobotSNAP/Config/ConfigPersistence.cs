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
                config = null;
                return false;
            }

            config = SimulationConfig.LoadFromJson(path);
            return config != null;
        }

        public static IReadOnlyList<string> GetAvailableNames()
        {
            if (!Directory.Exists(ConfigDirectoryPath)) return Array.Empty<string>();

            var names = new List<string>();
            foreach (string file in Directory.GetFiles(ConfigDirectoryPath, "*.json"))
                names.Add(Path.GetFileNameWithoutExtension(file));
            return names;
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
