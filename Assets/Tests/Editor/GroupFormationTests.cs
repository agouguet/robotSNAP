using System.Collections.Generic;
using NUnit.Framework;
using RobotSNAP.Agents;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    public sealed class GroupFormationTests
    {
        [Test]
        public void CreateSlots_KeepsTheLeaderOnTheFirstSlot()
        {
            List<Vector2> slots = GroupFormation.CreateSlots(3, 1f, GroupFormation.WedgeFormation);

            Assert.That(slots.Count, Is.EqualTo(3));
            Assert.That(slots[0], Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void CreateSlots_WedgeAlternatesBehindTheLeader()
        {
            List<Vector2> slots = GroupFormation.CreateSlots(3, 1f, GroupFormation.WedgeFormation);

            Assert.That(slots[1].x, Is.LessThan(0f));
            Assert.That(slots[2].x, Is.GreaterThan(0f));
            Assert.That(slots[1].y, Is.EqualTo(-0.7f).Within(0.001f));
            Assert.That(slots[2].y, Is.EqualTo(-0.7f).Within(0.001f));
            // A pedestrian V is wider than it is deep, otherwise it reads as a queue.
            Assert.That(Mathf.Abs(slots[1].x), Is.GreaterThan(Mathf.Abs(slots[1].y)));
        }

        [Test]
        public void CreateSlots_RowWalksAbreastOnBothSides()
        {
            List<Vector2> slots = GroupFormation.CreateSlots(5, 2f, GroupFormation.RowFormation);

            Assert.That(slots[0], Is.EqualTo(Vector2.zero));
            Assert.That(slots[1], Is.EqualTo(new Vector2(2f, 0f)));
            Assert.That(slots[2], Is.EqualTo(new Vector2(-2f, 0f)));
            Assert.That(slots[3].x, Is.EqualTo(4f).Within(0.001f));
            Assert.That(slots[4].x, Is.EqualTo(-4f).Within(0.001f));
            // The outermost members trail slightly so the row reads as a loose arc.
            Assert.That(slots[3].y, Is.LessThan(0f));
            Assert.That(slots[4].y, Is.EqualTo(slots[3].y).Within(0.001f));
        }

        [Test]
        public void CreateSlots_LineStacksBehindTheLeader()
        {
            List<Vector2> slots = GroupFormation.CreateSlots(3, 2f, GroupFormation.ColumnFormation);

            Assert.That(slots[1], Is.EqualTo(new Vector2(0f, -2f)));
            Assert.That(slots[2], Is.EqualTo(new Vector2(0f, -4f)));
        }

        [Test]
        public void CreateSlots_PairWalksTwoAbreast()
        {
            List<Vector2> slots = GroupFormation.CreateSlots(4, 2f, GroupFormation.PairFormation);

            Assert.That(slots[1], Is.EqualTo(new Vector2(1f, 0f)));
            Assert.That(slots[2], Is.EqualTo(new Vector2(-1f, -2f)));
            Assert.That(slots[3], Is.EqualTo(new Vector2(1f, -2f)));
        }

        [Test]
        public void CreateSlots_ClusterStaysCloseToTheLeader()
        {
            List<Vector2> slots = GroupFormation.CreateSlots(6, 1f, GroupFormation.ClusterFormation);

            Assert.That(slots.Count, Is.EqualTo(6));
            for (int index = 1; index < slots.Count; index++)
            {
                Assert.That(slots[index].magnitude, Is.LessThanOrEqualTo(1.6f + 0.001f));
                Assert.That(slots[index].magnitude, Is.GreaterThan(0.1f));
            }
        }

        [Test]
        public void CreateSlots_HandlesEmptyAndSingleGroups()
        {
            Assert.That(GroupFormation.CreateSlots(0, 1f, GroupFormation.WedgeFormation), Is.Empty);
            Assert.That(GroupFormation.CreateSlots(1, 1f, GroupFormation.WedgeFormation).Count, Is.EqualTo(1));
        }

        [Test]
        public void Rotate_TurnsOffsetsWithTheLeaderYaw()
        {
            // A quarter turn puts the leader's forward (local +y) on world +x...
            Vector2 rotated = GroupFormation.Rotate(new Vector2(1f, 0f), Mathf.PI * 0.5f);

            Assert.That(rotated.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(rotated.y, Is.EqualTo(-1f).Within(0.001f));

            // ... so its right (local +x) points down world -z, and its forward matches (sin, cos).
            Vector2 forward = GroupFormation.Rotate(new Vector2(0f, 1f), Mathf.PI * 0.5f);
            Assert.That(forward.x, Is.EqualTo(1f).Within(0.001f));
            Assert.That(forward.y, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void Rotate_KeepsTrailingSlotsBehindTheLeaderWhenItTurns()
        {
            // Column slot: one metre behind the leader in its local frame.
            Vector2 behind = new(0f, -1f);

            Vector2 straight = GroupFormation.Rotate(behind, 0f);
            Assert.That(straight.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(straight.y, Is.EqualTo(-1f).Within(0.001f));

            // Leader now walks towards world +x: the slot must trail on -x, not lead on +x.
            Vector2 turned = GroupFormation.Rotate(behind, Mathf.PI * 0.5f);
            Assert.That(turned.x, Is.EqualTo(-1f).Within(0.001f));
            Assert.That(turned.y, Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void Normalize_ReadsFormationNamesAndLegacyAliases()
        {
            Assert.That(GroupFormation.Normalize(null), Is.EqualTo(GroupFormation.PairFormation));
            Assert.That(GroupFormation.Normalize("grid"), Is.EqualTo(GroupFormation.PairFormation));
            Assert.That(GroupFormation.Normalize("LINE"), Is.EqualTo(GroupFormation.ColumnFormation));
            Assert.That(GroupFormation.Normalize("Column"), Is.EqualTo(GroupFormation.ColumnFormation));
            Assert.That(GroupFormation.Normalize("cluster"), Is.EqualTo(GroupFormation.ClusterFormation));
            Assert.That(GroupFormation.Normalize("wedge"), Is.EqualTo(GroupFormation.WedgeFormation));
            Assert.That(GroupFormation.Normalize("abreast"), Is.EqualTo(GroupFormation.RowFormation));
            Assert.That(GroupFormation.Normalize("shoulder"), Is.EqualTo(GroupFormation.RowFormation));
        }

        [Test]
        public void RequiredGoalDistance_MatchesTheControllersSpeedRamp()
        {
            // Full cruise speed: the goal must sit a whole slow-down distance ahead of the slot.
            Assert.That(
                GroupFormation.RequiredGoalDistance(0.8f, 0.8f, 1.5f),
                Is.EqualTo(1.5f).Within(0.001f));

            // Half speed lands halfway up the 20%..100% ramp of the controller.
            Assert.That(
                GroupFormation.RequiredGoalDistance(0.4f, 0.8f, 1.5f),
                Is.EqualTo(1.5f * 0.375f).Within(0.001f));

            // Below the ramp floor the agent would creep: aim straight at the slot instead.
            Assert.That(GroupFormation.RequiredGoalDistance(0.1f, 0.8f, 1.5f), Is.EqualTo(0f));
            Assert.That(GroupFormation.RequiredGoalDistance(0f, 0.8f, 1.5f), Is.EqualTo(0f));

            // A faster leader than the follower's cruise speed cannot ask for more than full ramp.
            Assert.That(
                GroupFormation.RequiredGoalDistance(2f, 0.8f, 1.5f),
                Is.EqualTo(1.5f).Within(0.001f));
        }

        [Test]
        public void SteadyHeading_TurnsAtABoundedRateAndTakesTheShortWay()
        {
            float quarterTurn = Mathf.PI * 0.5f;
            const float step = 0.1f;

            // 90° per second for 0.1 s: the heading advances by exactly 9°.
            float heading = GroupFormation.SteadyHeading(0f, quarterTurn, quarterTurn, step);
            Assert.That(heading, Is.EqualTo(quarterTurn * step).Within(0.0001f));

            // Crossing ±π must turn by 20°, not by 340°.
            float almostPi = Mathf.PI - 0.1f;
            float minusAlmostPi = -Mathf.PI + 0.1f;
            float crossed = GroupFormation.SteadyHeading(almostPi, minusAlmostPi, quarterTurn, step);
            float turned = Mathf.DeltaAngle(almostPi * Mathf.Rad2Deg, crossed * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            Assert.That(turned, Is.EqualTo(quarterTurn * step).Within(0.001f));

            // No time or no rate means no movement.
            Assert.That(GroupFormation.SteadyHeading(0.5f, 1.5f, 0f, 0.1f), Is.EqualTo(0.5f));
            Assert.That(GroupFormation.SteadyHeading(0.5f, 1.5f, 1f, 0f), Is.EqualTo(0.5f));
        }
    }
}
