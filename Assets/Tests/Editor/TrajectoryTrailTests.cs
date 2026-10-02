using System.Collections.Generic;
using NUnit.Framework;
using RobotSNAP.Metrics;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// The window a moving replay draws.
    ///
    /// While the cursor is inside an episode the map shows the trail each agent left over the last few
    /// seconds, and at the end of the frise it shows the whole trajectories. The rule that makes the trail a
    /// window onto a path - rather than a segment that restarts at every sample boundary as the window slides
    /// past it - is the arithmetic pinned here, which is a pure function of the samples and needs neither a
    /// scene nor a panel.
    /// </summary>
    public sealed class TrajectoryTrailTests
    {
        /// <summary>An agent walking one metre per second along +X for <paramref name="seconds"/> seconds.</summary>
        private static List<double[]> Walker(int seconds)
        {
            var samples = new List<double[]>();
            for (int second = 0; second <= seconds; second++)
                samples.Add(new[] { (double)second, (double)second, 0.0 });

            return samples;
        }

        [Test]
        public void TheWindowKeepsTheSamplesInsideIt()
        {
            List<Vector2> trail = AnalysisTrackReader.PointsBetween(Walker(10), 6.0, 8.0);

            Assert.That(trail.Count, Is.EqualTo(3), "the samples at 6, 7 and 8 seconds and nothing else");
            Assert.That(trail[0].x, Is.EqualTo(6f).Within(1e-4f));
            Assert.That(trail[trail.Count - 1].x, Is.EqualTo(8f).Within(1e-4f));
        }

        [Test]
        public void TheWindowIsCutAtBothEdgesBetweenSamples()
        {
            List<Vector2> trail = AnalysisTrackReader.PointsBetween(Walker(10), 6.5, 8.5);

            Assert.That(trail[0].x, Is.EqualTo(6.5f).Within(1e-4f),
                "the trail starts where the agent stood at the left edge, not at the next sample");
            Assert.That(trail[trail.Count - 1].x, Is.EqualTo(8.5f).Within(1e-4f),
                "and it reaches where the agent stands now, not where it was last sampled");
            Assert.That(trail.Count, Is.EqualTo(4), "the two edges plus the samples at 7 and 8");
        }

        [Test]
        public void AnAgentThatHasNotStartedLeavesNothingToDraw()
        {
            Assert.That(AnalysisTrackReader.PointsBetween(Walker(10), -5.0, -1.0), Is.Empty,
                "a window before the first sample is not a line starting where the agent eventually went");
        }

        [Test]
        public void AnAgentThatHasStoppedIsOnePointAndNotALineToNowhere()
        {
            List<Vector2> trail = AnalysisTrackReader.PointsBetween(Walker(5), 7.0, 9.0);

            Assert.That(trail.Count, Is.EqualTo(1));
            Assert.That(trail[0].x, Is.EqualTo(5f).Within(1e-4f), "it stands where it stopped");
        }

        [Test]
        public void TheReplayOffersTheWindowWhileTheMapUsesTheWholePathAtTheEnd()
        {
            var episode = new EpisodeMetrics
            {
                Id = "s_test-0001",
                Robot = "robot_1",
                WorldSeconds = 10.0,
                Trajectories = new Dictionary<string, List<double[]>> { ["robot_1"] = Walker(10) },
            };

            var replay = new AnalysisEpisodeReplay();
            replay.Show(episode);

            Assert.That(replay.AtEnd, Is.True, "an episode opens on its complete trajectory");
            Assert.That(replay.Track("robot_1").Count, Is.EqualTo(11),
                "at the end the map draws the whole path, which is what a finished run asks for");

            replay.Seek(8.0);

            Assert.That(replay.Track("robot_1").Count, Is.EqualTo(9),
                "the complete prefix is still available while the cursor is inside the episode");
            Assert.That(replay.Trail("robot_1").Count, Is.EqualTo(6),
                "and the trail is the last five seconds of it: 3, 4, 5, 6, 7 and 8");
            Assert.That(replay.Trail("robot_1")[0].x, Is.EqualTo(3f).Within(1e-4f),
                "a window that slides with the cursor rather than a line growing from the start");

            Assert.That(replay.Trail("nobody_here"), Is.Empty, "an unknown agent is an empty trail, not an error");
        }
    }
}
