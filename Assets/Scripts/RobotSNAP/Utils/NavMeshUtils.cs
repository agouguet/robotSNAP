using UnityEngine;
using UnityEngine.AI;

namespace RobotSNAP.Utils
{
    public static class NavMeshUtils
    {
        // Returns -1 if no complete path, otherwise the path length
        public static float GetNavMeshPathLength(Vector3 start, Vector3 end, NavMeshQueryFilter filter)
        {
            NavMeshPath p = new NavMeshPath();
            bool found = NavMesh.CalculatePath(start, end, filter, p);
            if (!found || p.status != NavMeshPathStatus.PathComplete) return -1f;
            float total = 0f;
            for (int i = 1; i < p.corners.Length; i++) total += Vector3.Distance(p.corners[i - 1], p.corners[i]);
            return total;
        }

        // Samples a random point on the navmesh around center within radius using the provided filter.
        // Returns center if none found.
        public static Vector3 GetRandomPointOnNavMesh(Vector3 center, float radius, NavMeshQueryFilter filter)
        {
            int maxAttempts = 10;
            for (int i = 0; i < maxAttempts; i++)
            {
                Vector3 randomPoint = center + Random.insideUnitSphere * radius;
                if (NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, radius, filter))
                {
                    return hit.position;
                }
            }
            return center;
        }

        // Finds a point along a NavMeshPath at approximately targetDistance measured from startPos.
        // If path is empty returns Vector3.zero.
        public static Vector3 FindPointAlongPathAtDistance(NavMeshPath path, float targetDistance, Vector3 startPos)
        {
            if (path == null || path.corners.Length == 0) return Vector3.zero;

            float accumulatedDistance = 0f;
            Vector3 previousPoint = startPos;

            for (int i = 0; i < path.corners.Length; i++)
            {
                Vector3 currentPoint = path.corners[i];
                float segmentLength = Vector3.Distance(previousPoint, currentPoint);

                if (accumulatedDistance + segmentLength >= targetDistance)
                {
                    float distanceNeeded = targetDistance - accumulatedDistance;
                    Vector3 direction = (currentPoint - previousPoint).normalized;
                    return previousPoint + direction * distanceNeeded;
                }

                accumulatedDistance += segmentLength;
                previousPoint = currentPoint;
            }
            return path.corners[^1];
        }
    }
}
