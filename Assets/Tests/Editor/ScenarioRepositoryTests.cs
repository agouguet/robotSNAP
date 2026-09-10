using System.IO;
using NUnit.Framework;
using RobotSNAP.Core.Scenario;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    public sealed class ScenarioRepositoryTests
    {
        [Test]
        public void Load_ParsesBundledScenarioAndCachesItsMetadata()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Scenarios", "corner.yaml");
            var repository = new ScenarioRepository();

            ScenarioData scenario = repository.Load("corner", path, cache: true);
            ScenarioInfo info = repository.GetInfo("corner", path);

            Assert.That(scenario, Is.Not.Null);
            Assert.That(scenario.Info.Name, Is.EqualTo("Corner"));
            Assert.That(repository.CacheSize, Is.EqualTo(1));
            Assert.That(info, Is.SameAs(scenario.Info));
        }

        [Test]
        public void Clear_RemovesScenarioCache()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Scenarios", "corner.yaml");
            var repository = new ScenarioRepository();
            repository.Load("corner", path, cache: true);

            repository.Clear();

            Assert.That(repository.CacheSize, Is.Zero);
            Assert.That(repository.TryGetScenario("corner", out _), Is.False);
        }
    }
}
