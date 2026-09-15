using NUnit.Framework;
using RobotSNAP.Agents;
using RobotSNAP.Agents.Movement.Controllers;
using RobotSNAP.Agents.Movement.Interfaces;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// The external mode is the hook the Python API drives: the human walks the velocity it is given, nothing
    /// else, and a driver that stops refreshing its command must leave the crowd standing rather than walking
    /// on forever.
    /// </summary>
    public sealed class ExternalControlTests
    {
        private HumanConfig _config;
        private ExternalControlController _controller;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<HumanConfig>();
            _config.maxSpeed = 2f;
            _controller = new ExternalControlController(_config, commandTimeout: 1f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_config != null)
                Object.DestroyImmediate(_config);

            _config = null;
            _controller = null;
        }

        private Vector2 Compute() => _controller.ComputeVelocity(
            Vector2.zero,
            Vector2.zero,
            new Vector2(10f, 0f),
            null,
            null,
            null,
            RobotObservation.None,
            0.02f);

        [Test]
        public void WithoutACommand_TheHumanStandsStill()
        {
            Assert.That(_controller.HasCommand, Is.False);
            Assert.That(Compute(), Is.EqualTo(Vector2.zero));
            Assert.That(_controller.GetConfidence(), Is.EqualTo(0f));
        }

        [Test]
        public void TheCommandIsReplayedAsTheDesiredVelocity()
        {
            _controller.SetCommand(new Vector2(1.5f, -0.5f));

            Assert.That(_controller.HasCommand, Is.True);
            Assert.That(Compute(), Is.EqualTo(new Vector2(1.5f, -0.5f)));
            Assert.That(_controller.GetConfidence(), Is.EqualTo(1f));
        }

        [Test]
        public void ACommandFasterThanTheConfigurationIsClamped()
        {
            _config.maxSpeed = 1.2f;
            _controller.SetCommand(new Vector2(10f, 0f));

            Assert.That(Compute().magnitude, Is.EqualTo(1.2f).Within(0.001f),
                "The configuration of the agent stays the upper bound.");
        }

        [Test]
        public void ClearingTheCommand_StopsTheHuman()
        {
            _controller.SetCommand(new Vector2(1f, 0f));
            Assert.That(Compute().magnitude, Is.GreaterThan(0f));

            _controller.ClearCommand();

            Assert.That(_controller.HasCommand, Is.False);
            Assert.That(Compute(), Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void TheCommandExpiresWhenTheExternalDriverStops()
        {
            // The rule itself, as a pure function: past the timeout the command is no longer fresh.
            Assert.That(ExternalControlController.IsCommandFresh(commandTime: 10f, now: 10.5f, timeout: 1f), Is.True);
            Assert.That(ExternalControlController.IsCommandFresh(commandTime: 10f, now: 11.5f, timeout: 1f), Is.False);
            Assert.That(ExternalControlController.IsCommandFresh(commandTime: 10f, now: 99f, timeout: 0f), Is.True,
                "A timeout of zero latches the command until it is replaced.");
        }

        [Test]
        public void ALatchedCommandSurvivesAnArbitraryPause()
        {
            var latched = new ExternalControlController(_config, commandTimeout: 0f);
            latched.SetCommand(new Vector2(0.8f, 0f));

            Assert.That(latched.HasCommand, Is.True);
            Assert.That(latched.ComputeVelocity(
                Vector2.zero, Vector2.zero, Vector2.zero, null, null, null, RobotObservation.None, 0.02f),
                Is.EqualTo(new Vector2(0.8f, 0f)));
        }

        [Test]
        public void ResetForgetsTheCommand()
        {
            _controller.SetCommand(new Vector2(1f, 1f));

            _controller.Reset();

            Assert.That(_controller.HasCommand, Is.False);
            Assert.That(Compute(), Is.EqualTo(Vector2.zero));
        }
    }
}
