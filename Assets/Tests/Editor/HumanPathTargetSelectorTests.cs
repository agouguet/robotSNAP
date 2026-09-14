using NUnit.Framework;
using RobotSNAP.Agents;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    public sealed class HumanPathTargetSelectorTests
    {
        [Test]
        public void SelectTarget_AdvancesPastNearbyIntermediateCorner()
        {
            Vector3[] corners =
            {
                new(-1f, 0f, 0f),
                Vector3.zero,
                new(0f, 0f, -10f)
            };

            Vector2 target = HumanPathTargetSelector.SelectTarget(
                new Vector2(-1f, 0f),
                new Vector2(0f, -10f),
                corners,
                1.5f,
                0.2f);

            Assert.That(target, Is.EqualTo(new Vector2(0f, -10f)));
        }

        [Test]
        public void SelectTarget_KeepsDistantIntermediateCorner()
        {
            Vector3[] corners =
            {
                new(-8f, 0f, 0f),
                Vector3.zero,
                new(0f, 0f, -10f)
            };

            Vector2 target = HumanPathTargetSelector.SelectTarget(
                new Vector2(-8f, 0f),
                new Vector2(0f, -10f),
                corners,
                1.5f,
                0.2f);

            Assert.That(target, Is.EqualTo(Vector2.zero));
        }
    }
}
