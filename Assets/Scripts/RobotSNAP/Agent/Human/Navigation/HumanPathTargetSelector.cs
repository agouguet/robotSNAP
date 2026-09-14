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
    }
}
