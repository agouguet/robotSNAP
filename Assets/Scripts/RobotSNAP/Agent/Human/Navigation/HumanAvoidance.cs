using UnityEngine;

namespace RobotSNAP.Agents
{
    /// <summary>
    /// Reciprocal, anticipatory collision avoidance between pedestrians.
    ///
    /// The social-force repulsion of the movement controller only bites once two agents are almost touching:
    /// two humans walked into each other and then crawled, neither able to decide who goes where. This is the
    /// classic collision-cone answer (Best &amp; Fitch, "A collision cone approach to the reciprocal
    /// n-body collision avoidance problem", 2015): look ahead over a horizon, measure how close the predicted
    /// trajectories come, and steer aside early. Both agents apply the same rule, so a face-to-face encounter
    /// resolves instead of mirroring itself into a standstill, and a head-on one is settled by a keep-right
    /// convention rather than by noise.
    ///
    /// The prediction is pure, so the behaviour can be tested without a scene.
    /// </summary>
    public static class HumanAvoidance
    {
        /// <summary>How far ahead a conflict is anticipated, in seconds.</summary>
        public const float DefaultHorizon = 3f;

        /// <summary>
        /// Clearance kept on top of the two body radii, in metres. Pedestrians hold more than a body width
        /// between them: the bodies alone are the physical limit, not the distance people walk at.
        /// </summary>
        public const float DefaultMargin = 0.3f;

        /// <summary>
        /// Predicted gap under which a conflict is worth steering for, in metres, measured from the personal
        /// space rather than from the bodies. Together with <see cref="DefaultMargin"/> it fixes the distance
        /// at which two agents start choosing a side: 1.2 m centre to centre for the shipped body radius.
        /// </summary>
        public const float ConflictGap = 0.3f;

        /// <summary>
        /// Under this lateral distance between the two predicted positions the encounter counts as head-on,
        /// and the keep-right convention decides the side. Without it, a perfectly symmetric approach has no
        /// preferred side and both agents dither.
        /// </summary>
        private const float SymmetricLateralTolerance = 0.1f;

        /// <summary>What is about to happen with one other pedestrian.</summary>
        public readonly struct Prediction
        {
            public Prediction(bool isConflict, float timeToCollision, float closestApproach, float urgency, float side)
            {
                IsConflict = isConflict;
                TimeToCollision = timeToCollision;
                ClosestApproach = closestApproach;
                Urgency = urgency;
                Side = side;
            }

            /// <summary>No conflict: the trajectories keep more than a body apart.</summary>
            public static Prediction None => new(false, float.PositiveInfinity, float.PositiveInfinity, 0f, 1f);

            public bool IsConflict { get; }

            /// <summary>Seconds until the predicted closest approach.</summary>
            public float TimeToCollision { get; }

            /// <summary>Distance between the two bodies at that closest approach.</summary>
            public float ClosestApproach { get; }

            /// <summary>0 when there is room, 1 when the two bodies are about to overlap.</summary>
            public float Urgency { get; }

            /// <summary>+1 to steer to the right-hand side of the heading, -1 to the left.</summary>
            public float Side { get; }
        }

        /// <summary>
        /// Conflict between one agent and one neighbour. <paramref name="heading"/> is the direction the agent
        /// wants to walk: the side is expressed relative to it, never to the world, so two agents meeting each
        /// other both step aside consistently.
        /// </summary>
        public static Prediction Predict(
            Vector2 position,
            Vector2 velocity,
            float radius,
            Vector2 otherPosition,
            Vector2 otherVelocity,
            float otherRadius,
            Vector2 heading,
            float horizon = DefaultHorizon,
            float margin = DefaultMargin)
        {
            Vector2 offset = otherPosition - position;
            Vector2 relative = otherVelocity - velocity;
            float radiusSum = Mathf.Max(0f, radius) + Mathf.Max(0f, otherRadius);
            float gapAllowed = radiusSum + Mathf.Max(0f, margin);

            float time = 0f;
            float closest = offset.magnitude;
            bool closing = false;
            if (relative.sqrMagnitude > 0.000001f)
            {
                float approach = -Vector2.Dot(offset, relative) / relative.sqrMagnitude;
                if (approach > 0f)
                {
                    time = approach;

                    float sample = Mathf.Min(time, Mathf.Max(0f, horizon));
                    closest = (offset + relative * sample).magnitude;
                    closing = true;
                }
            }

            // A neighbour walking away, or at the very same pace, is not on a collision course: the two
            // trajectories never get closer than they are now, so there is nothing to step aside for.
            if (!closing)
                return Prediction.None;

            if (time > horizon)
                return Prediction.None;

            float gap = closest - gapAllowed;
            if (gap > ConflictGap)
                return Prediction.None;

            float urgencySpace = Mathf.Clamp01(1f - gap / ConflictGap);
            float urgencyTime = horizon <= 0f ? 1f : Mathf.Clamp01(1f - time / horizon);
            float urgency = Mathf.Clamp01(urgencySpace * Mathf.Lerp(0.35f, 1f, urgencyTime));

            return new Prediction(true, time, closest, urgency, ChooseSide(position, velocity, otherPosition, otherVelocity, heading, time));
        }

        /// <summary>Most urgent conflict among a neighbour set, or <see cref="Prediction.None"/>.</summary>
        public static Prediction MostUrgent(
            Vector2 position,
            Vector2 velocity,
            float radius,
            Vector2 heading,
            System.Collections.Generic.IReadOnlyList<Vector2> neighbourPositions,
            System.Collections.Generic.IReadOnlyList<Vector2> neighbourVelocities,
            float neighbourRadius,
            float horizon = DefaultHorizon,
            float margin = DefaultMargin)
        {
            Prediction worst = Prediction.None;
            if (neighbourPositions == null)
                return worst;

            int count = Mathf.Min(neighbourPositions.Count, neighbourVelocities?.Count ?? 0);
            for (int index = 0; index < count; index++)
            {
                Prediction prediction = Predict(
                    position,
                    velocity,
                    radius,
                    neighbourPositions[index],
                    neighbourVelocities[index],
                    neighbourRadius,
                    heading,
                    horizon,
                    margin);
                if (!prediction.IsConflict || prediction.Urgency <= worst.Urgency)
                    continue;

                worst = prediction;
            }

            return worst;
        }

        /// <summary>Unit vector pointing to the right-hand side of a heading (world axes of x/z).</summary>
        public static Vector2 RightOf(Vector2 heading) =>
            heading.sqrMagnitude < 0.0001f
                ? new Vector2(1f, 0f)
                : new Vector2(-heading.y, heading.x).normalized;

        /// <summary>
        /// Side to steer towards: away from where the other agent will be, and to the right when the two
        /// predicted positions are too close to tell. Both agents run this same rule, which is what makes a
        /// head-on encounter resolve instead of drifting into a mirrored standstill.
        /// </summary>
        private static float ChooseSide(
            Vector2 position,
            Vector2 velocity,
            Vector2 otherPosition,
            Vector2 otherVelocity,
            Vector2 heading,
            float time)
        {
            Vector2 myFuture = position + velocity * time;
            Vector2 otherFuture = otherPosition + otherVelocity * time;
            float lateral = Vector2.Dot(RightOf(heading), otherFuture - myFuture);

            return Mathf.Abs(lateral) > SymmetricLateralTolerance ? -Mathf.Sign(lateral) : 1f;
        }
    }
}
