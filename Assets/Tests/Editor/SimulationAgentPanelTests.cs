using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RobotSNAP.Agents;
using RobotSNAP.CameraControl;
using RobotSNAP.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// The list of agents of the simulation overlay once a session has stopped.
    ///
    /// A stop destroys the robots and sends the pedestrians back to their pool, so the agents the panel
    /// lists are gone from the scene while their names stay on screen. The panel reads its cast from the
    /// camera - the same cast the minimap follows - so what these cases hold together is that pair: the
    /// session change releases the cast, and the release is what empties the list. No test file covered the
    /// agent panel or the simulation tab controller before this one, so one was needed rather than a case
    /// squeezed into a file about another panel.
    /// </summary>
    public sealed class SimulationAgentPanelTests
    {
        private const string OverlayPath = "Assets/UI/Tabs/Simulator/uxml/SimulationOverlay.uxml";
        private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void RemoveWhatTheCaseBuilt()
        {
            foreach (GameObject created in _created)
            {
                if (created != null)
                    Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        private GameObject NewAgent(string name, string tag)
        {
            var agent = new GameObject(name);
            agent.tag = tag;
            _created.Add(agent);
            return agent;
        }

        private GameObject NewHost(string name)
        {
            var host = new GameObject(name);
            _created.Add(host);
            return host;
        }

        private CameraController NewCamera() => NewHost("test-camera").AddComponent<CameraController>();

        private static VisualElement BuildOverlay()
        {
            VisualTreeAsset overlay = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(OverlayPath);
            Assert.That(overlay, Is.Not.Null, $"The simulation overlay UXML is missing at {OverlayPath}.");
            return overlay.Instantiate();
        }

        /// <summary>How many agents the list of the overlay shows right now.</summary>
        private static int RowCount(VisualElement root)
        {
            VisualElement list = root.Q<VisualElement>("AgentList");
            Assert.That(list, Is.Not.Null, "The overlay has to carry the agent list.");
            return list.Query<VisualElement>(className: "agent-row").ToList().Count;
        }

        /// <summary>The heading of the list, glyph and count included.</summary>
        private static string HeadingText(VisualElement root) =>
            root.Q<Button>("AgentListSectionHeader")?.text;

        private static void SetField(object target, string field, object value) =>
            target.GetType().GetField(field, Private).SetValue(target, value);

        /// <summary>
        /// The whole panel: the two agents of a session are listed, the session takes them away exactly as
        /// a stop does, and the list and the selected-agent card are left describing nobody.
        /// </summary>
        [Test]
        public void AStoppedSessionLeavesNoRowAndNoSelection()
        {
            GameObject robot = NewAgent("robot_1", "Agent");
            robot.AddComponent<Robot>();
            GameObject pedestrian = NewAgent("pedestrian_1", "Human");

            CameraController camera = NewCamera();
            VisualElement root = BuildOverlay();
            var panel = new SimulationAgentPanel(root, camera);

            camera.SetFollowTarget(robot.transform);

            Assert.That(RowCount(root), Is.EqualTo(2), "Both agents of a live session are listed.");
            Assert.That(camera.IsFollowing, Is.True);

            // What a stop does to the cast: the robots are destroyed, the pedestrians are pooled and
            // switched off, and the session says so on its way out.
            Object.DestroyImmediate(robot);
            pedestrian.SetActive(false);
            camera.ReleaseFollowableAgents();

            Assert.That(RowCount(root), Is.Zero,
                "An agent that is not in the scene any more has no row to click.");
            Assert.That(HeadingText(root), Does.Contain("Agents List (0)"),
                "The count of the heading reads the list that was just emptied.");
            Assert.That(root.Q<Label>("SelectedAgentName").text, Is.EqualTo("No agent"));
            Assert.That(root.Q<Label>("SelectedAgentBadge").text, Is.EqualTo("Nothing selected"));
            Assert.That(camera.IsFollowing, Is.False,
                "The view cannot stay on an agent the session took away.");
        }

        /// <summary>
        /// The list of the camera holds the agents of a session, so it cannot outlive one: a robot that is
        /// destroyed leaves the list on its own, and a release empties it even when the agent it held was
        /// destroyed before the call - the case that used to keep a dead reference the panel would list.
        /// </summary>
        [Test]
        public void TheCameraLetsGoOfAnAgentThatIsGone()
        {
            GameObject agent = NewAgent("agent_1", "Agent");

            CameraController camera = NewCamera();
            camera.SetFollowTarget(agent.transform);

            Assert.That(camera.GetFollowableTargets(), Has.Count.EqualTo(1));
            Assert.That(camera.IsFollowing, Is.True);

            Object.DestroyImmediate(agent);

            Assert.That(camera.GetFollowableTargets(), Is.Empty,
                "A list built before an agent went away does not outlive it.");

            camera.ReleaseFollowableAgents();

            Assert.That(camera.IsFollowing, Is.False,
                "A release clears the agent even when it was destroyed before the call.");
            Assert.That(camera.GetFollowableTargets(), Is.Empty);
        }

        /// <summary>
        /// The wiring itself: the session announces the end of a run, and the tab controller is what turns
        /// that into the release. Without this case the call could be dropped from the state handler and
        /// the two cases above would still pass.
        ///
        /// The order of a real stop is respected: the agents are cleared first, and the state is published
        /// after. That order is also why the release is measured on the event rather than on the list - a
        /// list rebuilt from a scene whose agent is already gone reads empty whether or not anybody was told
        /// to let go of it.
        ///
        /// An edit-mode test runs none of the lifecycle the game runs - a component added here does not call
        /// its own Awake or OnEnable - so the handler is reached through the same private entry the bus calls
        /// and the two controls it writes are given by hand. What is under test is the release, not the
        /// buttons.
        /// </summary>
        [Test]
        public void LeavingARunReleasesTheCastOfTheView()
        {
            GameObject agent = NewAgent("agent_1", "Agent");

            CameraController camera = NewCamera();
            camera.SetFollowTarget(agent.transform);
            Assert.That(camera.GetFollowableTargets(), Has.Count.EqualTo(1),
                "The view holds the cast of the run.");

            int releases = 0;
            camera.OnFollowTargetChanged += target =>
            {
                if (target == null) releases++;
            };

            // A stop clears the agents of the session, and only then says the session is no longer running.
            Object.DestroyImmediate(agent);

            SimulationTabController tab = NewHost("test-simulation-tab").AddComponent<SimulationTabController>();
            SetField(tab, "cameraController", camera);
            SetField(tab, "_currentState", SimulationState.Running);
            SetField(tab, "_startStopButton", new Button());
            SetField(tab, "_pauseResumeButton", new Button());

            MethodInfo onStateChanged = typeof(SimulationTabController).GetMethod("OnStateChanged", Private);
            Assert.That(onStateChanged, Is.Not.Null, "The tab controller answers the session state.");
            onStateChanged.Invoke(tab, new object[]
            {
                new SimulationStateChangedEvent { NewState = SimulationState.Ready }
            });

            Assert.That(releases, Is.EqualTo(1),
                "The state change of a stop is what tells the view to let go of the agents of the run.");
            Assert.That(camera.GetFollowableTargets().Count, Is.Zero,
                "A stop puts the session back in Ready, and the view is left holding no agent.");
        }
    }
}
