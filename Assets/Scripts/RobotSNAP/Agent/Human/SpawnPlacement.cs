using UnityEngine;
using UnityEngine.AI;

namespace RobotSNAP.Agents
{
    /// <summary>
    /// Turns an authored spawn position into a walkable one.
    /// A group laid out near the edge of an environment would otherwise place members outside the
    /// walkable area, where they can never follow the leader — but a scene without any NavMesh must
    /// still honour the authored formation instead of stacking every member on the anchor.
    /// </summary>
    public static class SpawnPlacement
    {
        /// <summary>Radius used when a plain anchor has to be pulled out of a wall or an obstacle.</summary>
        public const float AnchorSearchRadius = 3f;

        /// <summary>
        /// Projects a formation slot while keeping it within <paramref name="maxDistance"/> of its anchor.
        /// A plain projection is free to slide a member several metres away when the slot lands inside a
        /// wall, which is exactly what breaks up a group at spawn: instead the offset is walked back
        /// towards the anchor until it finds walkable ground.
        /// </summary>
        public static Vector2 ProjectWithin(Vector2 candidate, Vector2 anchor, float maxDistance)
        {
            float limit = Mathf.Max(0.05f, maxDistance);
            if (TryProjectWithin(candidate, anchor, limit, out Vector2 projected))
                return projected;

            Vector2 offset = candidate - anchor;
            for (float scale = 0.75f; scale > 0.05f; scale -= 0.25f)
            {
                if (TryProjectWithin(anchor + offset * scale, anchor, limit, out projected))
                    return projected;
            }

            // No walkable ground around the slot. Only collapse onto the anchor when the anchor itself
            // is walkable: a scene without a NavMesh must keep the authored slot, otherwise the whole
            // group would spawn stacked on a single point.
            return IsWalkable(anchor) ? anchor : candidate;
        }

        /// <summary>
        /// Projects a spawn anchor onto the NavMesh without letting it drift away from the authored point.
        /// Returns the raw anchor when no walkable ground is reachable.
        /// </summary>
        public static Vector3 SnapToNavMesh(Vector3 anchor, float tightRadius = 1.5f, float looseRadius = AnchorSearchRadius)
        {
            if (NavMesh.SamplePosition(anchor, out NavMeshHit tight, tightRadius, NavMesh.AllAreas))
                return tight.position;
            if (NavMesh.SamplePosition(anchor, out NavMeshHit loose, looseRadius, NavMesh.AllAreas))
                return loose.position;
            return anchor;
        }

        private static bool TryProjectWithin(Vector2 candidate, Vector2 anchor, float limit, out Vector2 result)
        {
            result = anchor;
            var world = new Vector3(candidate.x, 0f, candidate.y);
            float searchRadius = limit * 1.5f + 0.5f;
            if (!NavMesh.SamplePosition(world, out NavMeshHit hit, searchRadius, NavMesh.AllAreas))
                return false;

            var projected = new Vector2(hit.position.x, hit.position.z);
            if (Vector2.Distance(projected, anchor) > limit)
                return false;

            result = projected;
            return true;
        }

        private static bool IsWalkable(Vector2 position) =>
            NavMesh.SamplePosition(new Vector3(position.x, 0f, position.y), out _, 0.1f, NavMesh.AllAreas);
    }
}
