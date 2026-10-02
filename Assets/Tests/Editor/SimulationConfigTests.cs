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
            _config.TimeScale = 150f;
            _config.FixedTimestep = 0f;
            _config.RosPublishFrequency = 100f;

            Assert.That(_config.EnvironmentCount, Is.EqualTo(1));
            Assert.That(_config.EnvironmentSpacing, Is.EqualTo(0f));
            Assert.That(_config.TimeScale, Is.EqualTo(100f));
            Assert.That(_config.FixedTimestep, Is.EqualTo(0.01f));
            Assert.That(_config.RosPublishFrequency, Is.EqualTo(60f));
        }

        [Test]
        public void TimeScale_ReachesTheTrainingCeilingAndStopsThere()
        {
            _config.TimeScale = 100f;
            Assert.That(_config.TimeScale, Is.EqualTo(100f), "x100 is a training run, not a typo to be clamped away");

            _config.TimeScale = 150f;
            Assert.That(_config.TimeScale, Is.EqualTo(100f), "the ceiling still guards a value past x100");

            _config.TimeScale = -5f;
            Assert.That(_config.TimeScale, Is.EqualTo(0f), "the floor is still 0");
        }

        [Test]
        public void Clone_CopiesConfigurationWithoutSharingInstance()
        {
            _config.DefaultScenario = "warehouse";
            _config.RosPrefix = "robot_1";
            _config.TimeScale = 2f;
            _config.Topics.CmdVel = "/drive";

            SimulationConfig clone = _config.Clone();
            try
            {
                Assert.That(clone, Is.Not.SameAs(_config));
                Assert.That(clone.DefaultScenario, Is.EqualTo("warehouse"));
                Assert.That(clone.RosPrefix, Is.EqualTo("robot_1"));
                Assert.That(clone.TimeScale, Is.EqualTo(2f));
                Assert.That(clone.Topics.CmdVel, Is.EqualTo("/drive"));

                // The table is copied rather than shared: editing the copy of a configuration must not edit
                // the configuration a session is still running under.
                clone.Topics.CmdVel = "/wheel";
                Assert.That(_config.Topics.CmdVel, Is.EqualTo("/drive"));
            }
            finally
            {
                Object.DestroyImmediate(clone);
            }
        }

        [Test]
        public void ResetToDefaultsPutsTheShippedTopicNamesBack()
        {
            _config.Topics.CmdVel = "/drive";
            _config.Topics.Scan = "/lidar";

            _config.ResetToDefaults();

            Assert.That(_config.Topics.CmdVel, Is.EqualTo("/cmd_vel"));
            Assert.That(_config.Topics.Scan, Is.EqualTo("/scan"));
        }
    }
}
