using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RobotSNAP.Metrics;
using RobotSNAP.ROS;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// Guards the metrics layer's arithmetic and its document shape. The numbers a benchmark reports are read
    /// long after the run that produced them, so a definition that drifts quietly - a speed averaged over wall
    /// time, a personal space counted per sample instead of per entry - is a result nobody can reproduce. Every
    /// test here pins one of those definitions.
    /// </summary>
    public sealed class MetricsTests
    {
        [TearDown]
        public void ResetStore()
        {
            MetricsStore.Instance.Clear();
        }

        private static EpisodeMetrics Synthetic(string id = "ep")
        {
            return new EpisodeMetrics
            {
                Id = id,
                Index = 7,
                Session = "s_test",
                Scenario = "corridor",
                Robot = "robot_1",
                StartedAt = "2026-09-30T10:00:00.0000000Z",
                Outcome = MetricsContract.OutcomeGoal,
                WorldSeconds = 12.0,
                WallSeconds = 1.2,
                Steps = 600,
                PathLengthMetres = 15.0,
                StraightLineMetres = 10.0,
                AverageSpeedMetresPerSecond = 1.25,
                MaxSpeedMetresPerSecond = 1.6,
                MinHumanDistanceMetres = 0.7,
                AverageHumanDistanceMetres = 3.2,
                PersonalSpaceIntrusions = 2,
                PersonalSpaceSeconds = 1.5,
                PersonalSpaceRadiusMetres = 0.5,
                Robots = new List<string> { "robot_1" },
                Trajectories = new Dictionary<string, List<double[]>>
                {
                    ["robot_1"] = new List<double[]> { new[] { 0.0, 0.0, 0.0 } },
                },
            };
        }

        // -- the record's contract ------------------------------------------

        [Test]
        public void TheEpisodeDocumentCarriesTheContractKeys()
        {
            JObject document = JObject.Parse(Synthetic("ep_1").ToJson());

            foreach (string key in new[]
                     {
                         "id", "index", "scenario", "robot", "started_at", "outcome", "world_seconds",
                         "wall_seconds", "steps", "path_length_m", "straight_line_m",
                         "avg_speed_mps", "max_speed_mps", "min_human_distance_m",
                         "avg_human_distance_m", "min_clearance_m", "robot_radius_m",
                         "human_radius_m", "personal_space_intrusions",
                         "personal_space_seconds", "robots", "trajectories",
                     })
            {
                Assert.That(document.ContainsKey(key), Is.True, $"missing contract key '{key}'");
            }

            Assert.That((string)document["id"], Is.EqualTo("ep_1"));
            Assert.That((int)document["index"], Is.EqualTo(7), "the session ordinal travels with the document");
        }

        // -- the session ordinal ---------------------------------------------

        [Test]
        public void TheStoreHandsOutOnePlacePerEpisodeInOrder()
        {
            MetricsStore store = MetricsStore.Instance;

            string first = store.NextEpisodeId(out int firstIndex);
            string second = store.NextEpisodeId(out int secondIndex);

            Assert.That(firstIndex, Is.EqualTo(1), "the first episode of a session is number one");
            Assert.That(secondIndex, Is.EqualTo(2), "and the counter the id is built from is the same one");
            Assert.That(first, Does.EndWith("-0001"));
            Assert.That(second, Does.EndWith("-0002"));

            store.Clear();
            store.NextEpisodeId(out int afterClear);
            Assert.That(afterClear, Is.EqualTo(1), "a new session numbers its episodes from the start again");
        }

        [Test]
        public void ATrajectoryIsATimeAndAPlanarPosition()
        {
            JObject document = JObject.Parse(Synthetic().ToJson());
            JArray point = (JArray)document["trajectories"]["robot_1"][0];

            Assert.That(point.Count, Is.EqualTo(3), "a trajectory point is [t, x, z]");
            Assert.That((double)point[0], Is.EqualTo(0.0));
        }

        [Test]
        public void AnEmptySessionIsAListNotAnError()
        {
            Assert.That(MetricsStore.Instance.Episodes, Is.Empty);
            Assert.That(MetricsStore.Instance.Get("nothing"), Is.Null);
        }

        [Test]
        public void TheStoreKeepsWhatItWasGivenAndClearsToANewSession()
        {
            MetricsStore store = MetricsStore.Instance;
            string before = store.SessionId;
            store.Add(Synthetic("ep_a"));

            Assert.That(store.Count, Is.EqualTo(1));
            Assert.That(store.Get("ep_a"), Is.Not.Null);

            int cleared = store.Clear();

            Assert.That(cleared, Is.EqualTo(1));
            Assert.That(store.Episodes, Is.Empty);
            Assert.That(store.SessionId, Is.Not.EqualTo(before), "a cleared session starts a new identity");
        }

        // -- trajectories ---------------------------------------------------

        [Test]
        public void ATrajectoryStaysBoundedAndKeepsBothEnds()
        {
            var buffer = new TrajectoryBuffer(4);
            for (int step = 0; step < 64; step++)
                buffer.Add(step, step, 0.0);

            Assert.That(buffer.Count, Is.LessThanOrEqualTo(4));
            Assert.That(buffer.Points[0][0], Is.EqualTo(0.0), "the first sample survives every halving");
            Assert.That(buffer.Stride, Is.GreaterThan(1), "beyond the budget the resolution is halved");
        }

        // -- the arithmetic -------------------------------------------------

        [Test]
        public void PathSpeedAndStraightLineFollowTheirDefinitions()
        {
            var accumulator = NewAccumulator();
            accumulator.Sample(0.0, new Vector3(0f, 0f, 0f), null);
            accumulator.Sample(1.0, new Vector3(1f, 0f, 0f), null);
            accumulator.Sample(2.0, new Vector3(1f, 0f, 2f), null);

            EpisodeMetrics episode = accumulator.Finish(MetricsContract.OutcomeGoal, 2.0, 0.5);

            Assert.That(episode.PathLengthMetres, Is.EqualTo(3.0).Within(1e-6), "1 m then 2 m");
            Assert.That(episode.WorldSeconds, Is.EqualTo(2.0).Within(1e-6));
            Assert.That(episode.AverageSpeedMetresPerSecond, Is.EqualTo(1.5).Within(1e-6));
            Assert.That(episode.Steps, Is.EqualTo(3));
        }

        [Test]
        public void TheStraightLineMetricMeasuresTheGoalNotTheEffort()
        {
            var accumulator = NewAccumulator();
            accumulator.SetGoal(new Vector3(3f, 0f, 4f));
            accumulator.Sample(0.0, Vector3.zero, null);
            accumulator.Sample(5.0, new Vector3(1f, 0f, 1f), null);

            EpisodeMetrics episode = accumulator.Finish(MetricsContract.OutcomeGoal, 5.0, 0.5);

            Assert.That(episode.StraightLineMetres, Is.EqualTo(5.0).Within(1e-6), "3-4-5 from the start pose");
            Assert.That(episode.PathLengthMetres, Is.LessThan(episode.StraightLineMetres + 1e-6));
        }

        [Test]
        public void AnEpisodeWithNoHumanSaysSoInsteadOfReportingZero()
        {
            var accumulator = NewAccumulator();
            accumulator.Sample(0.0, Vector3.zero, null);

            EpisodeMetrics episode = accumulator.Finish(MetricsContract.OutcomeGoal, 1.0, 0.1);

            Assert.That(episode.MinHumanDistanceMetres, Is.EqualTo(EpisodeMetrics.NoHumanDistance));
            Assert.That(episode.AverageHumanDistanceMetres, Is.EqualTo(EpisodeMetrics.NoHumanDistance));
        }

        // -- a run is as long as the session, not as long as its first event -----

        /// <summary>
        /// The trajectory of a session is the whole session, including what came after the goal.
        ///
        /// The recorder used to close an episode the instant its robot arrived, collided or left the map, so a
        /// driver who kept going - the ordinary case when a person is at the controls - had the rest of the run
        /// thrown away. Events now name the run and let it continue; only the end of the session closes it.
        /// </summary>
        [Test]
        public void AnEpisodeKeepsTheTrajectoryItDroveAfterTheEvent()
        {
            var accumulator = NewAccumulator();

            accumulator.Sample(0.0, new Vector3(0f, 0f, 0f), null);
            accumulator.Sample(1.0, new Vector3(1f, 0f, 0f), null);

            // The robot arrives at the goal...
            accumulator.LatchOutcome(MetricsContract.OutcomeGoal);

            // ...and is driven on for two more metres, which is the part the old recorder dropped.
            accumulator.Sample(2.0, new Vector3(2f, 0f, 0f), null);
            accumulator.Sample(3.0, new Vector3(3f, 0f, 0f), null);

            EpisodeMetrics episode = accumulator.Finish(MetricsContract.OutcomeStopped, 3.0, 0.4);

            Assert.That(episode.PathLengthMetres, Is.EqualTo(3.0).Within(1e-6),
                "the path covers the whole run, including the metres driven after the goal");
            Assert.That(episode.WorldSeconds, Is.EqualTo(3.0).Within(1e-6));
            Assert.That(episode.Trajectories["robot_1"].Count, Is.EqualTo(4),
                "every sample of the run is kept, not only the ones before the event");
            Assert.That(episode.Outcome, Is.EqualTo(MetricsContract.OutcomeGoal),
                "the run is filed under what it achieved, not under the button that stopped it");
        }

        /// <summary>The first thing that happens names the run; what happens next does not rename it.</summary>
        [Test]
        public void TheFirstEventNamesTheRunAndLaterOnesDoNot()
        {
            var accumulator = NewAccumulator();

            accumulator.LatchOutcome(MetricsContract.OutcomeCollision);
            accumulator.LatchOutcome(MetricsContract.OutcomeGoal);
            accumulator.LatchOutcome(MetricsContract.OutcomeOutOfBounds);
            accumulator.LatchOutcome(null);

            EpisodeMetrics episode = accumulator.Finish(MetricsContract.OutcomeTimeout, 4.0, 0.4);

            Assert.That(episode.Outcome, Is.EqualTo(MetricsContract.OutcomeCollision),
                "the collision came first, so a goal reached afterwards does not erase it");
        }

        /// <summary>A run nothing happened to is still filed under why it stopped.</summary>
        [Test]
        public void ARunWithNoEventIsFiledUnderWhyItStopped()
        {
            var accumulator = NewAccumulator();
            accumulator.Sample(0.0, Vector3.zero, null);

            EpisodeMetrics episode = accumulator.Finish(MetricsContract.OutcomeStopped, 1.0, 0.1);

            Assert.That(episode.Outcome, Is.EqualTo(MetricsContract.OutcomeStopped));
        }

        [Test]
        public void PersonalSpaceCountsEntriesNotSamples()
        {
            var accumulator = NewAccumulator(personalSpaceRadius: 0.5);
            var outside = new List<HumanSample> { new HumanSample(3, new Vector3(1f, 0f, 0f)) };
            var inside = new List<HumanSample> { new HumanSample(3, new Vector3(0.4f, 0f, 0f)) };

            accumulator.Sample(0.0, Vector3.zero, outside);
            accumulator.Sample(1.0, Vector3.zero, inside);
            accumulator.Sample(2.0, Vector3.zero, inside);
            accumulator.Sample(3.0, Vector3.zero, outside);

            EpisodeMetrics episode = accumulator.Finish(MetricsContract.OutcomeGoal, 3.0, 0.2);

            Assert.That(episode.PersonalSpaceIntrusions, Is.EqualTo(1), "one entry, not one per sample");
            Assert.That(episode.PersonalSpaceSeconds, Is.EqualTo(2.0).Within(1e-6));
            Assert.That(episode.MinHumanDistanceMetres, Is.EqualTo(0.4).Within(1e-6));
            Assert.That(episode.AverageHumanDistanceMetres, Is.EqualTo(0.7).Within(1e-6));
        }

        /// <summary>
        /// A clearance is measured between the bodies, not between the centres.
        ///
        /// Two agents one metre apart with 0.3 m footprints are 0.4 m apart in the only sense a bystander can
        /// see: the metric that read centres called that a comfortable metre and never reported the passage.
        /// The footprints now travel with the sample, so the same geometry reads as what it is - and a pass
        /// that really does leave room still reads as room.
        /// </summary>
        [Test]
        public void TheClearanceIsMeasuredBetweenTheBodiesNotTheCentres()
        {
            var accumulator = NewAccumulator(personalSpaceRadius: 0.5);
            var robots = new List<RobotSample> { new RobotSample("robot_1", Vector3.zero, 0.3f) };
            var broadBodies = new List<HumanSample> { new HumanSample(1, new Vector3(1f, 0f, 0f), 0.3f) };
            var slimBodies = new List<HumanSample> { new HumanSample(1, new Vector3(1f, 0f, 0f), 0.05f) };

            accumulator.Sample(0.0, robots, broadBodies);
            accumulator.Sample(1.0, robots, slimBodies);
            accumulator.Sample(2.0, robots, broadBodies);

            EpisodeMetrics episode = accumulator.Finish(MetricsContract.OutcomeGoal, 2.0, 0.1);

            Assert.That(episode.MinHumanDistanceMetres, Is.EqualTo(1.0).Within(1e-6),
                "the distance metric still reports centre to centre");
            Assert.That(episode.MinClearanceMetres, Is.EqualTo(0.4).Within(1e-6),
                "the clearance takes both footprints off the centre distance");
            Assert.That(episode.RobotRadiusMetres, Is.EqualTo(0.3).Within(1e-6));
            Assert.That(episode.HumanRadiusMetres, Is.EqualTo(0.3).Within(1e-6),
                "the radius recorded is the one of the human that came closest");
            Assert.That(episode.PersonalSpaceIntrusions, Is.EqualTo(2),
                "0.4 m of air is inside a 0.5 m personal space, and the slim pass is not");
        }

        [Test]
        public void TheHumanTrajectoryIsFiledUnderItsOwnKey()
        {
            var accumulator = NewAccumulator();
            var humans = new List<HumanSample> { new HumanSample(7, new Vector3(2f, 0f, 1f)) };

            accumulator.Sample(0.0, Vector3.zero, humans);
            EpisodeMetrics episode = accumulator.Finish(MetricsContract.OutcomeGoal, 1.0, 0.1);

            Assert.That(episode.Trajectories.ContainsKey("robot_1"), Is.True);
            Assert.That(episode.Trajectories.ContainsKey("human_7"), Is.True);
            Assert.That(episode.Trajectories["human_7"][0][2], Is.EqualTo(1.0).Within(1e-6));
        }

        [Test]
        public void EveryRobotOfTheFleetGetsItsOwnTrackAndTheScalarsStayTheTrackedRobots()
        {
            // The accumulator was built for robot_1, so robot_1's motion is the path the metrics describe;
            // robot_2's path is recorded beside it without ever entering the numbers.
            var accumulator = NewAccumulator();
            var fleet = new List<RobotSample>
            {
                new RobotSample("robot_1", new Vector3(0f, 0f, 0f)),
                new RobotSample("robot_2", new Vector3(0f, 0f, 5f)),
            };
            accumulator.Sample(0.0, fleet, null);

            fleet[0] = new RobotSample("robot_1", new Vector3(1f, 0f, 0f));
            fleet[1] = new RobotSample("robot_2", new Vector3(0f, 0f, -5f));
            accumulator.Sample(1.0, fleet, null);

            EpisodeMetrics episode = accumulator.Finish(MetricsContract.OutcomeGoal, 1.0, 0.1);

            Assert.That(episode.Trajectories.ContainsKey("robot_1"), Is.True);
            Assert.That(episode.Trajectories.ContainsKey("robot_2"), Is.True,
                "a scenario with two robots writes two robot tracks, not one");
            Assert.That(episode.Trajectories["robot_2"][1][2], Is.EqualTo(-5.0).Within(1e-6));
            Assert.That(episode.PathLengthMetres, Is.EqualTo(1.0).Within(1e-6),
                "the path is the tracked robot's 1 m, not the fleet's 11 m");
            Assert.That(episode.Robots, Is.EqualTo(new List<string> { "robot_1", "robot_2" }),
                "the tracked robot opens the list of robots the episode saw");
        }

        [Test]
        public void TheTrackedRobotOpensTheDocumentEvenWhenTheFleetListsItLast()
        {
            var accumulator = NewAccumulator();
            var fleet = new List<RobotSample>
            {
                new RobotSample("robot_2", new Vector3(0f, 0f, 5f)),
                new RobotSample("robot_1", new Vector3(2f, 0f, 0f)),
            };

            accumulator.Sample(0.0, fleet, null);
            EpisodeMetrics episode = accumulator.Finish(MetricsContract.OutcomeGoal, 1.0, 0.1);

            Assert.That(episode.Robots[0], Is.EqualTo("robot_1"),
                "the robot the scalar metrics name is the first one a reader meets");
            Assert.That(episode.Robots, Is.EqualTo(new List<string> { "robot_1", "robot_2" }));
        }

        // -- export ---------------------------------------------------------

        [Test]
        public void TheExportWritesASessionFileAnIndexAndACsv()
        {
            string root = Path.Combine(Path.GetTempPath(), "robotsnap_metrics_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                MetricsStore store = MetricsStore.Instance;
                store.Add(Synthetic("ep_export"));

                MetricsExportReport report = MetricsExporter.Export(store, root);

                Assert.That(File.Exists(report.SessionFile), Is.True);
                Assert.That(File.Exists(report.CsvFile), Is.True);
                Assert.That(File.Exists(report.IndexFile), Is.True);

                JObject index = JObject.Parse(File.ReadAllText(report.IndexFile));
                Assert.That((string)index["sessions"][0]["id"], Is.EqualTo(store.SessionId));

                JObject session = JObject.Parse(File.ReadAllText(report.SessionFile));
                Assert.That((int)session["episode_count"], Is.EqualTo(1));
                Assert.That((string)session["episodes"][0]["id"], Is.EqualTo("ep_export"));
                Assert.That((int)session["episodes"][0]["index"], Is.EqualTo(7));

                string csv = File.ReadAllText(report.CsvFile);
                StringAssert.StartsWith("id,index,scenario,robot,started_at,outcome", csv);
                StringAssert.Contains("ep_export,7,corridor", csv);
                StringAssert.Contains("ep_export", csv);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }

        // -- the router surface ---------------------------------------------

        [Test]
        public void TheRouterListsAnEmptySessionWithoutRefusing()
        {
            var router = new SimulationCommandRouter();

            CommandResult result = router.Execute("{\"command\":\"metrics_episodes\"}");

            Assert.That(result.Ok, Is.True);
            Assert.That((int)result.Payload["count"], Is.EqualTo(0));
            Assert.That(result.Payload["episodes"], Is.Empty);
        }

        [Test]
        public void TheRouterAnswersOneEpisodeAndAnUnknownOne()
        {
            MetricsStore.Instance.Add(Synthetic("ep_router"));
            var router = new SimulationCommandRouter();

            CommandResult found = router.Execute("{\"command\":\"metrics_episode\",\"id\":\"ep_router\"}");
            CommandResult missing = router.Execute("{\"command\":\"metrics_episode\",\"id\":\"nope\"}");

            Assert.That(found.Ok, Is.True);
            Assert.That((bool)found.Payload["found"], Is.True);
            Assert.That((string)found.Payload["episode"]["id"], Is.EqualTo("ep_router"));

            Assert.That(missing.Ok, Is.True, "a missing id is an answer, not a refusal");
            Assert.That((bool)missing.Payload["found"], Is.False);
        }

        [Test]
        public void TheRouterClearsTheSession()
        {
            MetricsStore.Instance.Add(Synthetic("ep_clear"));
            var router = new SimulationCommandRouter();

            CommandResult result = router.Execute("{\"command\":\"metrics_clear\"}");

            Assert.That(result.Ok, Is.True);
            Assert.That((int)result.Payload["cleared"], Is.EqualTo(1));
            Assert.That(MetricsStore.Instance.Episodes, Is.Empty);
        }

        // -- helpers --------------------------------------------------------

        private static EpisodeAccumulator NewAccumulator(double personalSpaceRadius = 0.5, int capacity = 64)
        {
            return new EpisodeAccumulator(
                "ep_test",
                1,
                "s_test",
                "corridor",
                "robot_1",
                personalSpaceRadius,
                capacity,
                "2026-09-30T10:00:00.0000000Z",
                0.0);
        }
    }
}
