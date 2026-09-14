using RobotSNAP.Core.Scenario;
using UnityEngine;
using UnityEngine.AI;

namespace RobotSNAP.Agents
{
    /// <summary>
    /// Turns an authored spawn position into a walkable one.
    /// The walkable grid of the scenario (<see cref="ScenarioNavigation"/>) takes priority over the NavMesh:
    /// it is the very ground the agents plan their paths on, so a map authored without any baked NavMesh still
    /// projects its spawns onto walkable pixels. The NavMesh stays the fallback of scenes that have one.
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
        /// The scenario grid is queried first and the NavMesh second, so the slot ends on the ground the
        /// agents actually walk on. The result never sits further from the anchor than
        /// <paramref name="maxDistance"/>, except for the final fall back onto a walkable anchor.
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
        /// Projects a spawn anchor onto walkable ground without letting it drift away from the authored point.
        /// The scenario grid is tried first, with <paramref name="tightRadius"/> and then
        /// <paramref name="looseRadius"/>; the NavMesh of the scene is only used when the grid cannot answer.
        /// Only X and Z move, the height of the anchor is kept. The grid models the floor, not the vertical layout.
        /// Returns the raw anchor when no walkable ground is reachable.
        /// </summary>
        public static Vector3 SnapToNavMesh(Vector3 anchor, float tightRadius = 1.5f, float looseRadius = AnchorSearchRadius)
        {
            if (ScenarioNavigation.IsAvailable)
            {
                var planar = new Vector2(anchor.x, anchor.z);
                if (ScenarioNavigation.TryProjectToWalkable(planar, tightRadius, out Vector2 tight))
                    return new Vector3(tight.x, anchor.y, tight.y);
                if (ScenarioNavigation.TryProjectToWalkable(planar, looseRadius, out Vector2 loose))
                    return new Vector3(loose.x, anchor.y, loose.y);
            }

            if (NavMesh.SamplePosition(anchor, out NavMeshHit tightHit, tightRadius, NavMesh.AllAreas))
                return tightHit.position;
            if (NavMesh.SamplePosition(anchor, out NavMeshHit looseHit, looseRadius, NavMesh.AllAreas))
                return looseHit.position;
            return anchor;
        }

        /// <summary>
        /// Nearest walkable point of a slot, provided it stays within <paramref name="limit"/> of the anchor.
        /// The scenario grid answers first; the NavMesh is only consulted when no grid is loaded.
        /// </summary>
        private static bool TryProjectWithin(Vector2 candidate, Vector2 anchor, float limit, out Vector2 result)
        {
            result = anchor;

            // A slot may start a bit off walkable ground, but it must never be carried past the anchor limit.
            float searchRadius = limit * 1.5f + 0.5f;

            if (ScenarioNavigation.IsAvailable)
            {
                if (!ScenarioNavigation.TryProjectToWalkable(candidate, searchRadius, out Vector2 walkable))
                    return false;
                if (Vector2.Distance(walkable, anchor) > limit)
                    return false;

                result = walkable;
                return true;
            }

            var world = new Vector3(candidate.x, 0f, candidate.y);
            if (!NavMesh.SamplePosition(world, out NavMeshHit hit, searchRadius, NavMesh.AllAreas))
                return false;

            var projected = new Vector2(hit.position.x, hit.position.z);
            if (Vector2.Distance(projected, anchor) > limit)
                return false;

            result = projected;
            return true;
        }

        /// <summary>True when the point stands on walkable ground: scenario grid first, NavMesh second.</summary>
        private static bool IsWalkable(Vector2 position)
        {
            if (ScenarioNavigation.IsAvailable)
                return ScenarioNavigation.IsWalkable(position);

            return NavMesh.SamplePosition(new Vector3(position.x, 0f, position.y), out _, 0.1f, NavMesh.AllAreas);
        }
    }
}
