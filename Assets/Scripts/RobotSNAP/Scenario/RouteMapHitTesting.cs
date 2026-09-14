using System.Collections.Generic;
using UnityEngine;

namespace RobotSNAP.Core.Scenario
{
    /// <summary>
    /// Pointer hit-testing helpers used by the scenario route map.
    /// Every position is expressed in canvas local space so the maths stays testable outside the Editor.
    /// </summary>
    public static class RouteMapHitTesting
    {
        /// <summary>Returns the index of the closest point within <paramref name="radius"/>, or -1 when none matches.</summary>
        public static int FindPoint(IReadOnlyList<Vector2> canvasPoints, Vector2 cursor, float radius)
        {
            if (canvasPoints == null)
                return -1;

            int bestIndex = -1;
            float bestDistance = float.MaxValue;
            for (int index = 0; index < canvasPoints.Count; index++)
            {
                float distance = Vector2.Distance(canvasPoints[index], cursor);
                if (distance <= radius && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = index;
                }
            }

            return bestIndex;
        }

        /// <summary>Returns the index of the segment ending at the closest edge within <paramref name="radius"/>, or -1.</summary>
        public static int FindSegment(IReadOnlyList<Vector2> canvasPoints, Vector2 cursor, float radius)
        {
            if (canvasPoints == null)
                return -1;

            int bestSegment = -1;
            float bestDistance = float.MaxValue;
            for (int index = 1; index < canvasPoints.Count; index++)
            {
                float distance = DistanceToSegment(cursor, canvasPoints[index - 1], canvasPoints[index]);
                if (distance <= radius && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestSegment = index;
                }
            }

            return bestSegment;
        }

        public static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Mathf.Epsilon)
                return Vector2.Distance(point, start);

            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return Vector2.Distance(point, start + t * segment);
        }

        /// <summary>Human readable name of a route point, shared by the map labels and the coordinates panel.</summary>
        public static string PointLabel(int index) => index <= 0 ? "Start" : $"Objective {index}";
    }
}
