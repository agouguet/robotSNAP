using UnityEngine;

namespace RobotSNAP.Core.Scenario
{
    /// <summary>
    /// Layout maths shared by the scenario map widgets: the route editor and the read-only review recap.
    /// </summary>
    public static class OccupancyMapLayout
    {
        /// <summary>
        /// Rect actually covered by a scale-to-fit occupancy image inside its canvas.
        /// Returns <see cref="Rect.zero"/> when the texture or the canvas has no usable size.
        /// </summary>
        public static Rect FitRect(Rect canvasRect, int textureWidth, int textureHeight)
        {
            if (textureWidth <= 0 || textureHeight <= 0 || canvasRect.width <= 0f || canvasRect.height <= 0f)
                return Rect.zero;

            float imageAspect = (float)textureWidth / textureHeight;
            float canvasAspect = canvasRect.width / canvasRect.height;
            if (imageAspect > canvasAspect)
            {
                float height = canvasRect.width / imageAspect;
                return new Rect(0f, (canvasRect.height - height) * 0.5f, canvasRect.width, height);
            }

            float width = canvasRect.height * imageAspect;
            return new Rect((canvasRect.width - width) * 0.5f, 0f, width, canvasRect.height);
        }
    }
}
