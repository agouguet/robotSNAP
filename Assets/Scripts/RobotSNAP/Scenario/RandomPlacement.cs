using System;
using UnityEngine;

namespace RobotSNAP.Core.Scenario
{
    /// <summary>
    /// Draws a spawn or a goal inside a zone and keeps it on ground the agents can actually walk on.
    ///
    /// The randomness is injected rather than owned: the simulation hands in a sampler built on
    /// <c>UnityEngine.Random</c>, which <c>SimulationRuntimeSettings.Apply</c> seeds for the whole run, so a
    /// given seed replays the very same points. A test hands in a scripted sampler and replays a fixed suite
    /// of draws. Nothing is kept between calls, so two identical runs draw the identical positions.
    ///
    /// Every point returned here is a world-space XZ point: the two components are the horizontal axes, and the
    /// vertical axis of the zone stays the business of the caller that builds its Vector3.
    /// </summary>
    public static class RandomPlacement
    {
        /// <summary>Draws tried before the last one is projected instead of drawn.</summary>
        public const int DefaultAttempts = 24;

        /// <summary>
        /// One uniform draw inside the XZ footprint of <paramref name="bounds"/>, at the height of its centre.
        /// Injected so the runtime can seed its own randomness and a test can replay fixed points.
        /// </summary>
        public delegate Vector2 Sampler(Bounds bounds);

        /// <summary>
        /// First of up to <paramref name="attempts"/> draws that <paramref name="isWalkable"/> accepts.
        ///
        /// A zone planted in a wall still has to yield a usable position: when every draw is refused, the last
        /// one is handed to <paramref name="project"/>, which pulls it onto the nearest navigable ground — the
        /// closest stand-in available for a zone that sits entirely inside a wall. <paramref name="attempts"/>
        /// zero or negative counts as a single attempt, so the budget can never turn into an endless loop.
        ///
        /// Degenerate inputs are answered instead of thrown back at the caller: a zone with no horizontal
        /// extent (a lone point, or an empty size) is never sampled and its centre is projected, and a missing
        /// sampler, walkability test or projection falls back on the centre of the zone.
        /// </summary>
        public static Vector2 SampleWalkable(
            Bounds bounds,
            Sampler sampler,
            Func<Vector2, bool> isWalkable,
            Func<Vector2, Vector2> project,
            int attempts = DefaultAttempts)
        {
            var centre = new Vector2(bounds.center.x, bounds.center.z);

            // Nothing to draw with, or nothing to judge a draw with: the centre is the only stable answer.
            if (sampler == null || isWalkable == null || project == null)
                return centre;

            // A zone without footprint cannot be sampled: its projected centre is the closest thing to a draw.
            if (bounds.size.x <= 0f || bounds.size.z <= 0f)
                return project(centre);

            int tries = attempts > 0 ? attempts : 1;
            Vector2 drawn = centre;
            for (int attempt = 0; attempt < tries; attempt++)
            {
                drawn = sampler(bounds);
                if (isWalkable(drawn))
                    return drawn;
            }

            return project(drawn);
        }
    }
}
