using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RobotSNAP.Core.Scenario;
using UnityEngine;
using VYaml.Serialization;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// The departure window of a route: how many spawn units a scenario gives the window to, and how the
    /// delays are drawn. The runtime that releases the agents is a MonoBehaviour of the scene and stays out of
    /// these tests; what is pinned here is the policy a release obeys.
    /// </summary>
    public sealed class DepartureWindowTests
    {
        private static readonly MethodInfo DrawDepartureDelay = typeof(ScenarioApplier)
            .GetMethod("DrawDepartureDelay", BindingFlags.Static | BindingFlags.NonPublic);

        private static readonly MethodInfo BuildDepartures = typeof(ScenarioApplier)
            .GetMethod("BuildDepartures", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly System.Type DepartureType = typeof(ScenarioApplier)
            .GetNestedType("Departure", BindingFlags.NonPublic);

        [Test]
        public void SpawnWindow_DefaultsToZero() =>
            Assert.That(new HumanScenarioConfig().SpawnWindow, Is.EqualTo(0f));

        [Test]
        public void SpawnWindow_RoundTripsThroughYaml()
        {
            var config = new HumanScenarioConfig { Id = "route_1", Count = 4, SpawnWindow = 12.5f };

            byte[] yaml = YamlSerializer.Serialize(config).ToArray();
            var readBack = YamlSerializer.Deserialize<HumanScenarioConfig>(yaml);
            string text = System.Text.Encoding.UTF8.GetString(yaml);

            Assert.That(text, Does.Contain("spawn_window"));
            Assert.That(text, Does.Not.Contain("SpawnWindow"));
            Assert.That(readBack.SpawnWindow, Is.EqualTo(12.5f));
        }

        [Test]
        public void Delay_IsZeroWithoutSpendingADraw_WhenTheWindowIsZero()
        {
            Random.InitState(4242);
            float delay = DrawDelay(0f);
            float nextDraw = Random.Range(0f, 1f);

            Random.InitState(4242);
            float drawWithoutTheWindow = Random.Range(0f, 1f);

            Assert.That(delay, Is.EqualTo(0f));
            Assert.That(nextDraw, Is.EqualTo(drawWithoutTheWindow));
        }

        [Test]
        public void Delay_RepeatsForTheSameSeed()
        {
            Random.InitState(7);
            float first = DrawDelay(9f);

            Random.InitState(7);

            Assert.That(DrawDelay(9f), Is.EqualTo(first));
        }

        [Test]
        public void Delay_StaysWithinTheWindow()
        {
            Random.InitState(11);

            for (int i = 0; i < 256; i++)
            {
                float delay = DrawDelay(9f);
                Assert.That(delay, Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(9f));
            }
        }

        [Test]
        public void Departures_TakeOneUnitPerFormationAndOnePerScatteredAgent()
        {
            ScenarioApplier applier = BuildApplier(6f);

            try
            {
                IList departures = BuildDeparturesOf(applier, 6);

                // The formation route leaves as one unit of three agents, the scattering route as three.
                Assert.That(departures.Count, Is.EqualTo(4));
                Assert.That(AgentsOf(departures[0]), Is.EqualTo(3));
                Assert.That(AgentsOf(departures[1]), Is.EqualTo(1));
                Assert.That(AgentsOf(departures[2]), Is.EqualTo(1));
                Assert.That(AgentsOf(departures[3]), Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(applier.gameObject);
            }
        }

        [Test]
        public void Departures_FollowTheSeed()
        {
            ScenarioApplier applier = BuildApplier(6f);

            try
            {
                Random.InitState(2026);
                IList first = BuildDeparturesOf(applier, 6);
                Random.InitState(2026);
                IList second = BuildDeparturesOf(applier, 6);

                Assert.That(second.Count, Is.EqualTo(first.Count));
                for (int i = 0; i < first.Count; i++)
                {
                    Assert.That(DelayOf(second[i]), Is.EqualTo(DelayOf(first[i])));
                    Assert.That(DelayOf(first[i]), Is.GreaterThanOrEqualTo(0f).And.LessThanOrEqualTo(6f));
                }
            }
            finally
            {
                Object.DestroyImmediate(applier.gameObject);
            }
        }

        [Test]
        public void Departures_LeaveImmediately_WhenTheScenarioAsksNoWindow()
        {
            ScenarioApplier applier = BuildApplier(0f);

            try
            {
                IList departures = BuildDeparturesOf(applier, 6);

                Assert.That(departures.Count, Is.EqualTo(4));
                foreach (object departure in departures)
                    Assert.That(DelayOf(departure), Is.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(applier.gameObject);
            }
        }

        /// <summary>
        /// A scenario of two routes over the same window: a three-agent wedge, which walks as one formation, and
        /// a three-agent crowd drawn inside a zone, whose agents each own their start and therefore their entry.
        /// </summary>
        private static ScenarioApplier BuildApplier(float spawnWindow)
        {
            var scenario = new ScenarioData
            {
                Info = new ScenarioInfo { Name = "Departure window" },
                Humans = new List<HumanScenarioConfig>
                {
                    new HumanScenarioConfig
                    {
                        Id = "formation",
                        Count = 3,
                        SpawnWindow = spawnWindow,
                        Spawn = SpawnZone("wedge"),
                        Goal = GoalPoint()
                    },
                    new HumanScenarioConfig
                    {
                        Id = "crowd",
                        Count = 3,
                        SpawnWindow = spawnWindow,
                        Spawn = SpawnZone(null),
                        Goal = GoalPoint()
                    }
                }
            };

            var gameObject = new GameObject("DepartureWindowTest");
            var applier = gameObject.AddComponent<ScenarioApplier>();
            typeof(ScenarioApplier)
                .GetField("_currentScenario", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(applier, scenario);
            return applier;
        }

        private static SpawnConfig SpawnZone(string formation) => new SpawnConfig
        {
            Zone = RefPoint.FromBounds(Vector3.zero, new Vector3(10f, 0f, 10f)),
            Formation = formation
        };

        private static GoalConfig GoalPoint() => new GoalConfig
        {
            Type = "point",
            Position = Point.FromVector3(Vector3.zero)
        };

        private static float DrawDelay(float spawnWindow) =>
            (float)DrawDepartureDelay.Invoke(null, new object[] { spawnWindow });

        private static IList BuildDeparturesOf(ScenarioApplier applier, int totalHumans) =>
            (IList)BuildDepartures.Invoke(applier, new object[] { totalHumans });

        private static int AgentsOf(object departure) =>
            (int)DepartureType.GetProperty("Count").GetValue(departure);

        private static float DelayOf(object departure) =>
            (float)DepartureType.GetProperty("Delay").GetValue(departure);
    }
}
