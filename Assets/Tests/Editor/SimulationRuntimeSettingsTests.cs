using NUnit.Framework;
using RobotSNAP.Core;
using RobotSNAP.Core.Scenario;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    public sealed class SimulationRuntimeSettingsTests
    {
        private SimulationConfig _config;
        private float _originalTimeScale;
        private float _originalFixedDeltaTime;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<SimulationConfig>();
            _originalTimeScale = Time.timeScale;
            _originalFixedDeltaTime = Time.fixedDeltaTime;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _originalTimeScale;
            Time.fixedDeltaTime = _originalFixedDeltaTime;
            Object.DestroyImmediate(_config);
        }

        [Test]
        public void Apply_UsesConfiguredSeedAndTimeSettings()
        {
            _config.RandomSeed = 123;
            _config.TimeScale = 2f;
            _config.FixedTimestep = 0.04f;

            int seed = SimulationRuntimeSettings.Apply(_config, 456);

            Assert.That(seed, Is.EqualTo(123));
            Assert.That(Time.timeScale, Is.EqualTo(2f));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(0.04f).Within(0.000001f));
        }

        [Test]
        public void Apply_UsesFallbackForRandomSeed()
        {
            _config.RandomSeed = -1;

            int seed = SimulationRuntimeSettings.Apply(_config, 456);

            Assert.That(seed, Is.EqualTo(456));
        }
    }
}
