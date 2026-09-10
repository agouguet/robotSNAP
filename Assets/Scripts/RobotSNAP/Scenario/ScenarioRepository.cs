using System;
using System.Collections.Generic;
using System.IO;
using VYaml.Serialization;

namespace RobotSNAP.Core.Scenario
{
    /// <summary>
    /// Filesystem and YAML boundary for scenario documents and their metadata cache.
    /// </summary>
    public sealed class ScenarioRepository
    {
        private readonly Dictionary<string, ScenarioData> _scenarios = new();
        private readonly Dictionary<string, ScenarioInfo> _infos = new();

        public int CacheSize => _scenarios.Count;

        public bool TryGetScenario(string id, out ScenarioData scenario) => _scenarios.TryGetValue(id, out scenario);

        public ScenarioData Load(string id, string filePath, bool cache)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("A scenario file path is required.", nameof(filePath));

            ScenarioData scenario = Parse(File.ReadAllBytes(filePath), filePath);
            if (cache)
            {
                _scenarios[id] = scenario;
                if (scenario.Info != null) _infos[id] = scenario.Info;
            }
            return scenario;
        }

        public ScenarioData Parse(byte[] yamlBytes, string sourceName)
        {
            try
            {
                ScenarioData scenario = YamlSerializer.Deserialize<ScenarioData>(yamlBytes);
                if (scenario == null) throw new InvalidDataException("Deserialization returned null");
                if (!scenario.IsValid(out string validationError))
                    throw new InvalidDataException($"Validation failed: {validationError}");
                return scenario;
            }
            catch (Exception exception)
            {
                throw new InvalidDataException($"Failed to parse YAML from {sourceName}: {exception.Message}", exception);
            }
        }

        public ScenarioInfo GetInfo(string id, string filePath)
        {
            if (_infos.TryGetValue(id, out ScenarioInfo cached)) return cached;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;

            try
            {
                ScenarioData scenario = YamlSerializer.Deserialize<ScenarioData>(File.ReadAllBytes(filePath));
                if (scenario?.Info == null) return null;
                _infos[id] = scenario.Info;
                return scenario.Info;
            }
            catch
            {
                return null;
            }
        }

        public void Clear()
        {
            _scenarios.Clear();
            _infos.Clear();
        }
    }
}
