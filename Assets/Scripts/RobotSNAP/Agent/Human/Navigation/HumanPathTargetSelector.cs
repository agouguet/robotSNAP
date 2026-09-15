using System.Collections.Generic;
using UnityEngine;

namespace RobotSNAP.Agents
{
    /// <summary>Selects a NavMesh steering target without treating intermediate corners as final goals.</summary>
    public static class HumanPathTargetSelector
    {
        public static Vector2 SelectTarget(
            Vector2 currentPosition,
            Vector2 finalDestination,
            IReadOnlyList<Vector3> pathCorners,
            float waypointAdvanceDistance,
            float destinationReachedDistance)
        {
            if (pathCorners == null || pathCorners.Count == 0)
                return finalDestination;

            for (int index = 0; index < pathCorners.Count; index++)
            {
                Vector3 corner = pathCorners[index];
                Vector2 corner2D = new(corner.x, corner.z);
                bool isFinalCorner = index == pathCorners.Count - 1;
                float advanceDistance = isFinalCorner
                    ? destinationReachedDistance
                    : waypointAdvanceDistance;
                if (Vector2.Distance(currentPosition, corner2D) > advanceDistance)
                    return corner2D;
            }

            return finalDestination;
        }

        /// <summary>
        /// How many leading entries of a kept path the agent has already reached: the anchor the plan was
        /// re-based on, plus every corner within <paramref name="advanceDistance"/> of it. The caller drops that
        /// many entries and re-anchors the head on the current position.
        ///
        /// A kept path is re-anchored on the agent at every update, which is what lets a slowly moving
        /// destination be followed without paying for a search. The catch is that the corners behind the agent
        /// stay in the list, and steering towards one of them turns the agent around: it oscillates on the spot
        /// and never arrives. Consuming them makes the progress permanent.
        /// </summary>
        public static int CountConsumedCorners(
            Vector2 currentPosition,
            IReadOnlyList<Vector3> pathCorners,
            float advanceDistance)
        {
            if (pathCorners == null || pathCorners.Count < 2)
                return 0;

            // Index 0 is the anchor: the agent stands on it. The last corner is the destination and is never
            // consumed here, so the path always keeps a head and a tail to steer between.
            int firstKept = 1;
            while (firstKept < pathCorners.Count - 1 &&
                   Vector2.Distance(currentPosition, ToPlane(pathCorners[firstKept])) <= advanceDistance)
            {
                firstKept++;
            }

            return firstKept - 1;
        }

        private static Vector2 ToPlane(Vector3 corner) => new(corner.x, corner.z);
    }
}
