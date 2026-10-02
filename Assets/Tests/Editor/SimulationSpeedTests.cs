using NUnit.Framework;
using RobotSNAP.Core;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// What raising the session's speed does to the clock every stream is stamped from.
    ///
    /// The scale is applied by the clock itself, and the clock advances one frame at a time through
    /// <see cref="Clock.Advance"/>, so the rule can be measured without waiting on real frames. A scene that
    /// already carries a clock is driven and then put back where it was found, which is what the play session
    /// of the open scene looks like from an edit-mode test.
    /// </summary>
    public sealed class SimulationSpeedTests
    {
        private GameObject _host;
        private Clock _clock;
        private bool _created;
        private float _originalTimeScale;
        private float _originalClockScale;

        [SetUp]
        public void SetUp()
        {
            _originalTimeScale = Time.timeScale;
            _created = Clock.Instance == null;
            if (_created)
            {
                _host = new GameObject("clock_under_test");
                _clock = _host.AddComponent<Clock>();
            }
            else
            {
                _clock = Clock.Instance;
            }

            _originalClockScale = _clock.TimeScale;
            _clock.SetTimeScale(1f);
            _clock.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _originalTimeScale;
            if (_created)
            {
                if (_host != null) Object.DestroyImmediate(_host);
            }
            else if (_clock != null)
            {
                // Put the scene's own clock back where the test found it.
                _clock.SetTimeScale(_originalClockScale);
                _clock.Initialize();
            }
        }

        [Test]
        public void TheClockAdvancesWithTheScaleAndNotWithTheSquareOfIt()
        {
            _clock.SetTimeScale(5f);

            _clock.Advance(0.1);

            Assert.That(
                _clock.CurrentTimeSeconds,
                Is.EqualTo(0.5).Within(1e-6),
                "a tenth of a second of wall time at scale five is half a second of simulation; squaring the " +
                "scale is the bug this pins");
        }

        [Test]
        public void TheClockKeepsTheWallRateAtScaleOne()
        {
            _clock.Advance(0.25);

            Assert.That(_clock.CurrentTimeSeconds, Is.EqualTo(0.25).Within(1e-6));
        }

        [Test]
        public void TheScaleIsAppliedToEveryFrameTheWorldAdvances()
        {
            _clock.SetTimeScale(2f);

            _clock.Advance(0.05);
            _clock.Advance(0.05);

            Assert.That(_clock.CurrentTimeSeconds, Is.EqualTo(0.2).Within(1e-6));
        }

        [Test]
        public void APauseFreezesTheClockAndTheResumeContinuesFromThere()
        {
            _clock.SetTimeScale(2f);
            _clock.Advance(0.1);
            Assert.That(_clock.CurrentTimeSeconds, Is.EqualTo(0.2).Within(1e-6));

            _clock.Pause();
            _clock.Advance(0.5);
            Assert.That(_clock.CurrentTimeSeconds, Is.EqualTo(0.2).Within(1e-6), "a paused clock does not move");

            _clock.Resume();
            _clock.Advance(0.1);
            Assert.That(_clock.CurrentTimeSeconds, Is.EqualTo(0.4).Within(1e-6));
        }

        [Test]
        public void TheClockReportsTheScaleAndTheScaledMilliseconds()
        {
            _clock.SetTimeScale(4f);
            _clock.Advance(0.25);

            Assert.That(_clock.TimeScale, Is.EqualTo(4f));
            Assert.That(_clock.CurrentTimeMillis, Is.EqualTo(1000.0).Within(1e-3));
        }

        [Test]
        public void APhysicsStepIsOneStampAndIsNotScaledASecondTime()
        {
            _clock.SetTimeScale(10f);

            _clock.AdvanceSimulated(Time.fixedDeltaTime);
            Assert.That(
                _clock.CurrentTimeSeconds,
                Is.EqualTo(Time.fixedDeltaTime).Within(1e-9),
                "a physics step is already simulation time: the physics loop runs ten times more often per " +
                "second of wall time at a scale of ten, so scaling the step by ten again would run the " +
                "stamped clock a hundred times faster than the world it stamps");

            _clock.AdvanceSimulated(Time.fixedDeltaTime * 2.0);
            Assert.That(
                _clock.CurrentTimeSeconds,
                Is.EqualTo(Time.fixedDeltaTime * 3.0).Within(1e-9),
                "three steps are three steps whatever the scale is");
        }

        [Test]
        public void EveryPhysicsStepCarriesItsOwnStamp()
        {
            _clock.SetTimeScale(10f);

            double first = _clock.CurrentTimeSeconds;
            _clock.AdvanceSimulated(Time.fixedDeltaTime);
            double second = _clock.CurrentTimeSeconds;
            _clock.AdvanceSimulated(Time.fixedDeltaTime);
            double third = _clock.CurrentTimeSeconds;

            Assert.That(second, Is.GreaterThan(first), "two steps must not share one stamp");
            Assert.That(third, Is.GreaterThan(second));
            Assert.That(
                second - first,
                Is.EqualTo(third - second).Within(1e-9),
                "the step between two stamps is the physics step, so a client can place a measurement " +
                "between the two poses around it");
        }
    }
}
