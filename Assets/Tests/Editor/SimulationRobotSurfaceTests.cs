using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RobotSNAP.Agents;
using RobotSNAP.ROS;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// The session surface once several robots share a scene: the state snapshot describes the whole roster,
    /// and a robot command reaches the robot it names instead of an arbitrary one.
    ///
    /// Building a real roster means running the scenario applier, so the cases below fill the roster of a
    /// <see cref="RobotRoster"/> by reflection and plant it as <see cref="RobotRoster.Current"/>. The router
    /// and the publisher are then driven through their public surface, over that roster, which is what makes
    /// the resolution they do here the resolution a session does.
    /// </summary>
    public sealed class SimulationRobotSurfaceTests
    {
        private static readonly System.Type SlotType =
            typeof(RobotRoster).GetNestedType("Slot", BindingFlags.NonPublic);

        private static readonly FieldInfo Slots =
            typeof(RobotRoster).GetField("_slots", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly SimulationCommandRouter _router = new SimulationCommandRouter();

        [TearDown]
        public void TearDown()
        {
            // A component added outside play mode does not run its own Awake, so Current is planted and
            // cleared here rather than left to the lifecycle of the objects below.
            SetCurrent(null);

            foreach (GameObject created in _created)
            {
                if (created != null)
                    Object.DestroyImmediate(created);
            }

            _created.Clear();
        }

        [Test]
        public void ACommandWithoutARobotKeyAddressesThePrimary()
        {
            RobotRoster roster = Roster("robot_1", "robot_2");
            roster.TryGet("robot_1", out Robot primary);
            roster.TryGet("robot_2", out Robot second);

            CommandResult result = _router.Execute("{\"command\":\"set_robot_goal\",\"x\":1,\"z\":2}");

            Assert.That(result.Ok, Is.True);
            Assert.That(result.Robot, Is.EqualTo("robot_1"), "The answer echoes the robot it acted on.");
            Assert.That(primary.HasGoal, Is.True, "The primary robot is the one an unnamed command reaches.");
            Assert.That(second.HasGoal, Is.False, "No other robot of the roster moved.");
        }

        [Test]
        public void ABlankRobotKeyAddressesThePrimary()
        {
            RobotRoster roster = Roster("robot_1", "robot_2");
            roster.TryGet("robot_2", out Robot second);

            CommandResult blank = _router.Execute("{\"command\":\"set_robot_goal\",\"x\":1,\"z\":2,\"robot\":\"   \"}");
            CommandResult absent = _router.Execute("{\"command\":\"set_robot_goal\",\"x\":3,\"z\":4,\"robot\":null}");

            Assert.That(blank.Ok, Is.True);
            Assert.That(blank.Robot, Is.EqualTo("robot_1"));
            Assert.That(absent.Ok, Is.True);
            Assert.That(absent.Robot, Is.EqualTo("robot_1"));
            Assert.That(second.HasGoal, Is.False);
        }

        [Test]
        public void ARobotKeyAddressesTheRobotItNames()
        {
            RobotRoster roster = Roster("robot_1", "robot_2");
            roster.TryGet("robot_1", out Robot primary);
            roster.TryGet("robot_2", out Robot second);

            CommandResult result = _router.Execute("{\"command\":\"set_robot_goal\",\"x\":1,\"z\":2,\"robot\":\"robot_2\"}");

            Assert.That(result.Ok, Is.True);
            Assert.That(result.Robot, Is.EqualTo("robot_2"));
            Assert.That(second.HasGoal, Is.True, "The named robot took the goal.");
            Assert.That(primary.HasGoal, Is.False, "The primary robot was not touched.");
        }

        [Test]
        public void AnUnknownRobotIdIsRefusedAndTheMessageNamesTheOnesTheRosterCarries()
        {
            Roster("robot_1", "robot_2");

            CommandResult result = _router.Execute("{\"command\":\"stop_robot\",\"robot\":\"robot_9\"}");

            Assert.That(result.Ok, Is.False);
            Assert.That(result.Message, Does.Contain("robot_9"));
            Assert.That(result.Message, Does.Contain("robot_1"), "The refusal names the robots a client could address.");
            Assert.That(result.Message, Does.Contain("robot_2"));
        }

        [Test]
        public void WithoutARobotTheSnapshotCarriesAnEmptyRoster()
        {
            // The opened scene of the editor carries no robot: the roster of a session that is not running is
            // the empty case this pins.
            JObject snapshot = Snapshot();

            Assert.That(snapshot["robots"].Type, Is.EqualTo(JTokenType.Array));
            Assert.That(snapshot["robots"], Is.Empty);
            Assert.That((int)snapshot["robot_count"], Is.EqualTo(0));
            Assert.That(snapshot["robot"].Type, Is.EqualTo(JTokenType.Null));
        }

        [Test]
        public void ARobotEntryCarriesExactlyTheKeysOfTheContract()
        {
            Roster("robot_1");

            JObject entry = (JObject)Snapshot()["robots"][0];

            Assert.That(
                entry.Properties().Select(property => property.Name),
                Is.EquivalentTo(new[]
                {
                    "id", "type", "is_primary", "x", "y", "z", "yaw",
                    "has_goal", "goal", "start_pose", "target_pose"
                })
            );
            Assert.That((string)entry["id"], Is.EqualTo("robot_1"));
            Assert.That((bool)entry["is_primary"], Is.True);
            Assert.That((string)entry["type"], Is.EqualTo(RobotProfiles.DefaultId));
        }

        [Test]
        public void TheLegacyRobotKeysStillDescribeThePrimaryRobotOfTheRoster()
        {
            RobotRoster roster = Roster("robot_1", "robot_2");
            roster.TryGet("robot_1", out Robot primary);
            roster.TryGet("robot_2", out Robot second);
            primary.transform.position = new Vector3(2f, 0f, 3f);
            second.transform.position = new Vector3(-4f, 0f, -5f);
            primary.SetGoal(new Vector3(6f, 0f, 7f));

            JObject snapshot = Snapshot();

            Assert.That((int)snapshot["robot_count"], Is.EqualTo(2));
            Assert.That((string)snapshot["robots"][0]["id"], Is.EqualTo("robot_1"), "The order is the roster's.");
            Assert.That((string)snapshot["robots"][1]["id"], Is.EqualTo("robot_2"));

            // The one robot named by the legacy keys is the primary, not whichever robot came last.
            Assert.That(Pose(snapshot["robot"]), Is.EqualTo(Pose(snapshot["robots"][0])));
            Assert.That(Pose(snapshot["robot"]), Is.Not.EqualTo(Pose(snapshot["robots"][1])));
            Assert.That((bool)snapshot["robot_has_goal"], Is.True);
            Assert.That(Position(snapshot["robot_goal"]), Is.EqualTo(Position(snapshot["robots"][0]["goal"])));
        }

        /// <summary>A publisher in the scene, read without the ROS loop the editor does not run.</summary>
        private JObject Snapshot()
        {
            var created = new GameObject("test-simulation-state");
            created.SetActive(false);
            _created.Add(created);

            var publisher = created.AddComponent<SimulationStatePublisher>();
            return JObject.Parse(publisher.CurrentStateJson);
        }

        /// <summary>The three pose numbers of a snapshot entry, so two entries compare whatever else they carry.</summary>
        private static (double x, double y, double z, double yaw) Pose(JToken token) =>
            ((double)token["x"], (double)token["y"], (double)token["z"], (double)token["yaw"]);

        /// <summary>The three position numbers of a goal or a pose, for the same reason.</summary>
        private static (double x, double y, double z) Position(JToken token) =>
            ((double)token["x"], (double)token["y"], (double)token["z"]);

        /// <summary>
        /// A roster planted as the current one, carrying one robot per id in the order given, which is the
        /// order a scenario lists them.
        /// </summary>
        private RobotRoster Roster(params string[] ids)
        {
            var created = new GameObject("test-robot-roster");
            _created.Add(created);

            var roster = created.AddComponent<RobotRoster>();
            var slots = (IList)Slots.GetValue(roster);

            foreach (string id in ids)
                slots.Add(Slot(id));

            SetCurrent(roster);
            return roster;
        }

        /// <summary>One entry of the roster's private slot list, holding a live robot under an id.</summary>
        private object Slot(string id)
        {
            var created = new GameObject(id);
            _created.Add(created);

            object slot = System.Activator.CreateInstance(SlotType, nonPublic: true);
            SlotType.GetField("Id").SetValue(slot, id);
            SlotType.GetField("TypeId").SetValue(slot, RobotProfiles.DefaultId);
            SlotType.GetField("Robot").SetValue(slot, created.AddComponent<Robot>());
            return slot;
        }

        /// <summary>Plants the roster the two session surfaces resolve their robots through.</summary>
        private static void SetCurrent(RobotRoster roster)
        {
            typeof(RobotRoster)
                .GetProperty("Current", BindingFlags.Public | BindingFlags.Static)
                .GetSetMethod(true)
                .Invoke(null, new object[] { roster });
        }
    }
}
