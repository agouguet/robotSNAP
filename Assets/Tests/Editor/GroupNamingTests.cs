using System.Collections.Generic;
using NUnit.Framework;
using RobotSNAP.Core.Scenario;

namespace RobotSNAP.Tests.Editor
{
    public sealed class GroupNamingTests
    {
        [Test]
        public void ToDisplayName_ShowsTheGeneratedIdentifierAsAReadableLabel()
        {
            Assert.That(GroupNaming.ToDisplayName("group_1"), Is.EqualTo("Group 1"));
            Assert.That(GroupNaming.ToDisplayName("group_3"), Is.EqualTo("Group 3"));
            Assert.That(GroupNaming.ToDisplayName("group_12"), Is.EqualTo("Group 12"));
            Assert.That(GroupNaming.ToDisplayName("group_128"), Is.EqualTo("Group 128"));
        }

        [Test]
        public void ToGroupId_ReadsTheReadableLabelBackAsTheGeneratedIdentifier()
        {
            Assert.That(GroupNaming.ToGroupId("Group 1"), Is.EqualTo("group_1"));
            Assert.That(GroupNaming.ToGroupId("Group 3"), Is.EqualTo("group_3"));
            Assert.That(GroupNaming.ToGroupId("Group 12"), Is.EqualTo("group_12"));
            Assert.That(GroupNaming.ToGroupId("Group 128"), Is.EqualTo("group_128"));
        }

        [Test]
        public void GeneratedIdentifiers_RoundTripThroughBothDirections()
        {
            string[] identifiers = { "group_1", "group_2", "group_3", "group_12", "group_128" };

            foreach (string identifier in identifiers)
            {
                string label = GroupNaming.ToDisplayName(identifier);
                Assert.That(GroupNaming.ToGroupId(label), Is.EqualTo(identifier));
                Assert.That(GroupNaming.ToDisplayName(identifier), Is.EqualTo(label));
            }
        }

        [Test]
        public void ToDisplayName_KeepsHandWrittenIdentifiersAsTheyWereWritten()
        {
            Assert.That(GroupNaming.ToDisplayName("chef"), Is.EqualTo("chef"));
            Assert.That(GroupNaming.ToDisplayName("team_a"), Is.EqualTo("team_a"));
            Assert.That(GroupNaming.ToDisplayName("group_0"), Is.EqualTo("group_0"));
            Assert.That(GroupNaming.ToDisplayName("group_007"), Is.EqualTo("group_007"));
            Assert.That(GroupNaming.ToDisplayName("group_-2"), Is.EqualTo("group_-2"));
            Assert.That(GroupNaming.ToDisplayName("group_"), Is.EqualTo("group_"));
            Assert.That(GroupNaming.ToDisplayName("group1"), Is.EqualTo("group1"));
        }

        [Test]
        public void ToGroupId_KeepsHandWrittenLabelsAsTheyWereWritten()
        {
            Assert.That(GroupNaming.ToGroupId("chef"), Is.EqualTo("chef"));
            Assert.That(GroupNaming.ToGroupId("team_a"), Is.EqualTo("team_a"));
            Assert.That(GroupNaming.ToGroupId("group_0"), Is.EqualTo("group_0"));
            Assert.That(GroupNaming.ToGroupId("group_007"), Is.EqualTo("group_007"));
            Assert.That(GroupNaming.ToGroupId("Group 0"), Is.EqualTo("Group 0"));
            Assert.That(GroupNaming.ToGroupId("Group 007"), Is.EqualTo("Group 007"));
            Assert.That(GroupNaming.ToGroupId("group1"), Is.EqualTo("group1"));
        }

        [Test]
        public void ToDisplayName_IgnoresTheCaseOfThePrefix()
        {
            Assert.That(GroupNaming.ToDisplayName("GROUP_3"), Is.EqualTo("Group 3"));
            Assert.That(GroupNaming.ToDisplayName("Group_3"), Is.EqualTo("Group 3"));
            Assert.That(GroupNaming.ToDisplayName("Group 3"), Is.EqualTo("Group 3"));
        }

        [Test]
        public void ToGroupId_IgnoresTheCaseOfThePrefix()
        {
            Assert.That(GroupNaming.ToGroupId("GROUP_3"), Is.EqualTo("group_3"));
            Assert.That(GroupNaming.ToGroupId("GROUP 3"), Is.EqualTo("group_3"));
            Assert.That(GroupNaming.ToGroupId("group_3"), Is.EqualTo("group_3"));
        }

        [Test]
        public void BothDirections_TolerateSurroundingSpaces()
        {
            Assert.That(GroupNaming.ToDisplayName("  group_12  "), Is.EqualTo("Group 12"));
            Assert.That(GroupNaming.ToGroupId("  Group 12  "), Is.EqualTo("group_12"));
            Assert.That(GroupNaming.ToGroupId("  Group 12  "), Is.EqualTo(GroupNaming.ToGroupId("Group 12")));
        }

        [Test]
        public void BothDirections_ReturnAnEmptyStringForNullEmptyAndBlankInput()
        {
            foreach (string blank in new[] { null, "", " ", "   \t " })
            {
                Assert.That(GroupNaming.ToDisplayName(blank), Is.EqualTo(string.Empty));
                Assert.That(GroupNaming.ToGroupId(blank), Is.EqualTo(string.Empty));
            }
        }

        [Test]
        public void NextId_StartsAtOneWhenNothingIsUsed()
        {
            Assert.That(GroupNaming.NextId(null), Is.EqualTo("group_1"));
            Assert.That(GroupNaming.NextId(new List<string>()), Is.EqualTo("group_1"));
            Assert.That(GroupNaming.NextId(new[] { "" }), Is.EqualTo("group_1"));
        }

        [Test]
        public void NextId_SkipsTheIdentifiersAlreadyUsed()
        {
            Assert.That(GroupNaming.NextId(new[] { "group_1" }), Is.EqualTo("group_2"));
            Assert.That(GroupNaming.NextId(new[] { "group_1", "group_3" }), Is.EqualTo("group_2"));
            Assert.That(GroupNaming.NextId(new[] { "group_2" }), Is.EqualTo("group_1"));
            Assert.That(GroupNaming.NextId(new[] { "group_1", "GROUP_5" }), Is.EqualTo("group_2"));
        }

        [Test]
        public void NextId_IgnoresHandWrittenIdentifiers()
        {
            Assert.That(GroupNaming.NextId(new[] { "chef" }), Is.EqualTo("group_1"));
            Assert.That(GroupNaming.NextId(new[] { "chef", "team_a" }), Is.EqualTo("group_1"));
            Assert.That(GroupNaming.NextId(new[] { "group_0", "group_007", "group_", "group1" }), Is.EqualTo("group_1"));
        }

        [Test]
        public void NextId_FillsTheFirstGapOfADenseSet()
        {
            Assert.That(
                GroupNaming.NextId(new[] { "group_1", "group_2", "group_3", "group_5", "chef", "GROUP_5" }),
                Is.EqualTo("group_4"));
        }
    }
}
