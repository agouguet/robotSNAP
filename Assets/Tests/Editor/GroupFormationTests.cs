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

        [Test]
        public void CohesionFactor_FullSpeedWhenTightAndStopAtTheFullError()
        {
            Assert.That(GroupProgress.CohesionFactor(0f), Is.EqualTo(1f));
            Assert.That(GroupProgress.CohesionFactor(1f), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(GroupProgress.CohesionFactor(GroupProgress.FullErrorDistance), Is.EqualTo(0f));
            Assert.That(
                GroupProgress.CohesionFactor(GroupProgress.FullErrorDistance * 3f),
                Is.EqualTo(0f),
                "Past the full error the group stays stopped instead of reversing.");
        }

        [Test]
        public void AdvanceSpeed_KeepsCruiseSpeedWhileTheGroupIsTight()
        {
            // A tight group walks at its cruise speed even if one member happens to be standing still.
            Assert.That(
                GroupProgress.AdvanceSpeed(1.2f, 0f, 0f),
                Is.EqualTo(1.2f).Within(0.0001f));
        }

        [Test]
        public void AdvanceSpeed_SlowsDownWithTheWorstFormationError()
        {
            Assert.That(
                GroupProgress.AdvanceSpeed(1f, 1f, 5f),
                Is.EqualTo(0.5f).Within(0.0001f),
                "One metre behind means half the cruise speed.");
            Assert.That(
                GroupProgress.AdvanceSpeed(1f, GroupProgress.FullErrorDistance, 5f),
                Is.EqualTo(0f),
                "The group waits once a member is two metres out of place.");
        }

        [Test]
        public void AdvanceSpeed_WaitsForTheSlowestMemberButKeepsInching()
        {
            // The laggard is stopped: the reference drops to its pace, never below the floor, so the group
            // re-forms instead of either stretching out or freezing for good.
            float speed = GroupProgress.AdvanceSpeed(1.5f, 1.5f, 0f);

            Assert.That(speed, Is.EqualTo(1.5f * GroupProgress.MinimumFactor).Within(0.0001f));

            // A slow (not stopped) member sets the pace exactly, as long as the cohesion term alone is still
            // asking for more than its own speed.
            Assert.That(
                GroupProgress.AdvanceSpeed(1.5f, 1f, 0.4f),
                Is.EqualTo(0.4f).Within(0.0001f),
                "Cohesion alone would ask for 0.75 m/s, so the laggard caps the group at its own pace.");
        }

        [Test]
        public void AdvanceSpeed_IgnoresTheLaggardBrakeBeforeTheGroupHasStarted()
        {
            // At spawn every member is still standing; braking there would keep the group parked forever.
            Assert.That(
                GroupProgress.AdvanceSpeed(1.2f, GroupProgress.LagStartDistance * 0.5f, 0f),
                Is.GreaterThan(0f));
        }

        [Test]
        public void TurnFactor_SlowsTheGroupIntoACornerWithoutStoppingIt()
        {
            Assert.That(GroupProgress.TurnFactor(0f), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                GroupProgress.TurnFactor(GroupProgress.FullTurnErrorDegrees),
                Is.EqualTo(GroupProgress.MinimumTurnFactor).Within(0.0001f));
            Assert.That(
                GroupProgress.TurnFactor(180f),
                Is.EqualTo(GroupProgress.MinimumTurnFactor).Within(0.0001f),
                "Even a U-turn keeps a floor of forward speed.");
        }

        [Test]
        public void AdvanceAlong_LandsExactlyOnAWaypointInsteadOfOvershootingIt()
        {
            var route = new List<Vector2> { Vector2.zero, new Vector2(10f, 0f), new Vector2(10f, 10f) };
            int leg = 1;

            // The step is larger than the whole leg: the reference must stop on the waypoint, not run past it.
            Vector2 position = GroupProgress.AdvanceAlong(route, ref leg, Vector2.zero, 25f, 0.2f, out bool completed);

            Assert.That(completed, Is.False);
            Assert.That(position, Is.EqualTo(new Vector2(10f, 0f)));
            Assert.That(leg, Is.EqualTo(2), "The waypoint was reached, so the next leg is already selected.");
        }

        [Test]
        public void AdvanceAlong_ConsumesEveryWaypointInsideTheArrivalRadius()
        {
            var route = new List<Vector2> { new(0f, 0f), new(0.1f, 0f), new(0.2f, 0f), new(5f, 0f) };
            int leg = 1;

            Vector2 position = GroupProgress.AdvanceAlong(route, ref leg, Vector2.zero, 0f, 0.25f, out bool completed);

            Assert.That(completed, Is.False);
            Assert.That(position, Is.EqualTo(new Vector2(0.2f, 0f)));
            Assert.That(leg, Is.EqualTo(3), "Three points were inside the arrival radius.");
        }

        [Test]
        public void AdvanceAlong_ReportsTheEndOfTheRoute()
        {
            var route = new List<Vector2> { Vector2.zero, new Vector2(1f, 0f) };
            int leg = 1;

            GroupProgress.AdvanceAlong(route, ref leg, new Vector2(0.5f, 0f), 5f, 0.2f, out bool completed);

            Assert.That(completed, Is.True);
            Assert.That(leg, Is.EqualTo(route.Count));
        }

        [Test]
        public void AdvanceAlong_WalksBackToTheFirstPointWhenTheRouteLoops()
        {
            var route = new List<Vector2> { new(0f, 0f), new(10f, 0f) };
            // Index 0 is the return leg of a looping route.
            int leg = 0;

            Vector2 position = GroupProgress.AdvanceAlong(route, ref leg, new Vector2(10f, 0f), 4f, 0.2f, out bool completed);

            Assert.That(completed, Is.False, "Coming home is not the end of a looping route.");
            Assert.That(position, Is.EqualTo(new Vector2(6f, 0f)));
            Assert.That(leg, Is.EqualTo(0));

            // Arriving home puts the group back on the ordinary legs.
            GroupProgress.AdvanceAlong(route, ref leg, new Vector2(0.1f, 0f), 0f, 0.2f, out completed);
            Assert.That(leg, Is.EqualTo(1));
            Assert.That(completed, Is.False);
        }

        [Test]
        public void AdvanceAlong_TreatsAnEmptyRouteAsFinished()
        {
            int leg = 1;

            GroupProgress.AdvanceAlong(new List<Vector2>(), ref leg, Vector2.one, 1f, 0.2f, out bool completed);

            Assert.That(completed, Is.True);
        }
    }
}
