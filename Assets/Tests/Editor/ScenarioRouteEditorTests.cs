using System.Collections.Generic;
using NUnit.Framework;
using RobotSNAP.Agents;
using RobotSNAP.Core.Scenario;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// The scenario wizard drives several robots, and what it writes is what the runtime builds. These tests
    /// hold that contract: one robot route per robot of the scenario, each with its own type, its own speed and
    /// its own points, read from and written back to the <c>robots</c> list.
    /// </summary>
    public sealed class ScenarioRouteEditorTests
    {
        private const string CreationTabPath = "Assets/UI/Tabs/Scenarios/uxml/ScenarioCreationTab.uxml";

        /// <summary>
        /// An editor bound to the real creation tab, because the fields it binds are the tab's: a renamed
        /// element would fail here rather than the first time somebody opens the wizard.
        /// </summary>
        private static ScenarioRouteEditor BuildEditor()
        {
            VisualTreeAsset tab = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(CreationTabPath);
            Assert.That(tab, Is.Not.Null, $"The creation tab UXML is missing at {CreationTabPath}.");
            return new ScenarioRouteEditor(tab.Instantiate());
        }

        private static void AddPoint(ScenarioData scenario, string reference, float x, float z)
        {
            scenario.Points[reference] = new RefPoint { X = x, Y = 0f, Z = z };
        }

        /// <summary>A scenario in the shape the wizard saves: two robots of different types and one crowd.</summary>
        private static ScenarioData BuildTwoRobotScenario()
        {
            var scenario = new ScenarioData
            {
                Info = new ScenarioInfo { Name = "Two robots" },
                Points = new Dictionary<string, RefPoint>(),
                Robots = new List<RobotScenarioConfig>
                {
                    new RobotScenarioConfig
                    {
                        Id = "robot_1",
                        Type = "kuri",
                        StartRef = "robot_1_start",
                        GoalRef = "robot_1_goal",
                        Speed = 0.31f
                    },
                    new RobotScenarioConfig
                    {
                        Id = "robot_2",
                        Type = "jackal",
                        StartRef = "robot_2_start",
                        WaypointRefs = new List<string> { "robot_2_waypoint_1" },
                        GoalRef = "robot_2_goal",
                        Speed = 1f
                    }
                },
                Humans = new List<HumanScenarioConfig>()
            };

            AddPoint(scenario, "robot_1_start", 1f, 2f);
            AddPoint(scenario, "robot_1_goal", 9f, 2f);
            AddPoint(scenario, "robot_2_start", 1f, 6f);
            AddPoint(scenario, "robot_2_waypoint_1", 5f, 6f);
            AddPoint(scenario, "robot_2_goal", 9f, 6f);
            return scenario;
        }

        [Test]
        public void Reset_OpensOnOneRobotAndOneCrowdRoute()
        {
            ScenarioRouteEditor editor = BuildEditor();
            editor.Reset();

            Assert.That(editor.RobotRouteCount, Is.EqualTo(1), "A scenario always drives at least one robot.");
            Assert.That(editor.PrimaryRobotTypeName, Is.EqualTo(RobotProfiles.Default.DisplayName));
        }

        [Test]
        public void Load_ReadsEveryRobotOfTheScenario()
        {
            ScenarioRouteEditor editor = BuildEditor();
            editor.Load(BuildTwoRobotScenario());

            Assert.That(editor.RobotRouteCount, Is.EqualTo(2));
            Assert.That(editor.RobotRouteSummary, Does.Contain(RobotProfiles.Find("jackal").DisplayName));
            Assert.That(editor.RobotRouteSummary, Does.Contain(RobotProfiles.Find("kuri").DisplayName));
        }

        /// <summary>
        /// A robot's start and its objectives can be areas, like a crowd's: the wizard reads the shape out of
        /// the point table and writes it back, so a scenario that scattered its robot keeps scattering it.
        /// </summary>
        [Test]
        public void LoadThenWrite_KeepsTheAreasOfARobotRoute()
        {
            var scenario = new ScenarioData
            {
                Info = new ScenarioInfo { Name = "Robot in areas" },
                Points = new Dictionary<string, RefPoint>(),
                Robots = new List<RobotScenarioConfig>
                {
                    new RobotScenarioConfig
                    {
                        Id = "robot_1",
                        Type = "jackal",
                        StartRef = "robot_1_start",
                        GoalRef = "robot_1_goal",
                        Speed = 2f
                    }
                },
                Humans = new List<HumanScenarioConfig>()
            };

            scenario.Points["robot_1_start"] = RefPoint.FromBounds(
                new Vector3(1f, 0f, 2f), new Vector3(4f, 0f, 3f));
            scenario.Points["robot_1_start"].Yaw = 90f;
            scenario.Points["robot_1_goal"] = RefPoint.FromBounds(
                new Vector3(9f, 0f, 2f), new Vector3(2f, 0f, 2f));

            ScenarioRouteEditor editor = BuildEditor();
            editor.Load(scenario);

            var saved = new ScenarioData { Info = new ScenarioInfo { Name = "Saved" } };
            editor.WriteToScenario(saved);

            Assert.That(saved.Robots, Has.Count.EqualTo(1));
            RefPoint start = saved.Points[saved.Robots[0].StartRef];
            Assert.That(start.IsBounds, Is.True,
                "The start of a robot the author turned into an area has to stay one.");
            Assert.That(start.ToBounds().size.x, Is.EqualTo(4f).Within(0.01f));
            Assert.That(start.ToBounds().size.z, Is.EqualTo(3f).Within(0.01f));
            Assert.That(start.Yaw, Is.EqualTo(90f).Within(0.1f),
                "The heading of a robot belongs to its start, area or not.");

            RefPoint goal = saved.Points[saved.Robots[0].GoalRef];
            Assert.That(goal.IsBounds, Is.True,
                "The objective of a robot the author turned into an area has to stay one.");
            Assert.That(goal.ToBounds().size.x, Is.EqualTo(2f).Within(0.01f));
            Assert.That(goal.ToBounds().size.z, Is.EqualTo(2f).Within(0.01f));
        }

        [Test]
        public void Load_OfALegacySingleRobotSection_StillOpensThatRobot()
        {
            var scenario = new ScenarioData
            {
                Info = new ScenarioInfo { Name = "Legacy" },
                Points = new Dictionary<string, RefPoint>(),
                Robot = new RobotScenarioConfig
                {
                    Id = "robot_1",
                    Type = "jackal",
                    StartRef = "start_robot",
                    GoalRef = "end_robot",
                    Speed = 2f
                },
                Humans = new List<HumanScenarioConfig>()
            };
            AddPoint(scenario, "start_robot", 0f, 0f);
            AddPoint(scenario, "end_robot", 5f, 0f);

            ScenarioRouteEditor editor = BuildEditor();
            editor.Load(scenario);

            Assert.That(editor.RobotRouteCount, Is.EqualTo(1));
            Assert.That(editor.PrimaryRobotTypeName, Is.EqualTo(RobotProfiles.Find("jackal").DisplayName));
        }

        [Test]
        public void WriteToScenario_EmitsEveryRobotAndDropsTheLegacySection()
        {
            ScenarioRouteEditor editor = BuildEditor();
            editor.Load(BuildTwoRobotScenario());

            var saved = new ScenarioData { Info = new ScenarioInfo { Name = "Saved" } };
            editor.WriteToScenario(saved);

            Assert.That(saved.Robots, Is.Not.Null);
            Assert.That(saved.Robots.Count, Is.EqualTo(2), "One entry per robot, or the second robot is lost.");
            Assert.That(saved.Robot, Is.Null,
                "Writing both shapes would leave one scenario holding two different robots.");

            Assert.That(saved.Robots[0].Id, Is.EqualTo("robot_1"));
            Assert.That(saved.Robots[0].Type, Is.EqualTo("kuri"));
            Assert.That(saved.Robots[0].Speed, Is.EqualTo(0.31f).Within(0.001f));

            Assert.That(saved.Robots[1].Id, Is.EqualTo("robot_2"));
            Assert.That(saved.Robots[1].Type, Is.EqualTo("jackal"));
            Assert.That(saved.Robots[1].Speed, Is.EqualTo(1f).Within(0.001f));
            Assert.That(saved.Robots[1].WaypointRefs, Has.Count.EqualTo(1),
                "The waypoint between start and goal is part of the route.");

            // Every reference the robots name has to exist, or the runtime resolves the point to the origin.
            foreach (RobotScenarioConfig robot in saved.Robots)
            {
                Assert.That(saved.Points, Does.ContainKey(robot.StartRef));
                Assert.That(saved.Points, Does.ContainKey(robot.GoalRef));
            }

            Assert.That(saved.Points["robot_1_start"].X, Is.EqualTo(1f).Within(0.01f));
            Assert.That(saved.Points["robot_2_goal"].Z, Is.EqualTo(6f).Within(0.01f));
        }

        [Test]
        public void AddRobotRoute_AddsARobotThatSurvivesSaving()
        {
            ScenarioRouteEditor editor = BuildEditor();
            editor.Reset();
            editor.AddRobotRoute();

            Assert.That(editor.RobotRouteCount, Is.EqualTo(2));

            var saved = new ScenarioData { Info = new ScenarioInfo { Name = "Three" } };
            editor.WriteToScenario(saved);

            Assert.That(saved.Robots, Has.Count.EqualTo(2));
            Assert.That(saved.Robots[0].Id, Is.EqualTo("robot_1"));
            Assert.That(saved.Robots[1].Id, Is.EqualTo("robot_2"));
            // Two robots on the same point would spend the run pushing each other apart.
            Assert.That(saved.Points[saved.Robots[0].StartRef].Z,
                Is.Not.EqualTo(saved.Points[saved.Robots[1].StartRef].Z).Within(0.01f));
        }

        [Test]
        public void RemoveActiveRoute_RefusesToDropTheLastRobot()
        {
            ScenarioRouteEditor editor = BuildEditor();
            editor.Reset();

            Assert.That(editor.RobotRouteCount, Is.EqualTo(1));
            editor.RemoveActiveRoute();

            Assert.That(editor.RobotRouteCount, Is.EqualTo(1),
                "A scenario with no robot is not a scenario this application can run.");
        }
    }
}
