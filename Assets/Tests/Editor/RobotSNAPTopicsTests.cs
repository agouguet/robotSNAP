using NUnit.Framework;
using RobotSNAP.Agents;
using RobotSNAP.ROS;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// Guards the one table the Unity side names its streams with, the mirror of
    /// <c>robotSNAP_ws/tests/robotsnap/test_topics.py</c>: the ten names of the contract, and the join that
    /// turns a name and an environment prefix into the topic a client sees.
    /// </summary>
    public sealed class RobotSNAPTopicsTests
    {
        /// <summary>
        /// The table is process-wide, so a test that renames a stream has to put the shipped names back: a
        /// name left behind would be read by whichever test runs next.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            RobotSNAPTopics.ForgetOverrides();
        }

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

        [Test]
        public void AConfigurationRenamesEveryStreamOfTheContract()
        {
            RobotSNAPTopics.Use(new RosTopicNames { CmdVel = "/drive", Scan = "/lidar" });

            Assert.That(RobotSNAPTopics.CmdVel, Is.EqualTo("/drive"));
            Assert.That(RobotSNAPTopics.Scan, Is.EqualTo("/lidar"));
            Assert.That(
                RobotSNAPTopics.All,
                Is.EqualTo(
                    new[]
                    {
                        "/clock",
                        "/odom",
                        "/lidar",
                        "/map",
                        "/drive",
                        "/simulation/state",
                        "/simulation/agents",
                        "/simulation/control",
                        "/simulation/control_result",
                        "/reset_done",
                    }
                ),
                "the page shows the table in force, not the one the application shipped with");

            RobotSNAPTopics.ForgetOverrides();
            Assert.That(RobotSNAPTopics.CmdVel, Is.EqualTo("/cmd_vel"));
        }

        [Test]
        public void ARenamedStreamIsTheOneEveryRobotOfTheFleetAnswersOn()
        {
            // The point of one relative table: naming the command stream renames it for the whole fleet at
            // once, because each robot joins its own id onto whatever the table holds.
            RobotSNAPTopics.Use(new RosTopicNames { CmdVel = "/drive" });

            // One identity per object - the component refuses a second one - so the fleet is two objects.
            var host = new GameObject("robot_1");
            var other = new GameObject("robot_2");
            try
            {
                var first = host.AddComponent<RobotIdentity>();
                first.Bind("robot_1", isPrimary: true, null);

                Assert.That(
                    first.StreamNames(RobotSNAPTopics.CmdVel, string.Empty),
                    Is.EqualTo(new[] { "/drive", "/robot_1/drive" }),
                    "the first robot keeps the bare name and answers under its id as well");

                var second = other.AddComponent<RobotIdentity>();
                second.Bind("robot_2", isPrimary: false, null);

                Assert.That(
                    second.StreamNames(RobotSNAPTopics.CmdVel, string.Empty),
                    Is.EqualTo(new[] { "/robot_2/drive" }),
                    "a later robot answers under its id alone");

                Assert.That(
                    second.StreamNames(RobotSNAPTopics.CmdVel, "env"),
                    Is.EqualTo(new[] { "/env/robot_2/drive" }),
                    "the environment prefix is still joined on top of the id");
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(other);
            }
        }

        [TestCase("odom", "/odom")]
        [TestCase("/odom", "/odom")]
        [TestCase("//odom//", "/odom")]
        [TestCase("/simulation/agents", "/simulation/agents")]
        [TestCase("  /cmd_vel  ", "/cmd_vel")]
        public void ATypedNameIsHeldInOneShape(string typed, string expected)
        {
            Assert.That(RosTopicNames.TryNormalize(typed, out string normalized, out string problem), Is.True, problem);
            Assert.That(normalized, Is.EqualTo(expected));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("/")]
        [TestCase("//")]
        [TestCase("my topic")]
        [TestCase("/cmd vel")]
        [TestCase("/scan#1")]
        [TestCase("/robot_1/cmd-vel")]
        public void ANameTheWireWouldRefuseIsRefused(string typed)
        {
            Assert.That(RosTopicNames.TryNormalize(typed, out string normalized, out string problem), Is.False);
            Assert.That(normalized, Is.Null);
            Assert.That(problem, Is.Not.Null.And.Not.Empty, "a refusal has to say why");
        }

        [Test]
        public void TheTableKnowsEveryStreamItCarries()
        {
            foreach (RosTopicNames.Row row in RosTopicNames.Rows)
            {
                var table = new RosTopicNames();
                Assert.That(table.Get(row.Slot), Is.EqualTo(row.Default), $"{row.Slot} is not named in Get");
                Assert.That(RosTopicNames.Shipped(row.Slot), Is.EqualTo(row.Default));
            }

            Assert.That(
                RosTopicNames.Rows.Length,
                Is.EqualTo(RobotSNAPTopics.ContractSlots.Length + 1),
                "the contract plus the additive metrics stream");
        }
    }
}
