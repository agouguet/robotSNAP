using System;
using System.IO;
using NUnit.Framework;
using RobotSNAP.Core.Scenario;

namespace RobotSNAP.Tests.Editor
{
    public sealed class ScenarioPathResolverTests
    {
        [Test]
        public void ResolvePaths_UsesDefaultsForEmptyFolders()
        {
            var paths = ScenarioPathResolver.ResolvePaths("/streaming", "", null);

            Assert.That(paths.ScenariosPath, Is.EqualTo(Path.Combine("/streaming", "Scenarios")));
            Assert.That(paths.MapsPath, Is.EqualTo(Path.Combine("/streaming", "Dataset")));
        }

        [Test]
        public void FindScenarioFile_UsesExtensionsInProvidedOrder()
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string yamlPath = Path.Combine(directory, "example.yaml");
                string ymlPath = Path.Combine(directory, "example.yml");
                File.WriteAllText(yamlPath, "yaml");
                File.WriteAllText(ymlPath, "yml");

                string path = ScenarioPathResolver.FindScenarioFile(directory, "example", new[] { ".yml", ".yaml" });

                Assert.That(path, Is.EqualTo(ymlPath));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
