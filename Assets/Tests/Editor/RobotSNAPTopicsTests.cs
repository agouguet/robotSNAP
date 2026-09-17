using NUnit.Framework;
using RobotSNAP.ROS;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// Guards the one table the Unity side names its streams with, the mirror of
    /// <c>robotSNAP_ws/tests/robotsnap/test_topics.py</c>: the ten names of the contract, and the join that
    /// turns a name and an environment prefix into the topic a client sees.
    /// </summary>
    public sealed class RobotSNAPTopicsTests
    {
        [Test]
        public void TheSurfaceIsExactlyTheContract()
        {
            Assert.That(
                RobotSNAPTopics.All,
                Is.EqualTo(
                    new[]
                    {
                        "/clock",
                        "/odom",
                        "/scan",
                        "/map",
                        "/cmd_vel",
                        "/simulation/state",
                        "/simulation/agents",
                        "/simulation/control",
                        "/simulation/control_result",
                        "/reset_done",
                    }
                )
            );
        }

        [Test]
        public void EveryTopicIsDistinct()
        {
            Assert.That(RobotSNAPTopics.All, Is.Unique);
        }

        [Test]
        public void WithoutAPrefixTheNameIsTheTopic()
        {
            Assert.That(RobotSNAPTopics.Full("/scan", ""), Is.EqualTo("/scan"));
            Assert.That(RobotSNAPTopics.Full("scan", null), Is.EqualTo("/scan"));
            Assert.That(RobotSNAPTopics.Full("/scan/", ""), Is.EqualTo("/scan"));
        }

        [Test]
        public void APrefixIsJoinedWithASeparator()
        {
            // The bug this rule exists for: gluing a prefix onto a name used to give `/envscan`, which no
            // client could guess.
            Assert.That(RobotSNAPTopics.Full("/scan", "env"), Is.EqualTo("/env/scan"));
            Assert.That(RobotSNAPTopics.Full("scan", "/env/"), Is.EqualTo("/env/scan"));
            Assert.That(
                RobotSNAPTopics.Full(RobotSNAPTopics.SimulationState, "env"),
                Is.EqualTo("/env/simulation/state")
            );
        }

        [Test]
        public void AnAlreadyPrefixedNameSurvivesASecondJoin()
        {
            // Builders hand a finished name to EnvROS, which prefixes it again: the join has to be idempotent
            // or a prefixed run would end up on `/env/env/scan`.
            string once = RobotSNAPTopics.Full("/odom", "env");
            Assert.That(RobotSNAPTopics.Full(once, "env"), Is.EqualTo(once));
        }

        [Test]
        public void ABlankTopicHasNoName()
        {
            Assert.That(RobotSNAPTopics.Full(null, "env"), Is.Null);
            Assert.That(RobotSNAPTopics.Full("", "env"), Is.Null);
            Assert.That(RobotSNAPTopics.Full("  ", "env"), Is.Null);
        }

        [Test]
        public void NormalizeTrimsTheSurroundingSlashes()
        {
            Assert.That(RobotSNAPTopics.Normalize("/simulation/agents"), Is.EqualTo("simulation/agents"));
            Assert.That(RobotSNAPTopics.Normalize("/env/"), Is.EqualTo("env"));
            Assert.That(RobotSNAPTopics.Normalize(null), Is.EqualTo(string.Empty));
        }
    }
}
