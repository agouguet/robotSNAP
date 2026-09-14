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
}
