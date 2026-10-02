using NUnit.Framework;
using RobotSNAP.Core;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// The time the application bar displays at the bottom of the window.
    ///
    /// It is the mission's time, read from the simulation clock - the same counter the state snapshot publishes
    /// as sim_time_seconds - so the bar and a client cannot diverge about how far the world has moved. What the
    /// bar does only while the scene carries no clock is count frames itself, and that fallback rule is exposed
    /// as the pure <see cref="AppStatusBar.SimulatedDelta"/>, which is measured here with synthetic values
    /// instead of waiting on frames.
    /// </summary>
    public sealed class StatusBarClockTests
    {
        private GameObject _clockHost;
        private Clock _clock;

        [SetUp]
        public void SetUp()
        {
            _clockHost = new GameObject("status_bar_clock_under_test");
            _clock = _clockHost.AddComponent<Clock>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_clockHost != null)
                Object.DestroyImmediate(_clockHost);
        }

        [Test]
        public void TheBarShowsTheClockTheSnapshotPublishes()
        {
            _clock.Initialize();
            _clock.AdvanceSimulated(2.5);

            Assert.That(
                AppStatusBar.DisplayedSeconds(_clock, 999.0),
                Is.EqualTo(2.5).Within(1e-9),
                "the bar reads the clock's elapsed counter, so it cannot drift from sim_time_seconds");
        }

        [Test]
        public void AMissionResetPutsTheDisplayedTimeBackToZero()
        {
            // Resetting a mission goes through Clock.Initialize (the scenario manager's ResetTime), which zeroes
            // the counter the bar reads, so the display follows on its own instead of needing a second reset.
            _clock.Initialize();
            _clock.AdvanceSimulated(12.0);
            Assert.That(_clock.ElapsedSeconds, Is.EqualTo(12.0).Within(1e-9));

            _clock.Initialize();

            Assert.That(_clock.ElapsedSeconds, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(AppStatusBar.DisplayedSeconds(_clock, 999.0), Is.EqualTo(0.0).Within(1e-9));
        }

        [Test]
        public void ASceneWithoutAClockFallsBackToTheBarsOwnCount()
        {
            Assert.That(
                AppStatusBar.DisplayedSeconds(null, 42.0),
                Is.EqualTo(42.0).Within(1e-9),
                "with no clock to read, the bar keeps counting instead of standing still");
        }

        [Test]
        public void TheBarAdvancesWithTheScaleOfTheSession()
        {
            Assert.That(
                AppStatusBar.SimulatedDelta(0.02f, 10f),
                Is.EqualTo(0.2f).Within(1e-6f),
                "the fallback count advances with the scale the session runs at");
        }

        [Test]
        public void TheBarKeepsTheWallRateAtScaleOne()
        {
            Assert.That(AppStatusBar.SimulatedDelta(0.02f, 1f), Is.EqualTo(0.02f).Within(1e-6f));
        }

        [Test]
        public void ANegativeDeltaOrScaleDoesNotTakeTimeBack()
        {
            Assert.That(AppStatusBar.SimulatedDelta(-0.02f, 10f), Is.EqualTo(0f), "a negative frame adds nothing");
            Assert.That(AppStatusBar.SimulatedDelta(0.02f, -10f), Is.EqualTo(0f), "a negative scale adds nothing");
        }
    }
}
