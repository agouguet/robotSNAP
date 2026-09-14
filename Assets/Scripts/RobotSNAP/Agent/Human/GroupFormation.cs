using System;
using System.Collections.Generic;
using UnityEngine;

namespace RobotSNAP.Agents
{
    /// <summary>
    /// Formation slots used when several humans walk together.
    /// Slots are expressed in the leader's local frame: +x is the leader's right, +y is its forward.
    /// The leader always owns slot zero.
    /// </summary>
    public static class GroupFormation
    {
        /// <summary>Two abreast, following rows behind: the usual pedestrian layout.</summary>
        public const string PairFormation = "pair";

        /// <summary>Single file, one behind the other: for narrow corridors and doorways.</summary>
        public const string ColumnFormation = "column";

        /// <summary>Shoulder to shoulder on one row: the natural way three or four friends chat while walking.</summary>
        public const string RowFormation = "row";

        /// <summary>Open V shape: the leader at the apex, the others trailing behind on both sides.</summary>
        public const string WedgeFormation = "wedge";

        /// <summary>Loose disc, kept tight around the leader: reads as a natural chatting group.</summary>
        public const string ClusterFormation = "cluster";

        public const string DefaultFormation = PairFormation;

        /// <summary>
        /// Lateral spread of a wedge relative to the requested spacing.
        /// A pedestrian V is much wider than it is deep: 0.85 opens it to roughly 40° per side,
        /// which reads as a group walking together instead of a tight queue.
        /// </summary>
        private const float WedgeLateralFactor = 0.85f;

        /// <summary>Depth of the wedge behind the apex, relative to the requested spacing.</summary>
        private const float WedgeDepthFactor = 0.7f;

        /// <summary>Trailing stagger of the outermost members of an abreast row.</summary>
        private const float RowStaggerFactor = 0.25f;

        /// <summary>
        /// Floor of the SFM speed ramp (see <c>SFMController.ComputeVelocity</c>):
        /// below <c>slowDownDistance</c> the desired speed is lerped from 20% to 100%.
        /// Mirrored here so the formation can invert that curve.
        /// </summary>
        public const float SlowDownFloor = 0.2f;

        /// <summary>Golden angle, used to spread a cluster without visible rows.</summary>
        private const float GoldenAngle = 2.399963f;

        public static string Normalize(string formation)
        {
            if (string.IsNullOrWhiteSpace(formation))
                return DefaultFormation;

            switch (formation.Trim().ToLowerInvariant())
            {
                case "column":
                case "line":      // legacy name for a single file
                case "file":
                case "single":
                    return ColumnFormation;
                case "wedge":
                case "v":
                case "vee":
                    return WedgeFormation;
                case "row":
                case "abreast":
                case "side":
                case "side-by-side":
                case "sidebyside":
                case "shoulder":
                    return RowFormation;
                case "cluster":
                case "circle":
                case "disc":
                case "loose":
                    return ClusterFormation;
                default:
                    return DefaultFormation;
            }
        }

        public static List<Vector2> CreateSlots(int count, float spacing, string formation)
        {
            var slots = new List<Vector2>(Mathf.Max(0, count));
            if (count <= 0)
                return slots;

            float step = Mathf.Max(0.2f, spacing);
            slots.Add(Vector2.zero);
            string layout = Normalize(formation);

            for (int index = 1; index < count; index++)
            {
                switch (layout)
                {
                    case ColumnFormation:
                        slots.Add(new Vector2(0f, -index * step));
                        continue;

                    case WedgeFormation:
                    {
                        int row = (index + 1) / 2;
                        float side = index % 2 == 1 ? -1f : 1f;
                        slots.Add(new Vector2(
                            side * row * step * WedgeLateralFactor,
                            -row * step * WedgeDepthFactor));
                        continue;
                    }

                    case RowFormation:
                    {
                        // Everyone on the leader's row, alternating sides. The outermost members
                        // trail slightly so the line reads as a loose arc rather than a rigid bar.
                        int rank = (index + 1) / 2;
                        float side = index % 2 == 1 ? 1f : -1f;
                        slots.Add(new Vector2(side * rank * step, -(rank - 1) * step * RowStaggerFactor));
                        continue;
                    }

                    case ClusterFormation:
                    {
                        float radius = Mathf.Min(step * 1.6f, step * 0.9f * Mathf.Sqrt(index));
                        float angle = index * GoldenAngle;
                        slots.Add(new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius));
                        continue;
                    }

                    default:
                    {
                        // Pair: the second member walks beside the leader, then rows of two behind.
                        int row = index / 2;
                        float side = index % 2 == 1 ? 1f : -1f;
                        slots.Add(new Vector2(side * step * 0.5f, -row * step));
                        continue;
                    }
                }
            }

            return slots;
        }

        /// <summary>
        /// Rotates a leader-local offset into world space for the given yaw (radians around Y).
        /// Local axes are +x to the leader's right and +y along the leader's forward, and the heading
        /// is <c>atan2(forward.x, forward.z)</c>, so the forward axis maps to <c>(sin, cos)</c> and the
        /// right axis to <c>(cos, -sin)</c>. Rotating the other way mirrors every asymmetric formation.
        /// </summary>
        public static Vector2 Rotate(Vector2 offset, float yawRadians)
        {
            float sin = Mathf.Sin(yawRadians);
            float cos = Mathf.Cos(yawRadians);
            return new Vector2(offset.x * cos + offset.y * sin, -offset.x * sin + offset.y * cos);
        }

        /// <summary>
        /// Distance at which a follower must aim, ahead of its slot, to hold the formation.
        /// The SFM regulates its speed from the distance left to the goal, so aiming straight at the
        /// slot makes every follower creep and settle roughly one <paramref name="slowDownDistance"/>
        /// behind it — the visible gap between the leader and the rest of the group. Aiming the same
        /// distance ahead of the slot inverts the curve: the follower slows down exactly onto its slot.
        /// </summary>
        public static float RequiredGoalDistance(float formationSpeed, float cruiseSpeed, float slowDownDistance)
        {
            if (slowDownDistance <= 0f || cruiseSpeed <= 0.01f)
                return 0f;

            float ratio = Mathf.Clamp01(formationSpeed / cruiseSpeed);
            if (ratio <= SlowDownFloor)
                return 0f;

            return slowDownDistance * Mathf.Clamp01((ratio - SlowDownFloor) / (1f - SlowDownFloor));
        }

        /// <summary>
        /// Turns <paramref name="currentHeading"/> towards <paramref name="targetHeading"/> by at most
        /// <paramref name="maxTurnRate"/> radians per second, taking the short way around ±π.
        /// A group whose frame snaps to the leader's instantaneous heading swings every follower across
        /// a wide arc as soon as the leader turns; a bounded turn rate keeps that pivot readable.
        /// </summary>
        public static float SteadyHeading(float currentHeading, float targetHeading, float maxTurnRate, float deltaTime)
        {
            if (maxTurnRate <= 0f || deltaTime <= 0f)
                return currentHeading;

            const float twoPi = 2f * Mathf.PI;
            float delta = Mathf.Repeat(targetHeading - currentHeading + Mathf.PI, twoPi) - Mathf.PI;
            float maxStep = maxTurnRate * deltaTime;
            return currentHeading + Mathf.Clamp(delta, -maxStep, maxStep);
        }
    }
}
