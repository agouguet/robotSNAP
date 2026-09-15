using System.Collections.Generic;
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

        [Test]
        public void CountConsumedCorners_TakesEverythingTheAgentHasReached()
        {
            // The shape the Default scenario ended up following: a kept path re-anchored on the agent still
            // carries the waypoints of the detour it has already taken. Steering to one of them turned the
            // pedestrian around and parked it in a two-metre loop until its route gave up.
            Vector3[] corners =
            {
                new(2.43f, 0f, -0.74f),
                new(1.08f, 0f, -1.33f),
                new(1.25f, 0f, -1.08f),
                new(1.41f, 0f, -1.00f),
                new(8.46f, 0f, -0.02f)
            };

            int consumed = HumanPathTargetSelector.CountConsumedCorners(
                new Vector2(2.43f, -0.74f),
                corners,
                1.5f);

            Assert.That(consumed, Is.EqualTo(3),
                "The anchor and the three reached corners go, the destination stays.");
        }

        [Test]
        public void CountConsumedCorners_KeepsEveryoneTheAgentStillHasToWalkTo()
        {
            Vector3[] corners =
            {
                Vector3.zero,
                new(5f, 0f, 0f),
                new(0f, 0f, -10f)
            };

            Assert.That(
                HumanPathTargetSelector.CountConsumedCorners(new Vector2(0f, 0f), corners, 1.5f),
                Is.Zero,
                "A corner five metres away is still ahead.");
        }

        [Test]
        public void CountConsumedCorners_NeverEatsTheDestination()
        {
            // Every intermediate corner is within reach: the head and the tail must survive, or the caller ends
            // up with a path it cannot steer along at all.
            Vector3[] corners =
            {
                Vector3.zero,
                new(0.4f, 0f, 0f),
                new(0.8f, 0f, 0.2f),
                new(1.1f, 0f, 0.4f)
            };

            Assert.That(
                HumanPathTargetSelector.CountConsumedCorners(new Vector2(0f, 0f), corners, 1.5f),
                Is.EqualTo(corners.Length - 2),
                "Two entries always stay: the anchor and the destination.");
        }

        [Test]
        public void CountConsumedCorners_SurvivesAStraightTwoPointPath()
        {
            Vector3[] corners = { Vector3.zero, new(10f, 0f, 0f) };

            Assert.That(
                HumanPathTargetSelector.CountConsumedCorners(new Vector2(1f, 0f), corners, 1.5f),
                Is.Zero);
        }
    }

    /// <summary>
    /// The avoidance law has to see a conflict coming long before the bodies are close, and both agents have
    /// to choose sides that let them pass: that pair of properties is what the old random, under-1.5 m rule
    /// was missing.
    /// </summary>
    public sealed class HumanAvoidanceTests
    {
        private const float Radius = 0.25f;

        [Test]
        public void Predict_DetectsAFaceToFaceEncounterMetresAway()
        {
            HumanAvoidance.Prediction prediction = HumanAvoidance.Predict(
                Vector2.zero,
                new Vector2(1.2f, 0f),
                Radius,
                new Vector2(3.5f, 0f),
                new Vector2(-1.2f, 0f),
                Radius,
                Vector2.right);

            Assert.That(prediction.IsConflict, Is.True, "Three and a half metres away is already a conflict.");
            Assert.That(prediction.TimeToCollision, Is.EqualTo(1.46f).Within(0.05f));
            Assert.That(prediction.Urgency, Is.GreaterThan(0f));
        }

        [Test]
        public void Predict_IgnoresAGapThatStaysOpen()
        {
            // Passing each other with two metres of clearance: no reason to slow down or steer.
            HumanAvoidance.Prediction prediction = HumanAvoidance.Predict(
                Vector2.zero,
                new Vector2(1.2f, 0f),
                Radius,
                new Vector2(4f, 2.5f),
                new Vector2(-1.2f, 0f),
                Radius,
                Vector2.right);

            Assert.That(prediction.IsConflict, Is.False);
        }

        [Test]
        public void Predict_IgnoresSomebodyWalkingAway()
        {
            HumanAvoidance.Prediction prediction = HumanAvoidance.Predict(
                Vector2.zero,
                Vector2.zero,
                Radius,
                new Vector2(1f, 0f),
                new Vector2(1.2f, 0f),
                Radius,
                Vector2.right);

            Assert.That(prediction.IsConflict, Is.False);
            Assert.That(prediction.TimeToCollision, Is.EqualTo(float.PositiveInfinity));
        }

        [Test]
        public void Predict_KeepsRightForBothAgentsOfAHeadOnEncounter()
        {
            HumanAvoidance.Prediction mine = HumanAvoidance.Predict(
                Vector2.zero,
                new Vector2(1.2f, 0f),
                Radius,
                new Vector2(3f, 0f),
                new Vector2(-1.2f, 0f),
                Radius,
                Vector2.right);

            HumanAvoidance.Prediction theirs = HumanAvoidance.Predict(
                new Vector2(3f, 0f),
                new Vector2(-1.2f, 0f),
                Radius,
                Vector2.zero,
                new Vector2(1.2f, 0f),
                Radius,
                Vector2.left);

            Assert.That(mine.IsConflict, Is.True);
            Assert.That(theirs.IsConflict, Is.True);
            Assert.That(mine.Side, Is.EqualTo(1f), "A perfectly symmetric encounter falls back to keep-right.");
            Assert.That(theirs.Side, Is.EqualTo(1f), "And the other agent does the same, in its own frame.");
        }

        [Test]
        public void Predict_SteersAwayFromWhereTheOtherAgentWillBe()
        {
            // The neighbour walks in from the agent's right: the chosen side must be the left one.
            HumanAvoidance.Prediction prediction = HumanAvoidance.Predict(
                Vector2.zero,
                new Vector2(1.2f, 0f),
                Radius,
                new Vector2(3f, 0.7f),
                new Vector2(-1.2f, 0f),
                Radius,
                Vector2.right);

            Assert.That(prediction.IsConflict, Is.True);
            Assert.That(prediction.Side, Is.EqualTo(-1f));
        }

        [Test]
        public void Predict_GrowsMoreUrgentAsTheGapCloses()
        {
            float far = HumanAvoidance.Predict(
                Vector2.zero,
                new Vector2(1.2f, 0f),
                Radius,
                new Vector2(3.5f, 0f),
                new Vector2(-1.2f, 0f),
                Radius,
                Vector2.right).Urgency;

            float near = HumanAvoidance.Predict(
                Vector2.zero,
                new Vector2(1.2f, 0f),
                Radius,
                new Vector2(2.2f, 0f),
                new Vector2(-1.2f, 0f),
                Radius,
                Vector2.right).Urgency;

            Assert.That(near, Is.GreaterThan(far));
            Assert.That(near, Is.LessThanOrEqualTo(1f));
        }

        [Test]
        public void MostUrgent_KeepsTheWorstConflictOfTheCrowd()
        {
            var positions = new List<Vector2> { new(4f, 0f), new(1.2f, 0f), new(3f, 3f) };
            var velocities = new List<Vector2> { new(-1.2f, 0f), new(-1.2f, 0f), Vector2.zero };

            HumanAvoidance.Prediction worst = HumanAvoidance.MostUrgent(
                Vector2.zero,
                new Vector2(1.2f, 0f),
                Radius,
                Vector2.right,
                positions,
                velocities,
                Radius);

            Assert.That(worst.IsConflict, Is.True);
            Assert.That(worst.TimeToCollision, Is.EqualTo(0.5f).Within(0.05f), "The closest walker wins.");
        }

        [Test]
        public void RightOf_IsPerpendicularToTheHeading()
        {
            Vector2 right = HumanAvoidance.RightOf(new Vector2(1f, 0f));

            Assert.That(Vector2.Dot(right, new Vector2(1f, 0f)), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(right.magnitude, Is.EqualTo(1f).Within(0.0001f));
        }
    }
}
