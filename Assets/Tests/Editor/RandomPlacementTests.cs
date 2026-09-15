using System.Collections.Generic;
using NUnit.Framework;
using RobotSNAP.Core.Scenario;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// Contract of <see cref="RandomPlacement"/>.
    ///
    /// The randomness is injected, so every case below replays a fixed suite of points and asserts on exact
    /// values instead of on a distribution: the interesting behaviour is what happens to a draw that lands in
    /// a wall, to a zone that is entirely made of wall, and to the degenerate zones an authored file can carry.
    /// </summary>
    public sealed class RandomPlacementTests
    {
        /// <summary>Horizontal centre of the zone used by the cases below.</summary>
        private static readonly Vector2 ZoneCentre = new Vector2(1f, 2f);

        /// <summary>
        /// Zone of the cases below: 6 m x 6 m around <see cref="ZoneCentre"/>, with the zero height an authored
        /// zone carries (only x and z are written in the YAML). Such a zone is sampled, not treated as empty.
        /// </summary>
        private static Bounds Zone(float sizeX = 6f, float sizeZ = 6f) =>
            new Bounds(new Vector3(ZoneCentre.x, 0.5f, ZoneCentre.y), new Vector3(sizeX, 0f, sizeZ));

        [Test]
        public void SampleWalkable_SkipsTheDrawsLandedInAWall()
        {
            var inWall = new Vector2(-4f, -4f);
            var onFloor = new Vector2(2f, 1f);
            var sampler = new ScriptedSampler(inWall, onFloor);

            Vector2 result = RandomPlacement.SampleWalkable(
                Zone(),
                sampler.Next,
                point => point == onFloor,
                point => point + Vector2.one);

            Assert.That(result, Is.EqualTo(onFloor), "The first navigable draw is kept as it is.");
            Assert.That(sampler.Calls, Is.EqualTo(2), "A draw in the wall is followed by another attempt.");
        }

        [Test]
        public void SampleWalkable_KeepsADrawAcceptedOnTheFirstAttempt()
        {
            var onFloor = new Vector2(2f, 1f);
            var sampler = new ScriptedSampler(onFloor);

            Vector2 result = RandomPlacement.SampleWalkable(
                Zone(),
                sampler.Next,
                _ => true,
                point => point + Vector2.one);

            Assert.That(result, Is.EqualTo(onFloor));
            Assert.That(sampler.Calls, Is.EqualTo(1), "An accepted draw is never re-drawn nor projected.");
        }

        [Test]
        public void SampleWalkable_ProjectsTheLastDrawWhenTheWholeZoneIsWall()
        {
            var draws = new Vector2[] { new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(2f, 2f) };
            var sampler = new ScriptedSampler(draws);
            var projected = new List<Vector2>();

            Vector2 result = RandomPlacement.SampleWalkable(
                Zone(),
                sampler.Next,
                _ => false,
                point =>
                {
                    projected.Add(point);
                    return point + Vector2.right;
                },
                attempts: draws.Length);

            Assert.That(sampler.Calls, Is.EqualTo(draws.Length), "Every attempt is spent before giving up.");
            Assert.That(projected.Count, Is.EqualTo(1), "Only the draw the caller ends up with is projected.");
            Assert.That(projected[0], Is.EqualTo(draws[draws.Length - 1]), "The projection receives the last draw.");
            Assert.That(result, Is.EqualTo(draws[draws.Length - 1] + Vector2.right));
        }

        [Test]
        public void SampleWalkable_BoundsTheNumberOfDrawsByAttempts()
        {
            var sampler = new ScriptedSampler(new Vector2(0f, 0f));

            RandomPlacement.SampleWalkable(Zone(), sampler.Next, _ => false, point => point, attempts: 5);

            Assert.That(sampler.Calls, Is.EqualTo(5), "At most one draw per attempt, never more.");
        }

        [Test]
        public void SampleWalkable_UsesTheDefaultAttemptBudget()
        {
            var sampler = new ScriptedSampler(new Vector2(0f, 0f));

            RandomPlacement.SampleWalkable(Zone(), sampler.Next, _ => false, point => point);

            Assert.That(
                sampler.Calls,
                Is.EqualTo(RandomPlacement.DefaultAttempts),
                "The default budget is the documented number of attempts.");
        }

        [Test]
        public void SampleWalkable_CountsAZeroOrNegativeBudgetAsOneAttempt()
        {
            var sampler = new ScriptedSampler(new Vector2(0f, 0f));

            RandomPlacement.SampleWalkable(Zone(), sampler.Next, _ => false, point => point, attempts: 0);

            Assert.That(sampler.Calls, Is.EqualTo(1), "A zero budget still gives the zone one chance.");

            sampler.ResetCalls();
            RandomPlacement.SampleWalkable(Zone(), sampler.Next, _ => false, point => point, attempts: -7);

            Assert.That(sampler.Calls, Is.EqualTo(1), "A negative budget is one attempt, not an endless loop.");
        }

        [Test]
        public void SampleWalkable_ProjectsTheCentreOfAZoneWithoutExtent()
        {
            var sampler = new ScriptedSampler(new Vector2(42f, 42f));
            var bounds = new Bounds(new Vector3(3f, 0.5f, 4f), Vector3.zero);
            var projected = new List<Vector2>();

            Vector2 result = RandomPlacement.SampleWalkable(
                bounds,
                sampler.Next,
                _ => false,
                point =>
                {
                    projected.Add(point);
                    return point;
                });

            Assert.That(sampler.Calls, Is.EqualTo(0), "A zone with no footprint has nothing to sample.");
            Assert.That(projected.Count, Is.EqualTo(1), "The centre is still handed to the projection.");
            Assert.That(projected[0], Is.EqualTo(new Vector2(3f, 4f)));
            Assert.That(result, Is.EqualTo(new Vector2(3f, 4f)));
        }

        [Test]
        public void SampleWalkable_NeverSamplesAZoneFlattenedOnOneAxis()
        {
            var sampler = new ScriptedSampler(new Vector2(42f, 42f));
            var bounds = new Bounds(new Vector3(3f, 0.5f, 4f), new Vector3(6f, 0f, 0f));

            Vector2 result = RandomPlacement.SampleWalkable(
                bounds,
                sampler.Next,
                _ => false,
                point => point + Vector2.up);

            Assert.That(sampler.Calls, Is.EqualTo(0), "A zone flat on one axis cannot be sampled in two.");
            Assert.That(result, Is.EqualTo(new Vector2(3f, 5f)));
        }

        [Test]
        public void SampleWalkable_FallsBackOnTheCentreWhenSomethingIsMissing()
        {
            var sampler = new ScriptedSampler(new Vector2(42f, 42f));
            Bounds bounds = Zone();

            Assert.That(
                RandomPlacement.SampleWalkable(bounds, sampler.Next, null, point => point + Vector2.one),
                Is.EqualTo(ZoneCentre),
                "Without a walkability test there is nothing to accept a draw with, so the centre answers.");
            Assert.That(
                RandomPlacement.SampleWalkable(bounds, sampler.Next, _ => false, null),
                Is.EqualTo(ZoneCentre),
                "Without a projection a refused draw has nowhere to go.");
            Assert.That(
                RandomPlacement.SampleWalkable(bounds, null, _ => true, point => point + Vector2.one),
                Is.EqualTo(ZoneCentre),
                "Without a sampler there is nothing to draw with.");
            Assert.That(sampler.Calls, Is.EqualTo(0), "A missing argument means no draw is attempted at all.");
        }

        [Test]
        public void SampleWalkable_HandsTheSamplerTheZoneItWasGiven()
        {
            Bounds bounds = Zone();
            var accepted = new Vector2(bounds.min.x, bounds.min.z);
            var sampler = new ScriptedSampler(accepted);

            Vector2 result = RandomPlacement.SampleWalkable(bounds, sampler.Next, _ => true, point => point);

            Assert.That(sampler.LastBounds, Is.EqualTo(bounds), "The sampler draws inside the zone it is given.");
            Assert.That(result, Is.EqualTo(accepted));
            Assert.That(sampler.LastBounds.center.y, Is.EqualTo(0.5f), "Only XZ of the zone is used by the draw.");
        }

        /// <summary>Sampler replaying a fixed suite of points, and recording how it was called.</summary>
        private sealed class ScriptedSampler
        {
            private readonly Queue<Vector2> _points;

            public ScriptedSampler(params Vector2[] points) => _points = new Queue<Vector2>(points);

            /// <summary>How many draws were served so far.</summary>
            public int Calls { get; private set; }

            /// <summary>Last zone the sampler was handed.</summary>
            public Bounds LastBounds { get; private set; }

            /// <summary>Next scripted point; once the suite runs out, the origin keeps coming back.</summary>
            public Vector2 Next(Bounds bounds)
            {
                Calls++;
                LastBounds = bounds;
                return _points.Count > 0 ? _points.Dequeue() : Vector2.zero;
            }

            public void ResetCalls() => Calls = 0;
        }
    }
}
