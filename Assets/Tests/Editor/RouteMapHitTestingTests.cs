using System.Collections.Generic;
using NUnit.Framework;
using RobotSNAP.Core.Scenario;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    public sealed class RouteMapHitTestingTests
    {
        private static readonly List<Vector2> Route = new()
        {
            new Vector2(0f, 0f),
            new Vector2(10f, 0f),
            new Vector2(10f, 10f)
        };

        [Test]
        public void FindPoint_ReturnsClosestPointInsideRadius()
        {
            int index = RouteMapHitTesting.FindPoint(Route, new Vector2(11f, 1f), 4f);

            Assert.That(index, Is.EqualTo(1));
        }

        [Test]
        public void FindPoint_ReturnsMinusOneWhenNothingIsCloseEnough()
        {
            int index = RouteMapHitTesting.FindPoint(Route, new Vector2(40f, 40f), 4f);

            Assert.That(index, Is.EqualTo(-1));
        }

        [Test]
        public void FindPoint_PrefersTheClosestOfTwoCandidates()
        {
            int index = RouteMapHitTesting.FindPoint(Route, new Vector2(9f, 8f), 12f);

            Assert.That(index, Is.EqualTo(2));
        }

        [Test]
        public void FindSegment_HitsThePolylineBetweenTwoPoints()
        {
            int segment = RouteMapHitTesting.FindSegment(Route, new Vector2(5f, 2f), 4f);

            Assert.That(segment, Is.EqualTo(1));
        }

        [Test]
        public void FindSegment_IgnoresDistantCursor()
        {
            int segment = RouteMapHitTesting.FindSegment(Route, new Vector2(5f, 30f), 4f);

            Assert.That(segment, Is.EqualTo(-1));
        }

        [Test]
        public void DistanceToSegment_ClampsToTheSegmentEnds()
        {
            float distance = RouteMapHitTesting.DistanceToSegment(
                new Vector2(-5f, 0f),
                Vector2.zero,
                new Vector2(10f, 0f));

            Assert.That(distance, Is.EqualTo(5f).Within(0.0001f));
        }

        [Test]
        public void PointLabel_NamesStartAndFollowingObjectives()
        {
            Assert.That(RouteMapHitTesting.PointLabel(0), Is.EqualTo("Start"));
            Assert.That(RouteMapHitTesting.PointLabel(3), Is.EqualTo("Objective 3"));
        }
    }
}
