using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RobotSNAP.Metrics;
using UnityEngine;
using UnityEngine.UIElements;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// The analysis tab reads the metrics store and nothing else. These tests pin the things a reader would
    /// notice if they drifted: the list follows the store as episodes end, an episode says which place in the
    /// session it took, the overview counts only the episodes it was handed, a filter narrows both the list and
    /// the selection, a row's glyphs do only what they say, an episode that held no human says so instead of
    /// reporting a zero distance, every agent of an episode gets its own colour, and a confirmation dialog
    /// never acts before the reader has confirmed it.
    /// </summary>
    public sealed class AnalysisTabTests
    {
        /// <summary>
        /// The store is a session-wide singleton that survives a play session when the project runs with no
        /// domain reload, so a test starts from an empty one instead of inheriting whatever the last run left.
        /// </summary>
        [SetUp]
        public void ResetStoreBefore() => MetricsStore.Instance.Clear();

        [TearDown]
        public void ResetStore() => MetricsStore.Instance.Clear();

        private static EpisodeMetrics Episode(
            string id,
            string outcome = MetricsContract.OutcomeGoal,
            double minHuman = 0.8,
            double meanHuman = 2.4,
            int index = 0,
            string scenario = "corridor",
            int intrusions = 2)
        {
            return new EpisodeMetrics
            {
                Id = id,
                Index = index,
                Session = "s_test",
                Scenario = scenario,
                Robot = "robot_1",
                StartedAt = "2026-09-30T10:00:00.0000000Z",
                Outcome = outcome,
                WorldSeconds = 12.0,
                WallSeconds = 1.2,
                Steps = 600,
                PathLengthMetres = 15.0,
                StraightLineMetres = 10.0,
                AverageSpeedMetresPerSecond = 1.25,
                MaxSpeedMetresPerSecond = 1.6,
                MinHumanDistanceMetres = minHuman,
                AverageHumanDistanceMetres = meanHuman,
                PersonalSpaceIntrusions = intrusions,
                PersonalSpaceSeconds = 1.5,
                PersonalSpaceRadiusMetres = 0.5,
                Trajectories = new Dictionary<string, List<double[]>>
                {
                    ["robot_1"] = new List<double[]> { new[] { 0.0, 0.0, 0.0 }, new[] { 1.0, 1.0, 0.5 } },
                    ["human_1"] = new List<double[]> { new[] { 0.0, 2.0, 2.0 } },
                    ["human_2"] = new List<double[]> { new[] { 0.0, -2.0, 2.0 } },
                },
            };
        }

        private static List<VisualElement> Rows(VisualElement list)
            => list.Query<VisualElement>(className: "analysis-episode-row").ToList();

        private static VisualElement RowOf(VisualElement list, string id)
            => Rows(list).FirstOrDefault(row => (string)row.userData == id);

        private static List<string> RowText(VisualElement row)
            => row.Query<Label>().ToList().Select(label => label.text).ToList();

        /// <summary>The value half of the metric row a reader finds by its label.</summary>
        private static string MetricValue(VisualElement root, string label)
        {
            foreach (VisualElement row in root.Query<VisualElement>(className: "analysis-metric-row").ToList())
            {
                List<Label> labels = row.Query<Label>().ToList();
                if (labels.Count >= 2 && labels[0].text == label)
                    return labels[1].text;
            }
            return null;
        }

        /// <summary>
        /// The final half of a metric row - the value the episode ended on, which only a scrubbed row carries.
        /// Null for a row that holds one value, which is every row of an episode read whole.
        /// </summary>
        private static string MetricFinal(VisualElement root, string label)
        {
            foreach (VisualElement row in root.Query<VisualElement>(className: "analysis-metric-row").ToList())
            {
                List<Label> labels = row.Query<Label>().ToList();
                if (labels.Count >= 3 && labels[0].text == label)
                    return labels[2].text;
            }
            return null;
        }

        /// <summary>
        /// One episode a replay can be read through: a robot that drives a straight metre per second for four
        /// seconds, and a human standing 0.4 m off the end of that line. The numbers are the ones the
        /// accumulator would have written, so a prefix can be checked against the value it is heading for -
        /// the closest human is 2.04 m away halfway through and 0.40 m at the end, and the robot only enters
        /// that human's personal space on the last sample.
        /// </summary>
        private static EpisodeMetrics ReplayEpisode()
        {
            return new EpisodeMetrics
            {
                Id = "s_test-0001",
                Index = 1,
                Session = "s_test",
                Scenario = "corridor",
                Robot = "robot_1",
                StartedAt = "2026-09-30T10:00:00.0000000Z",
                Outcome = MetricsContract.OutcomeGoal,
                WorldSeconds = 4.0,
                WallSeconds = 0.4,
                Steps = 5,
                PathLengthMetres = 4.0,
                StraightLineMetres = 4.0,
                AverageSpeedMetresPerSecond = 1.0,
                MaxSpeedMetresPerSecond = 1.0,
                MinHumanDistanceMetres = 0.4,
                AverageHumanDistanceMetres = 2.11263,
                PersonalSpaceIntrusions = 1,
                PersonalSpaceSeconds = 1.0,
                PersonalSpaceRadiusMetres = 0.5,
                Trajectories = new Dictionary<string, List<double[]>>
                {
                    ["robot_1"] = new List<double[]>
                    {
                        new[] { 0.0, 0.0, 0.0 },
                        new[] { 1.0, 1.0, 0.0 },
                        new[] { 2.0, 2.0, 0.0 },
                        new[] { 3.0, 3.0, 0.0 },
                        new[] { 4.0, 4.0, 0.0 },
                    },
                    ["human_1"] = new List<double[]>
                    {
                        new[] { 0.0, 4.0, 0.4 },
                        new[] { 4.0, 4.0, 0.4 },
                    },
                },
            };
        }

        private static List<string> Colours(IReadOnlyList<AnalysisAgentPalette.TrackColour> tracks)
            => tracks.Select(track =>
            {
                var packed = (Color32)track.Colour;
                return $"{packed.r},{packed.g},{packed.b}";
            }).ToList();

        /// <summary>The headline tile of an overview, found by the caption under it.</summary>
        private static VisualElement Tile(VisualElement summaryHost, string caption)
            => summaryHost.Query<VisualElement>(className: "analysis-tile").ToList().FirstOrDefault(tile =>
                tile.Query<Label>(className: "analysis-tile-caption").ToList().Any(label => label.text == caption));

        private static string TileValue(VisualElement summaryHost, string caption)
            => Tile(summaryHost, caption)?.Query<Label>(className: "analysis-tile-value").ToList()[0].text;

        private static List<string> Captions(VisualElement summaryHost, string className)
            => summaryHost.Query<Label>(className: className).ToList().Select(label => label.text).ToList();

        private static AnalysisEpisodeList BoundList(out AnalysisSession session)
        {
            var list = new AnalysisEpisodeList();
            session = new AnalysisSession();
            list.Bind(session);
            return list;
        }

        // -- the list follows the store -------------------------------------

        [Test]
        public void TheEpisodeListFillsFromTheStoreWhenAnEpisodeEnds()
        {
            var list = new AnalysisEpisodeList();
            var session = new AnalysisSession();
            list.Bind(session);

            Assert.That(Rows(list), Is.Empty, "an empty session is an empty list, never an error");
            Assert.That(list.Query<Label>(className: "analysis-empty-message").ToList(), Is.Not.Empty,
                "an empty session says so instead of leaving a blank panel");

            MetricsStore.Instance.Add(Episode("s_test-0001", index: 1));
            Assert.That(session.Poll(), Is.True, "a new episode is a change the session has to notice");

            List<VisualElement> rows = Rows(list);
            Assert.That(rows.Count, Is.EqualTo(1));

            List<string> text = RowText(rows[0]);
            Assert.That(text, Does.Contain("01_corridor"), "the row names the episode by its session ordinal");
            Assert.That(text, Does.Contain("robot_1"), "the row names the robot");
            Assert.That(text, Does.Contain("Reached goal"), "the row names the outcome");
            Assert.That(text, Does.Contain("12.00 s world"), "the row names the simulated duration");
            Assert.That(text.Any(value => value.Contains("2026-09-30")), Is.True, "the row names the instant it started");

            MetricsStore.Instance.Add(Episode("s_test-0002", index: 2));
            Assert.That(session.Poll(), Is.True);
            Assert.That(Rows(list).Count, Is.EqualTo(2), "the newest episode is added, the list grows");
        }

        // -- the session ordinal on the label --------------------------------

        [Test]
        public void AnEpisodeIsLabelledByItsPlaceInTheSession()
        {
            Assert.That(AnalysisFormatting.EpisodeLabel(Episode("a", index: 1, scenario: "default")),
                Is.EqualTo("01_default"));
            Assert.That(AnalysisFormatting.EpisodeLabel(Episode("b", index: 12, scenario: "default")),
                Is.EqualTo("12_default"), "two digits is a format, not a ceiling");
            Assert.That(AnalysisFormatting.EpisodeLabel(Episode("c", index: 0, scenario: "default")),
                Is.EqualTo("default"),
                "an episode with no place to show keeps its scenario rather than pretending to be number zero");
            Assert.That(AnalysisFormatting.EpisodeLabel(Episode("d", index: 3, scenario: "")),
                Is.EqualTo("03_(no scenario)"));
        }

        // -- clearing the session --------------------------------------------

        [Test]
        public void ClearingTheSessionStartsFromAnEmptyList()
        {
            MetricsStore.Instance.Add(Episode("s_test-0001"));
            MetricsStore.Instance.Add(Episode("s_test-0002"));

            var list = new AnalysisEpisodeList();
            var session = new AnalysisSession();
            list.Bind(session);
            Assert.That(Rows(list).Count, Is.EqualTo(2), "the session starts with the episodes the store holds");
            Assert.That(session.Selected, Is.Not.Null);

            MetricsStore.Instance.Clear();
            Assert.That(session.Poll(), Is.True, "a cleared store is a change the session has to notice");

            Assert.That(session.Episodes, Is.Empty);
            Assert.That(session.Selected, Is.Null, "nothing is selected once the session is empty");
            Assert.That(Rows(list), Is.Empty, "the list is empty again, not left holding removed rows");
            Assert.That(list.Query<Label>(className: "analysis-empty-message").ToList(), Is.Not.Empty,
                "an emptied session says so instead of drawing an empty list");
        }

        // -- deleting one episode --------------------------------------------

        [Test]
        public void DeletingOneEpisodeLeavesTheOthers()
        {
            MetricsStore store = MetricsStore.Instance;
            store.Add(Episode("s_test-0001"));
            store.Add(Episode("s_test-0002"));
            store.Add(Episode("s_test-0003"));

            var list = new AnalysisEpisodeList();
            var session = new AnalysisSession();
            list.Bind(session);
            session.Select("s_test-0002");

            EpisodeMetrics removed = store.Remove("s_test-0002");
            Assert.That(removed, Is.Not.Null, "the removal reports the episode it dropped");
            Assert.That(removed.Id, Is.EqualTo("s_test-0002"));
            Assert.That(store.Remove("s_test-9999"), Is.Null, "an unknown id removes nothing");

            Assert.That(session.Poll(), Is.True);
            Assert.That(session.Episodes.Select(episode => episode.Id),
                Is.EqualTo(new[] { "s_test-0001", "s_test-0003" }),
                "the two episodes that were not dropped are still there, in order");
            Assert.That(session.Selected.Id, Is.EqualTo("s_test-0003"),
                "deleting the selected episode moves the selection instead of leaving it dangling");
            Assert.That(Rows(list).Count, Is.EqualTo(2));
        }

        // -- the confirmation dialog -----------------------------------------

        [Test]
        public void TheConfirmationDialogDoesNotActUntilConfirmed()
        {
            MetricsStore store = MetricsStore.Instance;
            store.Add(Episode("s_test-0001"));

            var dialog = new ConfirmationDialog(
                "Clear this session?",
                "This cannot be undone.",
                "Clear session",
                alternateText: "Export first");

            int confirmations = 0;
            int alternates = 0;
            dialog.Confirmed += () =>
            {
                confirmations++;
                store.Clear();
            };
            dialog.Alternate += () => alternates++;

            Assert.That(dialog.AlternateButton.text, Is.EqualTo("Export first"),
                "the safer alternative is offered by name");
            Assert.That(dialog.ConfirmButton.ClassListContains("destructive"), Is.True,
                "the destructive button is told apart from the others");
            Assert.That(dialog.CancelButton.ClassListContains("destructive"), Is.False);

            // Escape, a click on the backdrop and the cancel button all run this path.
            dialog.Dismiss();
            Assert.That(confirmations, Is.Zero, "closing the dialog does not confirm anything");
            Assert.That(store.Count, Is.EqualTo(1), "and leaves the store exactly as it was");
            Assert.That(dialog.parent, Is.Null, "a dismissed dialog leaves the tree");

            Assert.That(alternates, Is.Zero, "the alternative only runs when it is asked for");
            dialog.Accept();
            Assert.That(confirmations, Is.EqualTo(1), "confirming runs the destructive path once");
            Assert.That(store.Episodes, Is.Empty);
        }

        // -- an episode with no human does not lie about it -----------------

        [Test]
        public void AnEpisodeWithoutHumansDoesNotClaimADistance()
        {
            Assert.That(AnalysisFormatting.HumanDistance(EpisodeMetrics.NoHumanDistance),
                Is.EqualTo(AnalysisFormatting.NoHumans));
            Assert.That(AnalysisFormatting.HumanDistance(EpisodeMetrics.NoHumanDistance),
                Does.Not.Contain("0.00"), "the no-human sentinel is not a measurement of zero metres");

            MetricsStore.Instance.Add(Episode(
                "s_test-0001",
                minHuman: EpisodeMetrics.NoHumanDistance,
                meanHuman: EpisodeMetrics.NoHumanDistance));

            var detailHost = new VisualElement();
            var detail = new AnalysisEpisodeDetail(detailHost);
            detail.Bind(new AnalysisSession());

            Assert.That(MetricValue(detailHost, "Closest human"), Is.EqualTo(AnalysisFormatting.NoHumans));
            Assert.That(MetricValue(detailHost, "Mean nearest human"), Is.EqualTo(AnalysisFormatting.NoHumans));
            Assert.That(MetricValue(detailHost, "Closest human"), Does.Not.Contain("0.00"));

            // The session tile follows the same rule: no human in any episode is not a mean of zero.
            var summaryHost = new VisualElement();
            var summary = new AnalysisSessionSummary(summaryHost);
            AnalysisSession session = new AnalysisSession();
            summary.Show(session.Episodes, AnalysisSessionSummary.WholeSession);

            Assert.That(Tile(summaryHost, "Mean closest human"), Is.Not.Null,
                "the session overview carries a closest-human tile");
            Assert.That(TileValue(summaryHost, "Mean closest human"), Is.EqualTo(AnalysisFormatting.NoHumans));
        }

        // -- the episode panel is a reading, not a place for actions ---------

        [Test]
        public void TheEpisodeDetailNamesTheEpisodeAndCarriesNoActionOfItsOwn()
        {
            var host = new VisualElement();
            var detail = new AnalysisEpisodeDetail(host);
            detail.Bind(new AnalysisSession());

            Assert.That(host.Query<Button>().ToList(), Is.Empty,
                "export and delete live in the list row, not on the panel that describes a run");
            Assert.That(host.Query<VisualElement>(className: "analysis-empty-message").ToList(), Is.Not.Empty,
                "nothing selected says so");

            MetricsStore.Instance.Add(Episode("s_test-0001", index: 4, scenario: "default"));
            detail.Bind(new AnalysisSession());

            Assert.That(host.Query<Label>(className: "analysis-detail-subject").ToList()[0].text,
                Is.EqualTo("Episode 04_default"), "the panel names the run the way the list does");
            Assert.That(MetricValue(host, "Episode id"), Is.EqualTo("s_test-0001"),
                "the stable id is still readable, it is just not the label anymore");
        }

        // -- one colour per agent -------------------------------------------

        [Test]
        public void TheLegendGivesEveryAgentItsOwnColour()
        {
            string[] keys = { "robot_1", "human_1", "human_2", "human_3" };
            IReadOnlyList<AnalysisAgentPalette.TrackColour> tracks = AnalysisAgentPalette.Assign(keys, "robot_1");

            Assert.That(tracks.Count, Is.EqualTo(keys.Length), "every track of the episode is drawn");
            Assert.That(tracks.Select(track => track.Key), Is.Unique, "each agent is named once");
            Assert.That(Colours(tracks), Is.Unique, "each agent draws in its own colour");

            Assert.That(tracks[0].IsRobot, Is.True, "the robot is the first, distinguished entry");
            Assert.That(tracks[0].Colour, Is.EqualTo(AnalysisAgentPalette.RobotColour));
            Assert.That(tracks[0].Label, Is.EqualTo("Robot"));
            Assert.That(Colours(tracks).Skip(1), Does.Not.Contain(Colours(tracks)[0]),
                "no human is drawn in the robot's colour");

            // The colour of a human depends on the agent, not on the order a dictionary enumerated in.
            string[] shuffled = { "human_3", "robot_1", "human_1", "human_2" };
            Assert.That(Colours(AnalysisAgentPalette.Assign(shuffled, "robot_1")), Is.EqualTo(Colours(tracks)));
        }

        [Test]
        public void AMultiRobotFleetIsNotDrawnAsACrowd()
        {
            string[] keys = { "robot_1", "robot_2", "robot_3", "human_1" };
            var fleet = new List<string> { "robot_1", "robot_2", "robot_3" };
            IReadOnlyList<AnalysisAgentPalette.TrackColour> tracks =
                AnalysisAgentPalette.Assign(keys, "robot_1", fleet);

            Assert.That(tracks.Count, Is.EqualTo(keys.Length), "every robot and every human is drawn");
            Assert.That(Colours(tracks), Is.Unique, "no two agents share a colour, fleet or crowd");

            Assert.That(tracks[0].Key, Is.EqualTo("robot_1"), "the followed robot leads the legend");
            Assert.That(tracks[0].IsRobot, Is.True);
            Assert.That(tracks[0].Label, Is.EqualTo("Robot"));

            Assert.That(tracks[1].Key, Is.EqualTo("robot_2"));
            Assert.That(tracks[1].IsRobot, Is.True, "a second robot is a robot, not a crowd member");
            Assert.That(tracks[1].Label, Is.EqualTo("Robot 2"));

            Assert.That(tracks[2].Key, Is.EqualTo("robot_3"));
            Assert.That(tracks[2].IsRobot, Is.True);
            Assert.That(tracks[2].Label, Is.EqualTo("Robot 3"));

            Assert.That(tracks[3].Key, Is.EqualTo("human_1"), "the crowd comes after the fleet");
            Assert.That(tracks[3].IsRobot, Is.False);
            Assert.That(tracks[3].Label, Is.EqualTo("Human 1"));

            // The roster decides what a robot is even when the key alone could be read as a crowd member.
            Assert.That(AnalysisAgentPalette.IsRobotKey("robot_2", fleet), Is.True);
            Assert.That(AnalysisAgentPalette.IsRobotKey("human_1", fleet), Is.False);

            // Ordered by the recorder's roster, not by the order the dictionary enumerated in.
            string[] shuffled = { "human_1", "robot_3", "robot_2", "robot_1" };
            Assert.That(
                AnalysisAgentPalette.Assign(shuffled, "robot_1", fleet)
                    .Select(track => track.Key).ToList(),
                Is.EqualTo(new[] { "robot_1", "robot_2", "robot_3", "human_1" }));
        }

        [Test]
        public void APrefixDoesNotCountASecondRobotAsAHuman()
        {
            var episode = new EpisodeMetrics
            {
                Id = "s_test-0001",
                Robot = "robot_1",
                Robots = new List<string> { "robot_1", "robot_2" },
                WorldSeconds = 4.0,
                PersonalSpaceRadiusMetres = 0.5,
                Trajectories = new Dictionary<string, List<double[]>>
                {
                    ["robot_1"] = new List<double[]>
                    {
                        new[] { 0.0, 0.0, 0.0 },
                        new[] { 1.0, 1.0, 0.0 },
                        new[] { 2.0, 2.0, 0.0 },
                        new[] { 3.0, 3.0, 0.0 },
                        new[] { 4.0, 4.0, 0.0 },
                    },
                    // A second robot running right on top of the first: a person would read as a collision, a
                    // robot must not, because the recorder never measured a robot as a human.
                    ["robot_2"] = new List<double[]>
                    {
                        new[] { 0.0, 0.1, 0.0 },
                        new[] { 4.0, 4.1, 0.0 },
                    },
                },
            };

            AnalysisPrefixMetrics metrics = AnalysisTrackReader.Metrics(episode, 2.0);

            Assert.That(metrics.HasRobot, Is.True);
            Assert.That(metrics.MinHumanDistanceMetres, Is.EqualTo(EpisodeMetrics.NoHumanDistance),
                "with no crowd at all the prefix reports the no-human sentinel, not the second robot");
            Assert.That(metrics.AverageHumanDistanceMetres, Is.EqualTo(EpisodeMetrics.NoHumanDistance));
            Assert.That(metrics.MinClearanceMetres, Is.EqualTo(EpisodeMetrics.NoHumanDistance),
                "and neither is there a clearance to report");
            Assert.That(metrics.PersonalSpaceIntrusions, Is.Zero,
                "a robot passing through another robot's space is not a personal-space intrusion");
        }

        /// <summary>
        /// The frise has to read the same passage the recorder did: the same footprints, the same clearance.
        ///
        /// The trajectory file carries poses and not radii, so the prefix borrows the pair the episode
        /// recorded. Reading it as a bare centre distance would make the numbers beside the cursor disagree
        /// with the numbers the run was scored on - two views of one episode, saying two different things.
        /// </summary>
        [Test]
        public void APrefixMeasuresTheSameClearanceTheRecorderDid()
        {
            var episode = new EpisodeMetrics
            {
                Id = "s_test-0001",
                Robot = "robot_1",
                WorldSeconds = 4.0,
                PersonalSpaceRadiusMetres = 0.5,
                RobotRadiusMetres = 0.3,
                HumanRadiusMetres = 0.3,
                Trajectories = new Dictionary<string, List<double[]>>
                {
                    ["robot_1"] = new List<double[]>
                    {
                        new[] { 0.0, 0.0, 0.0 },
                        new[] { 4.0, 2.0, 0.0 },
                    },
                    ["human_1"] = new List<double[]>
                    {
                        new[] { 0.0, 1.0, 0.0 },
                        new[] { 4.0, 1.0, 0.0 },
                    },
                },
            };

            AnalysisPrefixMetrics metrics = AnalysisTrackReader.Metrics(episode, 4.0);

            Assert.That(metrics.MinHumanDistanceMetres, Is.EqualTo(1.0).Within(1e-6),
                "the human distance stays centre to centre, as the recorder wrote it");
            Assert.That(metrics.MinClearanceMetres, Is.EqualTo(0.4).Within(1e-6),
                "the clearance takes both footprints off, as the recorder did");
            Assert.That(metrics.PersonalSpaceIntrusions, Is.EqualTo(1),
                "0.4 m of air is inside a 0.5 m personal space");

            Assert.That(AnalysisFormatting.Clearance(-0.2), Is.EqualTo("-0.20 m"),
                "bodies that overlap read as a negative gap, not as no measurement");
            Assert.That(AnalysisFormatting.Clearance(EpisodeMetrics.NoHumanDistance),
                Is.EqualTo(AnalysisFormatting.NoHumans));
        }

        // -- the session follows the store ----------------------------------

        [Test]
        public void TheSessionKeepsTheSelectedEpisodeWhileNewOnesArrive()
        {
            MetricsStore.Instance.Add(Episode("s_test-0001"));
            MetricsStore.Instance.Add(Episode("s_test-0002"));
            var session = new AnalysisSession();

            Assert.That(session.Selected.Id, Is.EqualTo("s_test-0002"), "the newest episode is selected first");

            session.Select("s_test-0001");
            Assert.That(session.Selected.Id, Is.EqualTo("s_test-0001"));

            MetricsStore.Instance.Add(Episode("s_test-0003"));
            Assert.That(session.Poll(), Is.True);
            Assert.That(session.Selected.Id, Is.EqualTo("s_test-0001"),
                "an episode ending must not pull the reader off the one being examined");

            MetricsStore.Instance.Clear();
            Assert.That(session.Poll(), Is.True, "clearing the store starts a new, empty session");
            Assert.That(session.Selected, Is.Null);
            Assert.That(session.Episodes, Is.Empty);
        }

        // -- which panel a selection asks for --------------------------------

        [Test]
        public void OneCheckedEpisodeIsADetailAndAnyOtherCountIsAnOverview()
        {
            Assert.That(AnalysisTabController.ShowsEpisodeDetail(1), Is.True,
                "one checked episode shows that run beside its own trajectory");
            Assert.That(AnalysisTabController.ShowsEpisodeDetail(0), Is.False,
                "nothing checked is the overview of the whole session");
            Assert.That(AnalysisTabController.ShowsEpisodeDetail(2), Is.False,
                "several checked is the overview of those several");
            Assert.That(AnalysisTabController.ShowsEpisodeDetail(9), Is.False);
        }

        // -- the checked set feeds the overview ------------------------------

        [Test]
        public void CheckingSeveralEpisodesScopesTheOverviewToThem()
        {
            MetricsStore store = MetricsStore.Instance;
            store.Add(Episode("s_test-0001", MetricsContract.OutcomeGoal, index: 1));
            store.Add(Episode("s_test-0002", MetricsContract.OutcomeGoal, index: 2));
            store.Add(Episode("s_test-0003", MetricsContract.OutcomeCollision, index: 3));

            AnalysisEpisodeList list = BoundList(out _);

            list.SetChecked("s_test-0001", true);
            list.SetChecked("s_test-0002", true);

            Assert.That(list.CheckedEpisodes.Select(episode => episode.Id),
                Is.EqualTo(new[] { "s_test-0001", "s_test-0002" }),
                "the checked set is what the overview is asked about");

            var host = new VisualElement();
            var summary = new AnalysisSessionSummary(host);
            IReadOnlyList<EpisodeMetrics> checkedEpisodes = list.CheckedEpisodes;
            summary.Show(checkedEpisodes, AnalysisSessionSummary.SelectionScope(checkedEpisodes.Count));

            Assert.That(TileValue(host, "Episodes"), Is.EqualTo("2"),
                "the overview counts the checked episodes, not the session");
            Assert.That(TileValue(host, "Success rate"), Is.EqualTo("100 %"),
                "the rate is over the checked episodes too");
            Assert.That(Captions(host, "analysis-section-subtitle")[0],
                Does.Contain("the 2 selected episodes"),
                "the overview says what it averaged");
        }

        [Test]
        public void TheOverviewCountsOnlyTheEpisodesItWasHanded()
        {
            MetricsStore store = MetricsStore.Instance;
            store.Add(Episode("s_test-0001", MetricsContract.OutcomeGoal, index: 1, intrusions: 3));
            store.Add(Episode("s_test-0002", MetricsContract.OutcomeCollision, index: 2, intrusions: 1));
            store.Add(Episode("s_test-0003", MetricsContract.OutcomeCollision, index: 3, intrusions: 0));

            var host = new VisualElement();
            var summary = new AnalysisSessionSummary(host);
            summary.Show(new List<EpisodeMetrics> { store.Get("s_test-0002"), store.Get("s_test-0003") },
                AnalysisSessionSummary.SelectionScope(2));

            Assert.That(TileValue(host, "Episodes"), Is.EqualTo("2"));
            Assert.That(TileValue(host, "Success rate"), Is.EqualTo("0 %"), "neither of the two reached the goal");
            Assert.That(TileValue(host, "Personal space intrusions"), Is.EqualTo("1"),
                "the intrusion count is summed over the two episodes, not over the session");
            Assert.That(Captions(host, "analysis-distribution-name"),
                Is.EqualTo(new[] { "Collision" }),
                "the mix lists only the outcomes the handed episodes ended as");
            Assert.That(Captions(host, "analysis-distribution-count"), Is.EqualTo(new[] { "2" }));

            // The overview of the whole session is a different reading, and says so.
            summary.Show(store.Episodes, AnalysisSessionSummary.WholeSession);
            Assert.That(TileValue(host, "Episodes"), Is.EqualTo("3"));
            Assert.That(TileValue(host, "Success rate"), Is.EqualTo("33.3 %"));
            Assert.That(Captions(host, "analysis-section-subtitle")[0],
                Does.Contain("every episode the session ran"));
        }

        // -- the filters ------------------------------------------------------

        [Test]
        public void SelectAllChecksTheRowsTheFiltersShowAndOnlyThose()
        {
            MetricsStore store = MetricsStore.Instance;
            store.Add(Episode("s_test-0001", MetricsContract.OutcomeGoal, index: 1, scenario: "corridor"));
            store.Add(Episode("s_test-0002", MetricsContract.OutcomeGoal, index: 2, scenario: "corridor"));
            store.Add(Episode("s_test-0003", MetricsContract.OutcomeCollision, index: 3, scenario: "intersection"));

            AnalysisEpisodeList list = BoundList(out _);

            list.SetOutcomeFilter(MetricsContract.OutcomeGoal);
            list.SelectAllVisible();
            Assert.That(list.CheckedEpisodes.Select(episode => episode.Id),
                Is.EqualTo(new[] { "s_test-0001", "s_test-0002" }),
                "select all takes the rows the filter shows, never the whole session");

            // The rows the new filter hides leave the view, so they leave the selection with it.
            list.SetOutcomeFilter(MetricsContract.OutcomeCollision);
            Assert.That(list.CheckedEpisodes, Is.Empty,
                "a line a filter hid must not stay counted in the selection");
            Assert.That(list.VisibleIds, Is.EqualTo(new[] { "s_test-0003" }));

            list.SelectAllVisible();
            Assert.That(list.CheckedEpisodes.Select(episode => episode.Id),
                Is.EqualTo(new[] { "s_test-0003" }));

            list.ClearSelection();
            Assert.That(list.CheckedEpisodes, Is.Empty);
        }

        [Test]
        public void TheScenarioSearchAndIntrusionFiltersNarrowTheList()
        {
            MetricsStore store = MetricsStore.Instance;
            store.Add(Episode("s_test-0001", MetricsContract.OutcomeGoal, index: 1,
                scenario: "corridor", intrusions: 0));
            store.Add(Episode("s_test-0002", MetricsContract.OutcomeGoal, index: 2,
                scenario: "intersection", intrusions: 4));

            AnalysisEpisodeList list = BoundList(out AnalysisSession session);

            // The scenario criterion is only offered when the session ran more than one scenario.
            Assert.That(list.Query<DropdownField>(className: "analysis-filter-second").ToList()[0].style.display.value,
                Is.EqualTo(DisplayStyle.Flex));

            list.SetScenarioFilter("intersection");
            Assert.That(list.VisibleIds, Is.EqualTo(new[] { "s_test-0002" }));

            list.SetScenarioFilter(AnalysisEpisodeFilter.Any);
            Assert.That(list.VisibleIds.Count, Is.EqualTo(2));

            list.SetSearch("02");
            Assert.That(list.VisibleIds, Is.EqualTo(new[] { "s_test-0002" }),
                "the search reads the label the row shows");

            list.SetSearch(string.Empty);
            list.SetIntrusionsOnly(true);
            Assert.That(list.VisibleIds, Is.EqualTo(new[] { "s_test-0002" }),
                "only the episode whose robot entered a personal space is left");

            list.SetIntrusionsOnly(false);
            Assert.That(session.Episodes.Count, Is.EqualTo(2), "the session itself is untouched by filtering");
        }

        [Test]
        public void ASessionWithOneScenarioDoesNotOfferTheScenarioFilter()
        {
            MetricsStore.Instance.Add(Episode("s_test-0001", index: 1, scenario: "corridor"));
            MetricsStore.Instance.Add(Episode("s_test-0002", index: 2, scenario: "corridor"));

            AnalysisEpisodeList list = BoundList(out _);
            DropdownField scenarioFilter = list.Query<DropdownField>(className: "analysis-filter-second").ToList()[0];

            Assert.That(scenarioFilter.style.display.value, Is.EqualTo(DisplayStyle.None),
                "a criterion whose only answer is 'all of them' is not worth a control");
        }

        // -- a row's glyphs are not a row click -------------------------------

        [Test]
        public void TheRowGlyphsDoNotSelectTheRow()
        {
            MetricsStore.Instance.Add(Episode("s_test-0001", index: 1));
            AnalysisEpisodeList list = BoundList(out _);

            VisualElement row = RowOf(list, "s_test-0001");
            Assert.That(row, Is.Not.Null);

            Button export = row.Query<Button>(className: "analysis-row-icon-export").ToList()[0];
            Button delete = row.Query<Button>(className: "analysis-row-icon-delete").ToList()[0];
            Toggle check = row.Query<Toggle>(className: "analysis-row-check").ToList()[0];
            Label label = row.Query<Label>(className: "analysis-row-label").ToList()[0];

            Assert.That(export.tooltip, Is.Not.Empty, "an unlabelled glyph says what it does on hover");
            Assert.That(delete.tooltip, Is.Not.Empty);
            Assert.That(AnalysisEpisodeList.IsRowAction(export), Is.True);
            Assert.That(AnalysisEpisodeList.IsRowAction(delete), Is.True);
            Assert.That(AnalysisEpisodeList.IsRowAction(check), Is.True,
                "the checkbox is the row's own action, not a way to select it");
            Assert.That(AnalysisEpisodeList.IsRowAction(label), Is.False);
            Assert.That(AnalysisEpisodeList.IsRowAction(row), Is.False);

            string selected = null;
            list.EpisodeSelected += id => selected = id;

            list.ClickRow(row, export);
            list.ClickRow(row, delete);
            list.ClickRow(row, check);
            Assert.That(selected, Is.Null, "a click on a glyph or a checkbox is not a selection");
            Assert.That(list.CheckedEpisodes, Is.Empty);

            list.ClickRow(row, label);
            Assert.That(selected, Is.EqualTo("s_test-0001"), "a click on the row itself selects that run");
            Assert.That(list.CheckedEpisodes.Select(episode => episode.Id), Is.EqualTo(new[] { "s_test-0001" }));
        }

        [Test]
        public void TheRowCarriesBothGlyphsAndACheckbox()
        {
            MetricsStore.Instance.Add(Episode("s_test-0001", index: 1));
            MetricsStore.Instance.Add(Episode("s_test-0002", index: 2));
            AnalysisEpisodeList list = BoundList(out _);

            Assert.That(Rows(list).Count, Is.EqualTo(2));
            foreach (VisualElement row in Rows(list))
            {
                Assert.That(row.Query<Button>(className: "analysis-row-icon-export").ToList().Count,
                    Is.EqualTo(1), "every row carries its own export glyph");
                Assert.That(row.Query<Button>(className: "analysis-row-icon-delete").ToList().Count,
                    Is.EqualTo(1), "and its own delete glyph");
                Assert.That(row.Query<Toggle>(className: "analysis-row-check").ToList().Count, Is.EqualTo(1));
            }

            // The glyphs live inside the row, not in a panel that describes one run.
            Assert.That(list.Query<VisualElement>(className: "analysis-row-actions").ToList().Count, Is.EqualTo(2));
        }

        // -- the toolbar ------------------------------------------------------

        [Test]
        public void TheToolbarOffersSelectAllAndClearSelection()
        {
            MetricsStore.Instance.Add(Episode("s_test-0001", index: 1));
            AnalysisEpisodeList list = BoundList(out _);

            List<string> tools = list.Query<Button>(className: "analysis-list-tool")
                .ToList().Select(button => button.text).ToList();
            Assert.That(tools, Is.EqualTo(new[] { "Select all", "Clear selection" }));

            Button clear = list.Query<Button>(className: "analysis-list-tool").ToList()[1];
            Assert.That(clear.enabledSelf, Is.False, "there is nothing to clear before anything is checked");

            list.SelectAllVisible();
            Assert.That(clear.enabledSelf, Is.True);
            Assert.That(list.CheckedEpisodes.Count, Is.EqualTo(1));

            // The reader sees a checked box, not only a set the reader cannot see.
            Assert.That(RowOf(list, "s_test-0001").Query<Toggle>(className: "analysis-row-check").ToList()[0].value,
                Is.True, "'Select all' checks the boxes it selected");

            list.ClearSelection();
            Assert.That(RowOf(list, "s_test-0001").Query<Toggle>(className: "analysis-row-check").ToList()[0].value,
                Is.False, "'Clear selection' unchecks them again");
        }

        // -- the replay cursor -------------------------------------------------

        [Test]
        public void TheCursorReadsTheEpisodeUpToTheInstantItStandsOn()
        {
            var replay = new AnalysisEpisodeReplay();
            replay.Show(ReplayEpisode());

            Assert.That(replay.Time, Is.EqualTo(4.0).Within(1e-6),
                "an episode opens on its complete trajectory, which is what the tab always showed");
            Assert.That(replay.IsScrubbed, Is.False, "and a cursor resting on the end is not a prefix");

            replay.Seek(2.0);
            Assert.That(replay.IsScrubbed, Is.True);

            Assert.That(replay.Track("robot_1").Count, Is.EqualTo(3), "the robot's line stops at the cursor");
            Assert.That(replay.Track("human_1").Count, Is.EqualTo(1),
                "every agent's line stops there, not only the robot's");

            AnalysisPrefixMetrics metrics = replay.Metrics();
            Assert.That(metrics.HasRobot, Is.True);
            Assert.That(metrics.Seconds, Is.EqualTo(2.0).Within(1e-6));
            Assert.That(metrics.PathLengthMetres, Is.EqualTo(2.0).Within(1e-6), "the path is the prefix of the path");
            Assert.That(metrics.AverageSpeedMetresPerSecond, Is.EqualTo(1.0).Within(1e-6));
            Assert.That(metrics.MaxSpeedMetresPerSecond, Is.EqualTo(1.0).Within(1e-6));
            Assert.That(metrics.MinHumanDistanceMetres, Is.EqualTo(Math.Sqrt(4.16)).Within(1e-3),
                "the human is still far away at this instant");
            Assert.That(metrics.PersonalSpaceIntrusions, Is.Zero, "the personal space has not been entered yet");

            // The cut lands between two samples, not only on them.
            replay.Seek(1.5);
            Assert.That(replay.Track("robot_1").Count, Is.EqualTo(3));
            Assert.That(replay.Track("robot_1")[2].x, Is.EqualTo(1.5f).Within(1e-4f),
                "the line is drawn as far as the robot had got, not as far as it was last sampled");
        }

        [Test]
        public void ScrubbingTheDetailReadsThePrefixAndKeepsTheFinalValue()
        {
            MetricsStore.Instance.Add(ReplayEpisode());

            var host = new VisualElement();
            var detail = new AnalysisEpisodeDetail(host);
            var replay = new AnalysisEpisodeReplay();
            detail.SetReplay(replay);
            var session = new AnalysisSession();
            detail.Bind(session);
            replay.Show(session.Selected);

            Assert.That(MetricValue(host, "Path length"), Is.EqualTo("4.00 m"),
                "with the cursor at the end the panel reads the recorded values");
            Assert.That(host.Query<Label>(className: "analysis-metric-final").ToList(), Is.Empty,
                "there is nothing to compare while the whole run is on screen");

            replay.Seek(2.0);

            Assert.That(MetricValue(host, "Path length"), Is.EqualTo("2.00 m"));
            Assert.That(MetricFinal(host, "Path length"), Is.EqualTo("final 4.00 m"),
                "the value the run ended on stays beside the prefix");
            Assert.That(MetricValue(host, "World duration"), Is.EqualTo("2.00 s"));
            Assert.That(MetricFinal(host, "World duration"), Is.EqualTo("final 4.00 s"));
            Assert.That(MetricValue(host, "Personal space intrusions"), Is.EqualTo("0"));
            Assert.That(MetricFinal(host, "Personal space intrusions"), Is.EqualTo("final 1"));
            Assert.That(MetricValue(host, "Closest human"), Is.EqualTo("2.04 m"));
            Assert.That(MetricFinal(host, "Closest human"), Is.EqualTo("final 0.40 m"),
                "the closest approach is still ahead of the cursor");
            Assert.That(MetricValue(host, "Episode id"), Is.EqualTo("s_test-0001"),
                "the identity of a run is not something a prefix shortens");
            Assert.That(MetricFinal(host, "Episode id"), Is.Null);
        }

        [Test]
        public void PlaybackAdvancesInRealSecondsAndStopsAtTheEnd()
        {
            var replay = new AnalysisEpisodeReplay();
            replay.Show(ReplayEpisode());
            Assert.That(replay.AtEnd, Is.True);

            replay.Play();
            Assert.That(replay.IsPlaying, Is.True);
            Assert.That(replay.Time, Is.Zero, "playing from the end replays the episode from its beginning");

            replay.Advance(1.5);
            Assert.That(replay.Time, Is.EqualTo(1.5).Within(1e-6),
                "a second of playback is a world second of the episode, whatever the time scale says");
            Assert.That(replay.IsPlaying, Is.True);

            replay.Advance(10.0);
            Assert.That(replay.Time, Is.EqualTo(4.0).Within(1e-6), "the cursor stops at the end of the episode");
            Assert.That(replay.IsPlaying, Is.False, "and the transport is back to play");

            replay.Rewind();
            Assert.That(replay.AtStart, Is.True);
            for (int step = 0; step < 5; step++)
                replay.Step();
            Assert.That(replay.Time, Is.EqualTo(4.0).Within(1e-6), "a step never walks past the end");

            // The step has a left twin: one second back, and it never walks before the start either.
            replay.Seek(2.0);
            replay.StepBack();
            Assert.That(replay.Time, Is.EqualTo(1.0).Within(1e-6), "a step back is a second of the episode");
            replay.Rewind();
            replay.StepBack();
            Assert.That(replay.AtStart, Is.True, "a step back stops at the beginning");
        }

        [Test]
        public void TheFriseSpansTheEpisodeAndReportsWhatTheReaderAsksFor()
        {
            var timeline = new AnalysisTimeline();
            var replay = new AnalysisEpisodeReplay();
            timeline.Bind(replay);
            replay.Show(ReplayEpisode());

            Slider slider = timeline.Q<Slider>(className: "analysis-timeline-slider");
            Assert.That(slider, Is.Not.Null, "the detail view carries a frise");
            Assert.That(slider.lowValue, Is.EqualTo(0f));
            Assert.That(slider.highValue, Is.EqualTo(4f).Within(1e-4f),
                "the frise runs from the beginning to the end of the episode");
            Assert.That(slider.value, Is.EqualTo(4f).Within(1e-4f));
            Assert.That(timeline.Query<Label>(className: "analysis-timeline-clock").ToList()[0].text,
                Is.EqualTo("00:04 / 00:04"), "the instant reads in clear next to the duration");

            double sought = -1.0;
            timeline.SeekRequested += seconds => sought = seconds;
            timeline.ScrubTo(1.0);
            Assert.That(sought, Is.EqualTo(1.0).Within(1e-4), "moving the frise asks for that instant");

            // And the other way round: when the cursor moves, the frise follows it.
            replay.Seek(1.0);
            Assert.That(slider.value, Is.EqualTo(1.0f).Within(1e-4f),
                "the frise stands where the cursor stands, whoever moved it");

            List<string> transport = timeline.Query<Button>(className: "analysis-timeline-button")
                .ToList().Select(button => button.tooltip).ToList();
            Assert.That(transport.Count, Is.EqualTo(4),
                "back to the beginning, play, one second back and one second forward");
            Assert.That(transport, Has.None.Empty, "an unlabelled control says what it does on hover");
            Assert.That(timeline.Query<Button>(className: "analysis-timeline-step").ToList()[0].text,
                Is.EqualTo("+1 s"));
            Assert.That(timeline.Query<Button>(className: "analysis-timeline-step-back").ToList()[0].text,
                Is.EqualTo("-1 s"));

            Assert.That(AnalysisTimeline.Clock(0.0), Is.EqualTo("00:00"));
            Assert.That(AnalysisTimeline.Clock(8.4), Is.EqualTo("00:08"));
            Assert.That(AnalysisTimeline.Clock(125.0), Is.EqualTo("02:05"));
        }

        [Test]
        public void TheTrajectoryMapGoesAwayWhenSeveralEpisodesAreChecked()
        {
            Assert.That(AnalysisTabController.ShowsTrajectoryMap(0), Is.False,
                "nothing checked opens on the session overview alone, with no trajectory beside it");
            Assert.That(AnalysisTabController.ShowsTrajectoryMap(1), Is.True,
                "one checked is that run beside its own trajectory");
            Assert.That(AnalysisTabController.ShowsTrajectoryMap(2), Is.False,
                "several checked has no one trajectory to draw and takes the whole band");
            Assert.That(AnalysisTabController.ShowsTrajectoryMap(9), Is.False);
        }

        // -- the map key names the agents, and nothing else --------------------

        /// <summary>
        /// The map resolves its parts by name, so a test that wants the key only has to lay those names out.
        /// The map of a scenario is loaded through the scenario service, which is absent here: the key is
        /// filled from the episode's own tracks either way, which is what this pins.
        /// </summary>
        private static VisualElement MapHost()
        {
            var root = new VisualElement();
            root.Add(new VisualElement { name = "AnalysisMapCanvas" });
            root.Add(new Image { name = "AnalysisMapImage" });
            root.Add(new Label { name = "AnalysisMapPlaceholder" });
            root.Add(new Label { name = "AnalysisMapCaption" });
            root.Add(new Label { name = "AnalysisMapOutcome" });
            root.Add(new VisualElement { name = "AnalysisMapLegendList" });
            root.Add(new VisualElement { name = "AnalysisMapOverlayHost" });
            return root;
        }

        [Test]
        public void TheKeyNamesEachAgentAndCarriesNoPointCount()
        {
            VisualElement root = MapHost();
            var map = new AnalysisEpisodeMap(root);

            map.Show(Episode("s_test-0001"));

            List<string> names = root.Query<Label>(className: "analysis-legend-name").ToList()
                .Select(label => label.text).ToList();
            Assert.That(names, Is.EqualTo(new[] { "Robot", "Human 1", "Human 2" }),
                "the key names the agents the palette drew, in the palette's order");
            Assert.That(names, Has.None.Contains("pts"),
                "how many samples a run kept is not part of who the line belongs to");
            Assert.That(names, Has.None.Contains("-"),
                "a name is a name, not a name and a number glued together");

            map.Dispose();
        }

        // -- the confirmation dialog closes itself -----------------------------

        [Test]
        public void ConfirmingClosesTheDialogOnItsOwn()
        {
            var host = new VisualElement();
            var dialog = new ConfirmationDialog("Delete this episode?", "This cannot be undone.", "Delete episode");
            dialog.Show(host);
            Assert.That(dialog.parent, Is.EqualTo(host));

            dialog.Accept();
            Assert.That(dialog.parent, Is.Null, "the popup closes itself once the reader has confirmed");

            int confirmations = 0;
            int cancellations = 0;
            var second = new ConfirmationDialog("Clear this session?", "This cannot be undone.", "Clear session");
            second.Confirmed += () => confirmations++;
            second.Cancelled += () => cancellations++;
            second.Show(host);
            second.Dismiss();

            Assert.That(confirmations, Is.Zero);
            Assert.That(cancellations, Is.EqualTo(1), "closing without acting is not a confirmation");
            Assert.That(second.parent, Is.Null);
        }

        [Test]
        public void AFinishedExportClosesTheDialogAndAFailedOneStays()
        {
            (string status, bool close) =
                AnalysisTabController.ExportOutcome(new MetricsExportReport("/tmp/out", null, null, null), null);
            Assert.That(close, Is.True, "a finished export closes the popup that asked for it");
            Assert.That(status, Is.EqualTo("Exported to /tmp/out"));

            (status, close) = AnalysisTabController.ExportOutcome(default, "disk is full");
            Assert.That(close, Is.False, "a failed export is the one outcome the reader has to act on");
            Assert.That(status, Is.EqualTo("Export failed: disk is full"));
        }
    }
}
