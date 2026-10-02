using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using RobotSNAP.Agents;
using RobotSNAP.Core;
using RobotSNAP.Core.Scenario;
using RobotSNAP.ROS;
using UnityEngine;
using UnityEngine.TestTools;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// The bridge command that reproduces the red Stop button of the interface.
    ///
    /// The button does not stop the session itself: it publishes a <see cref="StopSimulationCommand"/>, and
    /// the Scenario Manager answers by pausing the clock, clearing the agents of every environment and
    /// keeping the scenario and the map, which puts the session back in Ready. The command has to end in that
    /// same state, so what is measured here is the very path the button drives.
    ///
    /// An edit-mode test runs in an empty scene and cannot run the lifecycle the game runs - a component added
    /// here does not call its own Awake or Start, except for the <c>[ExecuteAlways]</c> singletons. The
    /// session is therefore stood up by hand: a Clock and a Supervisor are added for real, and the manager is
    /// planted with the fields the stop reads and wired to the bus through the same handler its Start would
    /// subscribe. Everything else is the real code: the router, the event, the manager's reaction to it, and
    /// the GameManager's ClearAgents.
    /// </summary>
    public sealed class SimulationStopCommandTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly SimulationCommandRouter _router = new SimulationCommandRouter();
        private readonly List<GameObject> _created = new List<GameObject>();

        private ScenarioManager _manager;
        private GameManager _environment;
        private HumanPoolManager _pool;
        private GameObject _human;
        private Supervisor _supervisor;
        private Clock _clock;
        private Action<StopSimulationCommand> _stopHandler;

        [TearDown]
        public void TearDown()
        {
            if (_stopHandler != null)
            {
                // The bus outlives the fixture, so the handler is removed rather than left for another test.
                EventBus.Instance.Unsubscribe(_stopHandler);
                _stopHandler = null;
            }

            // Read the configuration before the object carrying it goes away: a Supervisor that was added
            // without one makes its own, and that instance is not owned by the scene.
            SimulationConfig config = _supervisor != null ? _supervisor.ActiveConfig : null;

            foreach (GameObject created in _created)
            {
                if (created != null)
                    UnityEngine.Object.DestroyImmediate(created);
            }

            _created.Clear();

            if (config != null && config.name == "DefaultConfig")
                UnityEngine.Object.DestroyImmediate(config);

            _manager = null;
            _environment = null;
            _pool = null;
            _human = null;
            _supervisor = null;
            _clock = null;
        }

        [Test]
        public void StopSimulationIsRefusedWhenNoScenarioManagerIsInTheScene()
        {
            Assert.That(
                UnityEngine.Object.FindAnyObjectByType<ScenarioManager>(),
                Is.Null,
                "a session with no scenario manager is the state this case pins");

            CommandResult result = _router.Execute("{\"command\":\"stop_simulation\"}");

            Assert.That(result.Ok, Is.False, "a stop nobody can carry out is refused, not reported as done");
            Assert.That(result.Command, Is.EqualTo("stop_simulation"));
            Assert.That(result.Message, Does.Contain("no scenario manager"));
        }

        [Test]
        public void StopSimulationIsRefusedWhenNoScenarioIsLoaded()
        {
            BuildManager();

            CommandResult result = _router.Execute("{\"command\":\"stop_simulation\"}");

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Message, Does.Contain("no scenario loaded"));
        }

        [Test]
        public void StopClearsEveryRobotOfAMultiRobotEnvironment()
        {
            var scenario = new ScenarioData { Info = new ScenarioInfo { Name = "fleet" } };
            BuildSession(scenario, "fleet");

            // A scenario that fields two robots: both belong to the environment, and the stop has to let go
            // of both. The bug this case pins was a search for a single robot, which left the others standing.
            GameObject rosterObject = NewObject("test-roster");
            rosterObject.transform.SetParent(_environment.transform);
            RobotRoster roster = rosterObject.AddComponent<RobotRoster>();

            GameObject firstHost = NewObject("robot_1");
            firstHost.transform.SetParent(rosterObject.transform);
            Robot first = firstHost.AddComponent<Robot>();

            GameObject secondHost = NewObject("robot_2");
            secondHost.transform.SetParent(rosterObject.transform);
            Robot second = secondHost.AddComponent<Robot>();

            PlantRosterRobots(roster, first, second);
            Assert.That(roster.Count, Is.EqualTo(2), "the environment carries two robots to begin with");

            // Edit mode has no frame in which a deferred Destroy can run, so the engine logs that it cannot
            // carry the destruction out - one message per robot. What this case measures is that the stop lets
            // go of *every* robot, i.e. the roster's own count, which is the state the survivor bug lived in.
            LogAssert.Expect(LogType.Error, new Regex("Destroy may not be called from edit mode"));
            LogAssert.Expect(LogType.Error, new Regex("Destroy may not be called from edit mode"));

            CommandResult result = _router.Execute("{\"command\":\"stop_simulation\"}");

            Assert.That(result.Ok, Is.True);
            Assert.That(roster.Count, Is.EqualTo(0),
                "every robot is let go, not only the first one a single search happened to find");
            Assert.That(roster.Primary, Is.Null, "no robot is left as the one a client would reach");
        }

        [Test]
        public void StopSimulationClearsTheAgentsAndLeavesTheScenarioReady()
        {
            var scenario = new ScenarioData { Info = new ScenarioInfo { Name = "kept" } };
            BuildSession(scenario, "kept");

            Assert.That(
                _manager.CurrentState,
                Is.EqualTo(SimulationState.Running),
                "the scenario is applied and the clock runs, so the stop has something to take the session out of");
            Assert.That(_clock.IsPaused, Is.False);
            Assert.That(_pool.ActiveCount, Is.EqualTo(1), "the environment carries one agent to begin with");

            CommandResult result = _router.Execute("{\"command\":\"stop_simulation\"}");

            Assert.That(result.Ok, Is.True);
            Assert.That(result.Command, Is.EqualTo("stop_simulation"));
            Assert.That(_manager.CurrentState, Is.EqualTo(SimulationState.Ready), "the session is back in Ready");
            Assert.That(result.Message, Does.Contain("Ready"), "the answer names the state the session reached");

            Assert.That(_clock.IsPaused, Is.True, "the stop pauses the clock, like the red button");

            Assert.That(_manager.HasScenarioLoaded, Is.True, "the scenario stays loaded");
            Assert.That(_manager.CurrentScenarioId, Is.EqualTo("kept"));
            Assert.That(_manager.CurrentScenarioData, Is.SameAs(scenario), "the scenario in the scene is the same one");
            Assert.That(_manager.Environments, Does.Contain(_environment), "the environment stays attached to the manager");
            Assert.That(_environment == null, Is.False, "the environment - and the map it holds - was not destroyed");

            Assert.That(_pool.ActiveCount, Is.EqualTo(0), "the agents were cleared");
            Assert.That(_human.activeSelf, Is.False, "the agent was returned to the pool and switched off");
            Assert.That(result.Message, Does.Contain("map kept"));
        }

        /// <summary>A scenario manager on its own, with no scenario loaded and no environment under it.</summary>
        private void BuildManager()
        {
            _manager = NewObject("test-scenario-manager").AddComponent<ScenarioManager>();
            WireStopHandler();
        }

        /// <summary>
        /// A session that has applied the scenario: a Supervisor with a Clock for the state to be derived
        /// from, and a manager holding the scenario, one environment and one agent in its pool.
        /// </summary>
        private void BuildSession(ScenarioData scenario, string scenarioId)
        {
            _clock = NewObject("test-clock").AddComponent<Clock>();

            // The clock comes first: the Supervisor wires the one it finds, where it would otherwise make its
            // own and leave it in the scene.
            _supervisor = NewObject("test-supervisor").AddComponent<Supervisor>();
            Assert.That(Supervisor.Instance, Is.SameAs(_supervisor), "the Supervisor registered itself, as Awake does");
            SetField(_supervisor, "_clock", _clock);

            BuildManager();

            GameObject environmentObject = NewObject("test-environment");
            _environment = environmentObject.AddComponent<GameManager>();

            GameObject poolObject = NewObject("test-pool");
            poolObject.transform.SetParent(environmentObject.transform);
            _pool = poolObject.AddComponent<HumanPoolManager>();

            _human = new GameObject("test-human");
            _created.Add(_human);
            ActiveHumans(_pool).Add(_human);

            SetField(_environment, "_humanPool", _pool);
            SetField(_manager, "_currentScenarioData", scenario);
            SetField(_manager, "_currentScenarioId", scenarioId);
            SetField(_manager, "_scenarioApplied", true);
            SetField(_manager, "_isClockStarted", true);
            SetField(_manager, "_gameManagers", new List<GameManager> { _environment });
        }

        /// <summary>
        /// Subscribes the manager's own stop handler to the bus, which is what its Start does for a session
        /// running in play mode: a component added in edit mode never gets that call.
        /// </summary>
        private void WireStopHandler()
        {
            MethodInfo onStop = typeof(ScenarioManager).GetMethod("OnStopCommand", Private);
            Assert.That(onStop, Is.Not.Null, "the manager still carries the handler the stop event reaches");

            _stopHandler = (Action<StopSimulationCommand>)Delegate.CreateDelegate(
                typeof(Action<StopSimulationCommand>), _manager, onStop);
            EventBus.Instance.Subscribe(_stopHandler);
        }

        private GameObject NewObject(string name)
        {
            var created = new GameObject(name);
            _created.Add(created);
            return created;
        }

        private static List<GameObject> ActiveHumans(HumanPoolManager pool)
        {
            FieldInfo field = typeof(HumanPoolManager).GetField("_activeHumans", Private);
            Assert.That(field, Is.Not.Null);
            return (List<GameObject>)field.GetValue(pool);
        }

        /// <summary>
        /// Fills a roster with live robots without applying a scenario: the roster's own slot list is the
        /// state the stop has to empty, and building the bodies through the scenario loader would drag the
        /// whole map and prefab pipeline into an edit-mode fixture.
        /// </summary>
        private static void PlantRosterRobots(RobotRoster roster, params Robot[] robots)
        {
            Type slotType = typeof(RobotRoster).GetNestedType("Slot", BindingFlags.NonPublic);
            Assert.That(slotType, Is.Not.Null, "the roster still keeps one slot type per robot");

            FieldInfo slots = typeof(RobotRoster).GetField("_slots", Private);
            Assert.That(slots, Is.Not.Null);
            var list = (IList)slots.GetValue(roster);
            Assert.That(list, Is.Not.Null);

            list.Clear();
            foreach (Robot robot in robots)
            {
                object slot = Activator.CreateInstance(slotType);
                slotType.GetField("Id").SetValue(slot, robot.name);
                slotType.GetField("Robot").SetValue(slot, robot);
                list.Add(slot);
            }
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, Private);
            Assert.That(field, Is.Not.Null, $"the test plants '{name}' through reflection");
            field.SetValue(target, value);
        }
    }
}
