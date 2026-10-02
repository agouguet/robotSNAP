using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RobotSNAP.Core;
using RobotSNAP.Core.Scenario;
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
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private GameObject _clockHost;
        private Clock _clock;
        private readonly List<GameObject> _created = new List<GameObject>();
        private System.Action<StartSimulationCommand> _startHandler;

        [SetUp]
        public void SetUp()
        {
            _clockHost = new GameObject("status_bar_clock_under_test");
            _clock = _clockHost.AddComponent<Clock>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_startHandler != null)
            {
                // The bus outlives the fixture, so the handler is removed rather than left for another test.
                EventBus.Instance.Unsubscribe(_startHandler);
                _startHandler = null;
            }

            if (_clockHost != null)
                Object.DestroyImmediate(_clockHost);

            foreach (GameObject created in _created)
            {
                if (created != null)
                    Object.DestroyImmediate(created);
            }

            _created.Clear();
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
        public void AScenarioResetPutsTheDisplayedTimeBackToZero()
        {
            // The rule above, walked through the path the application actually takes: the scenario manager's
            // reset re-applies the scenario and writes the clock itself, so a caller - the reset command of the
            // bridge, a button, anything else - cannot leave the episode's origin behind at the session total.
            _clock.Initialize();
            _clock.AdvanceSimulated(37.5);
            Assert.That(
                AppStatusBar.DisplayedSeconds(Clock.Instance, 999.0),
                Is.EqualTo(37.5).Within(1e-9),
                "the bar is showing the session's total before the reset");

            ScenarioManager manager = NewManager();
            SetField(manager, "_currentScenarioData",
                new ScenarioData { Info = new ScenarioInfo { Name = "reset_under_test" } });
            SetField(manager, "_gameManagers", new List<GameManager>());

            manager.ResetAndReapply();

            Assert.That(_clock.ElapsedSeconds, Is.EqualTo(0.0).Within(1e-9),
                "the reset puts the session's clock back to the start of the episode");
            Assert.That(
                AppStatusBar.DisplayedSeconds(Clock.Instance, 999.0),
                Is.EqualTo(0.0).Within(1e-9),
                "so the bar reads 00:00 rather than the total of the session");
        }

        [Test]
        public void RestartingAStoppedScenarioPutsTheDisplayedTimeBackToZero()
        {
            // The other way into an episode: a stop leaves the scenario loaded but cleared, and pressing start
            // again re-applies it. That restart is what a run of the same scenario after a stop is, so it puts
            // the clock back too - without it, the bar of the second run carries on from the first one's total.
            ScenarioManager manager = NewManager();
            SetField(manager, "_currentScenarioData",
                new ScenarioData { Info = new ScenarioInfo { Name = "restart_under_test" } });
            SetField(manager, "_gameManagers", new List<GameManager>());
            SetField(manager, "_scenarioApplied", false);
            WireStartHandler(manager);

            _clock.Initialize();
            _clock.AdvanceSimulated(21.0);
            Assert.That(_clock.ElapsedSeconds, Is.EqualTo(21.0).Within(1e-9));

            EventBus.Instance.Publish(new StartSimulationCommand());

            Assert.That(_clock.ElapsedSeconds, Is.EqualTo(0.0).Within(1e-9),
                "a start after a stop begins an episode, and an episode begins at zero");
        }

        /// <summary>
        /// A scenario manager on its own, holding no environment: the reset reaches the clock without needing a
        /// map, a prefab or a crowd, which is what lets this case run in edit mode.
        /// </summary>
        private ScenarioManager NewManager()
        {
            var host = new GameObject("status_bar_reset_manager_under_test");
            _created.Add(host);
            return host.AddComponent<ScenarioManager>();
        }

        /// <summary>
        /// Subscribes the manager's own start handler to the bus, which is what its Start does for a session
        /// running in play mode: a component added in edit mode never gets that call.
        /// </summary>
        private void WireStartHandler(ScenarioManager manager)
        {
            MethodInfo onStart = typeof(ScenarioManager).GetMethod("OnStartCommand", Private);
            Assert.That(onStart, Is.Not.Null, "the manager still carries the handler the start event reaches");

            _startHandler = (System.Action<StartSimulationCommand>)System.Delegate.CreateDelegate(
                typeof(System.Action<StartSimulationCommand>), manager, onStart);
            EventBus.Instance.Subscribe(_startHandler);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, Private);
            Assert.That(field, Is.Not.Null, $"the test plants '{name}' through reflection");
            field.SetValue(target, value);
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
