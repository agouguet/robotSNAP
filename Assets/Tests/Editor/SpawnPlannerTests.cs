using System.Collections.Generic;
using NUnit.Framework;
using RobotSNAP.Core.Scenario;
using UnityEngine;
using UnityEngine.AI;

namespace RobotSNAP.Tests.Editor
{
    public sealed class SpawnPlannerTests
    {
        [Test]
        public void ResolveGroupLayouts_TakesTheFirstExplicitFormationAndSpacing()
        {
            var humans = new List<HumanScenarioConfig>
            {
                new() { Group = "g1", Spawn = new SpawnConfig { Formation = null, Spacing = 1.2f } },
                new() { Group = "g1", Spawn = new SpawnConfig { Formation = "wedge", Spacing = 2.4f } },
                new() { Group = null, Spawn = new SpawnConfig { Formation = "column" } }
            };

            Dictionary<string, SpawnPlanner.GroupLayout> layouts = SpawnPlanner.ResolveGroupLayouts(humans);

            Assert.That(layouts.Count, Is.EqualTo(1), "Only grouped routes define a layout.");
            Assert.That(layouts["g1"].HasFormation, Is.True);
            Assert.That(layouts["g1"].Formation, Is.EqualTo("wedge"));
            Assert.That(layouts["g1"].Spacing, Is.EqualTo(1.2f).Within(0.001f));
        }

        [Test]
        public void ResolveGroupLayouts_IgnoresAnUngroupedRoute()
        {
            var humans = new List<HumanScenarioConfig>
            {
                new() { Group = "   ", Spawn = new SpawnConfig { Formation = "cluster" } }
            };

            Assert.That(SpawnPlanner.ResolveGroupLayouts(humans), Is.Empty);
            Assert.That(SpawnPlanner.ResolveGroupLayouts(null), Is.Empty);
        }

        [Test]
        public void ClampSpacing_KeepsGroupsCompact()
        {
            Assert.That(SpawnPlanner.ClampSpacing(1.5f), Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(SpawnPlanner.ClampSpacing(42f), Is.EqualTo(SpawnPlanner.MaxSpacing));
            Assert.That(SpawnPlanner.ClampSpacing(0.1f), Is.EqualTo(SpawnPlanner.MinSpacing));
            Assert.That(SpawnPlanner.ClampSpacing(0f), Is.EqualTo(SpawnPlanner.DefaultSpacing));
            Assert.That(SpawnPlanner.ClampSpacing(-4f), Is.EqualTo(SpawnPlanner.DefaultSpacing));
        }

        [Test]
        public void HeadingTowards_WalksToTheFirstObjectiveThenTheSecond()
        {
            Assert.That(
                SpawnPlanner.HeadingTowards(Vector2.zero, new List<Vector3> { new(0f, 0f, 5f) }),
                Is.EqualTo(0f).Within(0.0001f));

            Assert.That(
                SpawnPlanner.HeadingTowards(Vector2.zero, new List<Vector3> { new(5f, 0f, 0f) }),
                Is.EqualTo(Mathf.PI * 0.5f).Within(0.0001f));

            // Standing on the first objective: the planner looks at the second one instead.
            Assert.That(
                SpawnPlanner.HeadingTowards(Vector2.zero, new List<Vector3> { Vector3.zero, new(0f, 0f, -4f) }),
                Is.EqualTo(Mathf.PI).Within(0.0001f));

            Assert.That(SpawnPlanner.HeadingTowards(Vector2.zero, null), Is.EqualTo(0f));
            Assert.That(SpawnPlanner.HeadingTowards(Vector2.zero, new List<Vector3>()), Is.EqualTo(0f));
        }

        [Test]
        public void SlotOffset_LeavesTheLeaderOnTheAnchor()
        {
            Assert.That(SpawnPlanner.SlotOffset(0, 4, 1.5f, "pair", 0f), Is.EqualTo(Vector2.zero));
            Assert.That(SpawnPlanner.SlotOffset(0, 1, 1.5f, "wedge", 0f), Is.EqualTo(Vector2.zero));
            Assert.That(SpawnPlanner.SlotOffset(2, 3, 1.5f, "column", 0f).x, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void SlotOffset_RotatesWithTheRouteHeading()
        {
            // Pair: the first follower walks beside the leader, so half a spacing to its right.
            Vector2 beside = SpawnPlanner.SlotOffset(1, 3, 1.5f, "pair", 0f);
            Assert.That(beside.x, Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(beside.y, Is.EqualTo(0f).Within(0.001f));

            // Same slot when the route heads towards world +x: it stays on the leader's right side.
            Vector2 turned = SpawnPlanner.SlotOffset(1, 3, 1.5f, "pair", Mathf.PI * 0.5f);
            Assert.That(turned.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(turned.y, Is.EqualTo(-0.75f).Within(0.001f));

            // Column: followers stay behind the leader whichever way it walks.
            Vector2 trailing = SpawnPlanner.SlotOffset(1, 2, 2f, "column", Mathf.PI * 0.5f);
            Assert.That(trailing.x, Is.EqualTo(-2f).Within(0.001f));
            Assert.That(trailing.y, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void SlotOffset_ClampsTheSpacingAndTheIndex()
        {
            // Beyond the last slot, the last one is reused instead of throwing.
            Vector2 last = SpawnPlanner.SlotOffset(9, 3, 1.5f, "column", 0f);
            Assert.That(last.y, Is.EqualTo(-3f).Within(0.001f));

            // A single agent has no formation to lay out.
            Assert.That(SpawnPlanner.SlotOffset(0, 0, 1.5f, "pair", 0f), Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void PlaceSlot_KeepsTheAuthoredSlotWhenNoNavMeshIsAvailable()
        {
            if (NavMesh.SamplePosition(Vector3.zero, out _, 100f, NavMesh.AllAreas))
                Assert.Ignore("The Editor test scene exposes a NavMesh; projection behaviour applies instead.");

            Vector3 placed = SpawnPlanner.PlaceSlot(new Vector3(1f, 0.5f, -2f), new Vector2(1.5f, -1f), 1.5f);

            Assert.That(placed.x, Is.EqualTo(2.5f).Within(0.001f));
            Assert.That(placed.z, Is.EqualTo(-3f).Within(0.001f));
            Assert.That(placed.y, Is.EqualTo(0.5f).Within(0.001f));
        }
    }
}
