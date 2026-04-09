using UnityEngine;
using UnityEngine.AI;

namespace RobotSNAP.Utils
{
    public static class NavMeshUtils
    {
        // Returns -1 if no complete path, otherwise the path length
        public static float GetNavMeshPathLength(Vector3 start, Vector3 end, NavMeshQueryFilter filter)
        {
            return GetNavMeshPathLengthSimple(start, end);
            NavMeshPath p = new NavMeshPath();
            bool found = NavMesh.CalculatePath(start, end, filter, p);
            if (!found || p.status != NavMeshPathStatus.PathComplete) return -1f;
            float total = 0f;
            for (int i = 1; i < p.corners.Length; i++) total += Vector3.Distance(p.corners[i - 1], p.corners[i]);
            return total;
        }

        public static float GetNavMeshPathLengthSimple(Vector3 start, Vector3 end)
        {
            NavMeshPath p = new NavMeshPath();
            bool found = NavMesh.CalculatePath(start, end, NavMesh.AllAreas, p);
            if (!found || p.status != NavMeshPathStatus.PathComplete) return -1f;
            float total = 0f;
            for (int i = 1; i < p.corners.Length; i++) total += Vector3.Distance(p.corners[i - 1], p.corners[i]);
            return total;
        }


        // Samples a random point on the navmesh around center within radius using the provided filter.
        // Returns center if none found.
        public static Vector3 GetRandomPointOnNavMesh(Vector3 center, float radius, NavMeshQueryFilter filter, int maxAttempts = 10)
        {
            // CORRECTION: Si l'agentTypeID est invalide, utiliser le default (0)
            if (filter.agentTypeID < 0)
            {
                Debug.LogWarning($"AgentTypeID invalide ({filter.agentTypeID}), correction vers 0");
                filter.agentTypeID = 0;
            }
            
            // CORRECTION: Si areaMask est 0 ou invalide, utiliser AllAreas
            if (filter.areaMask == 0)
            {
                Debug.LogWarning($"AreaMask = 0, utilisation de AllAreas");
                filter.areaMask = NavMesh.AllAreas;
            }
            
            for (int i = 0; i < maxAttempts; i++)
            {
                Vector2 randomCircle = Random.insideUnitCircle * radius;
                Vector3 randomPoint = new Vector3(
                    center.x + randomCircle.x,
                    center.y,
                    center.z + randomCircle.y
                );
                
                if (NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, radius, filter))
                {
                    return hit.position;
                }
            }
            
            // Fallback: chercher sans filtre
            if (NavMesh.SamplePosition(center, out NavMeshHit fallbackHit, radius * 2f, NavMesh.AllAreas))
            {
                return fallbackHit.position;
            }
            
            return center;
        }

        public static Vector3 GetRandomPointOnNavMeshSimple(Vector3 center, float radius, int maxAttempts = 30)
        {
            for (int i = 0; i < maxAttempts; i++)
            {
                // Générer un point aléatoire en 2D (X/Z)
                Vector2 randomOffset = Random.insideUnitCircle * radius;
                Vector3 randomPoint = new Vector3(
                    center.x + randomOffset.x,
                    center.y + 0.5f, // Légèrement au-dessus du sol
                    center.z + randomOffset.y
                );
                
                // Utiliser NavMesh.AllAreas (ignore tous les filtres)
                if (NavMesh.SamplePosition(randomPoint, out NavMeshHit hit, radius * 2f, NavMesh.AllAreas))
                {
                    return hit.position;
                }
            }
            
            // Dernier recours : retourner le centre
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
