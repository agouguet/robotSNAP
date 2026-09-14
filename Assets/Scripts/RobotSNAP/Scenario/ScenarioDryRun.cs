using System.Collections.Generic;
using UnityEngine;

namespace RobotSNAP.Core.Scenario
{
    /// <summary>How serious a dry-run finding is.</summary>
    public enum DryRunSeverity
    {
        Ok,
        Warning,
        Error
    }

    /// <summary>One line of the dry run: what was measured, and whether it is a problem.</summary>
    public readonly struct DryRunFinding
    {
        public DryRunFinding(DryRunSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }

        public DryRunSeverity Severity { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Estimates a scenario before it is saved: path lengths, walking times and the mistakes that only
    /// show up once the simulation runs (a two-point route on the same spot, a zero speed, a mission
    /// longer than any reviewer will wait for).
    ///
    /// Kept free of Unity objects so a whole scenario can be analysed without loading a scene.
    /// </summary>
    public static class ScenarioDryRun
    {
        /// <summary>Shorter than this and the route is a standing agent rather than a walk.</summary>
        public const float MinimumUsefulLength = 0.5f;

        /// <summary>Above this estimated walking time the route is probably a mistake.</summary>
        public const float LongDurationSeconds = 600f;

        /// <summary>Two consecutive points closer than this count as the same spot.</summary>
        public const float DuplicatePointTolerance = 0.01f;

        /// <summary>An ordered route with the speed it is walked at.</summary>
        public readonly struct Route
        {
            public Route(string name, IReadOnlyList<Vector2> points, float speed)
            {
                Name = name;
                Points = points;
                Speed = speed;
            }

            public string Name { get; }
            public IReadOnlyList<Vector2> Points { get; }
            public float Speed { get; }
        }

        /// <summary>Total planar length of a polyline.</summary>
        public static float PathLength(IReadOnlyList<Vector2> points)
        {
            if (points == null || points.Count < 2)
                return 0f;

            float length = 0f;
            for (int index = 1; index < points.Count; index++)
                length += Vector2.Distance(points[index - 1], points[index]);
            return length;
        }

        public static List<DryRunFinding> Analyse(IEnumerable<Route> routes)
        {
            var findings = new List<DryRunFinding>();
            if (routes == null)
                return findings;

            int index = 0;
            foreach (Route route in routes)
            {
                index++;
                string name = string.IsNullOrWhiteSpace(route.Name) ? $"Route {index}" : route.Name.Trim();

                if (route.Points == null || route.Points.Count < 2)
                {
                    findings.Add(new DryRunFinding(
                        DryRunSeverity.Error,
                        $"{name}: needs a start point and at least one objective."));
                    continue;
                }

                if (route.Speed <= 0.01f)
                {
                    findings.Add(new DryRunFinding(
                        DryRunSeverity.Error,
                        $"{name}: speed must be greater than zero."));
                    continue;
                }

                if (HasDuplicateConsecutivePoint(route.Points))
                {
                    findings.Add(new DryRunFinding(
                        DryRunSeverity.Error,
                        $"{name}: two consecutive points sit on the same spot."));
                }

                float length = PathLength(route.Points);
                float duration = length / route.Speed;
                if (length < MinimumUsefulLength)
                {
                    findings.Add(new DryRunFinding(
                        DryRunSeverity.Warning,
                        $"{name}: only {length:0.##} m long, the agent will barely move."));
                }
                else if (duration > LongDurationSeconds)
                {
                    findings.Add(new DryRunFinding(
                        DryRunSeverity.Warning,
                        $"{name}: {length:0.#} m is about {duration:0} s at {route.Speed:0.##} m/s."));
                }
                else
                {
                    findings.Add(new DryRunFinding(
                        DryRunSeverity.Ok,
                        $"{name}: {length:0.#} m, about {duration:0.#} s at {route.Speed:0.##} m/s."));
                }
            }

            return findings;
        }

        /// <summary>One-line headline of the dry run: how many routes, and the longest walk.</summary>
        public static string Describe(IReadOnlyList<Route> routes)
        {
            if (routes == null || routes.Count == 0)
                return "Dry run: no route to walk.";

            float longest = 0f;
            float speedOfLongest = 1f;
            foreach (Route route in routes)
            {
                float length = PathLength(route.Points);
                if (length <= longest)
                    continue;
                longest = length;
                speedOfLongest = route.Speed > 0.01f ? route.Speed : 1f;
            }

            if (longest <= 0f)
                return $"Dry run: {routes.Count} route(s), nothing to walk.";

            return $"Dry run: {routes.Count} route(s), longest {longest:0.#} m " +
                   $"≈ {longest / speedOfLongest:0.#} s at {speedOfLongest:0.##} m/s.";
        }

        private static bool HasDuplicateConsecutivePoint(IReadOnlyList<Vector2> points)
        {
            for (int index = 1; index < points.Count; index++)
            {
                if (Vector2.Distance(points[index - 1], points[index]) < DuplicatePointTolerance)
                    return true;
            }
            return false;
        }
    }
}
