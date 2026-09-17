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
        public void ResolveLayout_ReadsTheFormationSpacingAndParameterOfOneRoute()
        {
            var config = new HumanScenarioConfig
            {
                Spawn = new SpawnConfig
                {
                    Formation = "wedge",
                    Spacing = 2.4f,
                    FormationParameter = 60f
                }
            };

            SpawnPlanner.GroupLayout layout = SpawnPlanner.ResolveLayout(config);

            Assert.That(layout.HasFormation, Is.True);
            Assert.That(layout.Formation, Is.EqualTo("wedge"));
            Assert.That(layout.Spacing, Is.EqualTo(2.4f).Within(0.001f));
            Assert.That(layout.Parameter, Is.EqualTo(60f).Within(0.001f));
        }

        [Test]
        public void ResolveLayout_WithoutASpawnBlockFallsBackOnTheDefaultSpacing()
        {
            SpawnPlanner.GroupLayout bare = SpawnPlanner.ResolveLayout(new HumanScenarioConfig());

            Assert.That(bare.HasFormation, Is.False);
            Assert.That(bare.Formation, Is.Null);
            Assert.That(bare.Spacing, Is.EqualTo(SpawnPlanner.DefaultSpacing).Within(0.001f));

            // A null route cannot happen in a scenario, but the layout is asked for before the config is
            // validated, so it degrades to the default instead of throwing.
            Assert.That(
                SpawnPlanner.ResolveLayout(null).Spacing,
                Is.EqualTo(SpawnPlanner.DefaultSpacing).Within(0.001f));
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

        [Test]
        public void SpreadsApart_KeepsANamedFormationTogether()
        {
            Assert.That(SpawnPlanner.SpreadsApart("pair", true), Is.False);
            Assert.That(SpawnPlanner.SpreadsApart("wedge", true), Is.False);
            Assert.That(SpawnPlanner.SpreadsApart("column", true), Is.False);
            Assert.That(SpawnPlanner.SpreadsApart("cluster", true), Is.False);
        }

        [Test]
        public void SpreadsApart_ReadsTheScatterNamesAsIndependent()
        {
            Assert.That(SpawnPlanner.SpreadsApart("scatter", false), Is.True);
            Assert.That(SpawnPlanner.SpreadsApart("independent", false), Is.True);
            Assert.That(SpawnPlanner.SpreadsApart("none", false), Is.True);
        }

        [Test]
        public void SpreadsApart_TreatsAnUnnamedRouteWithAnAreaAsACrowd()
        {
            // The traffic crossing of a scenario names no shape: its agents appear over the whole zone and walk
            // their own way, instead of falling back on the pair formation that used to make them one block.
            Assert.That(SpawnPlanner.SpreadsApart(null, true), Is.True);
            Assert.That(SpawnPlanner.SpreadsApart(string.Empty, true), Is.True);
            Assert.That(SpawnPlanner.SpreadsApart("   ", true), Is.True);
        }

        [Test]
        public void SpreadsApart_KeepsAnUnnamedRouteOnASinglePointInFormation()
        {
            // Nowhere to spread into: keeping the formation is what the authored point describes.
            Assert.That(SpawnPlanner.SpreadsApart(null, false), Is.False);
            Assert.That(SpawnPlanner.SpreadsApart("", false), Is.False);
        }
    }
}
