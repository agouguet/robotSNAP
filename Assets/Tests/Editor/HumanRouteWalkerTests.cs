using System.Collections.Generic;
using NUnit.Framework;
using RobotSNAP.Agents;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    public sealed class HumanRouteWalkerTests
    {
        private static readonly Vector3[] Route =
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(2f, 0f, 0f)
        };

        [Test]
        public void SetRoute_StartsOnTheFirstPoint()
        {
            var walker = new HumanRouteWalker();

            walker.SetRoute(Route);

            Assert.That(walker.HasCurrent, Is.True);
            Assert.That(walker.Index, Is.EqualTo(0));
            Assert.That(walker.Current, Is.EqualTo(Route[0]));
        }

        [Test]
        public void AdvanceNext_WalksEveryPointBeforeFinishing()
        {
            var walker = new HumanRouteWalker { EndBehavior = HumanEndBehavior.Stay };
            walker.SetRoute(Route);

            Assert.That(walker.AdvanceNext(), Is.False);
            Assert.That(walker.Current, Is.EqualTo(Route[1]));
            Assert.That(walker.AdvanceNext(), Is.False);
            Assert.That(walker.Current, Is.EqualTo(Route[2]));

            Assert.That(walker.AdvanceNext(), Is.True);
            Assert.That(walker.IsActive, Is.False);
            Assert.That(walker.HasCurrent, Is.False);
        }

        [Test]
        public void AdvanceNext_LoopsBackToTheFirstPointInsteadOfFinishing()
        {
            var walker = new HumanRouteWalker { EndBehavior = HumanEndBehavior.Loop };
            walker.SetRoute(Route);

            walker.AdvanceNext();
            walker.AdvanceNext();

            Assert.That(walker.AdvanceNext(), Is.False);
            Assert.That(walker.Current, Is.EqualTo(Route[0]));
            Assert.That(walker.Loops, Is.EqualTo(1));
            Assert.That(walker.IsActive, Is.True);
        }

        [Test]
        public void AdvanceNext_OnAnInactiveWalker_StaysFinished()
        {
            var walker = new HumanRouteWalker();

            Assert.That(walker.AdvanceNext(), Is.True);
            Assert.That(walker.IsActive, Is.False);
        }

        [Test]
        public void Clear_ResetsTheProgression()
        {
            var walker = new HumanRouteWalker { EndBehavior = HumanEndBehavior.Loop };
            walker.SetRoute(Route);
            walker.AdvanceNext();

            walker.Clear();

            Assert.That(walker.IsActive, Is.False);
            Assert.That(walker.HasCurrent, Is.False);
            Assert.That(walker.Points, Is.Empty);
            Assert.That(walker.Loops, Is.EqualTo(0));
        }
    }

    /// <summary>
    /// The neighbour query has to return exactly the same set as the linear scan it replaced, while walking
    /// only the spatial-hash buckets around the query point.
    /// </summary>
    public sealed class HumanManagerNeighbourTests
    {
        private GameObject _host;
        private HumanManager _manager;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("HumanManager");
            _manager = _host.AddComponent<HumanManager>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
                Object.DestroyImmediate(_host);
        }

        private List<int> Query(int selfId, Vector2 center, float radius)
        {
            var positions = new List<Vector2>();
            var velocities = new List<Vector2>();
            var ids = new List<int>();
            _manager.GetNeighbors(selfId, center, radius, positions, velocities, ids);

            Assert.That(positions.Count, Is.EqualTo(ids.Count), "Positions and ids must stay aligned.");
            return ids;
        }

        [Test]
        public void GetNeighbors_ReturnsOnlyTheAgentsInsideTheRadius()
        {
            _manager.RegisterAgent(1, Vector2.zero);
            _manager.RegisterAgent(2, new Vector2(1.5f, 0f));
            _manager.RegisterAgent(3, new Vector2(40f, 40f));

            Assert.That(Query(1, Vector2.zero, 2f), Is.EquivalentTo(new[] { 2 }));
        }

        [Test]
        public void GetNeighbors_IgnoresTheQueryingAgentItself()
        {
            _manager.RegisterAgent(7, Vector2.zero);
            _manager.RegisterAgent(8, new Vector2(0.5f, 0f));

            Assert.That(Query(7, Vector2.zero, 3f), Is.EquivalentTo(new[] { 8 }));
        }

        [Test]
        public void GetNeighbors_SeesAgentsAcrossBucketBorders()
        {
            // Two agents a metre apart but on opposite sides of a 4 m cell: both must still be found.
            _manager.RegisterAgent(1, new Vector2(3.9f, 0f));
            _manager.RegisterAgent(2, new Vector2(4.1f, 0f));

            Assert.That(Query(1, new Vector2(3.9f, 0f), 2f), Is.EquivalentTo(new[] { 2 }));
        }

        [Test]
        public void GetNeighbors_DropsAnUnregisteredAgent()
        {
            _manager.RegisterAgent(1, Vector2.zero);
            _manager.RegisterAgent(2, new Vector2(1f, 0f));
            Assert.That(Query(1, Vector2.zero, 2f), Is.EquivalentTo(new[] { 2 }));

            _manager.UnregisterAgent(2);

            Assert.That(Query(1, Vector2.zero, 2f), Is.Empty);
        }

        [Test]
        public void GetNeighbors_MatchesTheLinearScanInACrowd()
        {
            var positions = new List<Vector2>();
            var random = new System.Random(1234);
            for (int index = 0; index < 120; index++)
            {
                var position = new Vector2(
                    (float)(random.NextDouble() * 60.0 - 30.0),
                    (float)(random.NextDouble() * 60.0 - 30.0));
                positions.Add(position);
                _manager.RegisterAgent(index + 1, position);
            }

            foreach (Vector2 center in new[]
                     {
                         Vector2.zero, new Vector2(12f, -7f), new Vector2(-25f, 25f), new Vector2(30f, 30f)
                     })
            {
                var expected = new List<int>();
                for (int index = 0; index < positions.Count; index++)
                {
                    float distance = Vector2.Distance(center, positions[index]);
                    if (distance < 5f && distance > 0.01f)
                        expected.Add(index + 1);
                }

                // Agent 0 is the querying agent, so it is excluded from both sides by id.
                Assert.That(
                    Query(0, center, 5f),
                    Is.EquivalentTo(expected),
                    $"Neighbours around {center} must match a plain distance scan.");
            }
        }
    }
}
