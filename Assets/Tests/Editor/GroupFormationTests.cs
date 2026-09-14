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
            Vector2 rotated = GroupFormation.Rotate(new Vector2(1f, 0f), Mathf.PI * 0.5f);

            Assert.That(rotated.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(rotated.y, Is.EqualTo(1f).Within(0.001f));
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
    }
}
