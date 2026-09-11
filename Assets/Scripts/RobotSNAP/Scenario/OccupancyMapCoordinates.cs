using UnityEngine;

namespace RobotSNAP.Core.Scenario
{
    /// <summary>
    /// Converts between a top-left-origin occupancy-map preview and RobotSNAP's
    /// historical Unity world convention. The image's left/top edges map to
    /// world max X/min Z respectively.
    /// </summary>
    public static class OccupancyMapCoordinates
    {
        public static Vector2 ImageNormalizedToWorld(Vector2 imagePosition, Bounds worldBounds)
        {
            float imageX = Mathf.Clamp01(imagePosition.x);
            float imageY = Mathf.Clamp01(imagePosition.y);
            return new Vector2(
                Mathf.Lerp(worldBounds.max.x, worldBounds.min.x, imageX),
                Mathf.Lerp(worldBounds.min.z, worldBounds.max.z, imageY));
        }

        public static Vector2 WorldToImageNormalized(Vector2 worldPosition, Bounds worldBounds)
        {
            float imageX = Mathf.Approximately(worldBounds.size.x, 0f)
                ? 0.5f
                : (worldBounds.max.x - worldPosition.x) / worldBounds.size.x;
            float imageY = Mathf.Approximately(worldBounds.size.z, 0f)
                ? 0.5f
                : (worldPosition.y - worldBounds.min.z) / worldBounds.size.z;
            return new Vector2(Mathf.Clamp01(imageX), Mathf.Clamp01(imageY));
        }
    }
}
