using NUnit.Framework;
using RobotSNAP.Core;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    public sealed class SimulationConfigTests
    {
        private SimulationConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<SimulationConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
        }

        [Test]
        public void NumericSettings_AreClampedToSupportedRanges()
        {
            _config.EnvironmentCount = 0;
            _config.EnvironmentSpacing = -1f;
            _config.TimeScale = 99f;
            _config.FixedTimestep = 0f;
            _config.RosPublishFrequency = 100f;

            Assert.That(_config.EnvironmentCount, Is.EqualTo(1));
            Assert.That(_config.EnvironmentSpacing, Is.EqualTo(0f));
            Assert.That(_config.TimeScale, Is.EqualTo(10f));
            Assert.That(_config.FixedTimestep, Is.EqualTo(0.01f));
            Assert.That(_config.RosPublishFrequency, Is.EqualTo(60f));
        }

        [Test]
        public void Clone_CopiesConfigurationWithoutSharingInstance()
        {
            _config.DefaultScenario = "warehouse";
            _config.RosPrefix = "robot_1";
            _config.TimeScale = 2f;

            SimulationConfig clone = _config.Clone();
            try
            {
                Assert.That(clone, Is.Not.SameAs(_config));
                Assert.That(clone.DefaultScenario, Is.EqualTo("warehouse"));
                Assert.That(clone.RosPrefix, Is.EqualTo("robot_1"));
                Assert.That(clone.TimeScale, Is.EqualTo(2f));
            }
            finally
            {
                Object.DestroyImmediate(clone);
            }
        }
    }
}
