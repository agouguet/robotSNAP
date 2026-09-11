using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using RobotSNAP.Core.Scenario;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    public sealed class ScenarioLoaderArchiveTests
    {
        [Test]
        public void ArchiveScenario_MovesYamlToRecoverableFolder()
        {
            string testDirectory = Path.Combine(Path.GetTempPath(), "robot_snap_archive_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);
            string scenarioPath = Path.Combine(testDirectory, "scenario_to_delete.yaml");
            File.WriteAllText(scenarioPath, "info:\n  name: Archive test\n");
            var gameObject = new GameObject("ScenarioLoaderArchiveTest");
            var loader = gameObject.AddComponent<ScenarioLoader>();

            try
            {
                typeof(ScenarioLoader)
                    .GetField("_scenariosPath", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(loader, testDirectory);

                bool success = loader.ArchiveScenario(
                    "scenario_to_delete",
                    out string archivedPath,
                    out string error);

                Assert.That(success, Is.True, error);
                Assert.That(File.Exists(scenarioPath), Is.False);
                Assert.That(File.Exists(archivedPath), Is.True);
                Assert.That(Path.GetDirectoryName(archivedPath), Is.EqualTo(Path.Combine(testDirectory, "DeletedScenarios")));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(testDirectory))
                    Directory.Delete(testDirectory, true);
            }
        }
    }
}
