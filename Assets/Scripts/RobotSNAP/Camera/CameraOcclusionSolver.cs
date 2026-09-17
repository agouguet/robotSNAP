// CameraOcclusionSolver.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RobotSNAP.CameraControl
{
    /// <summary>
    /// Keeps the agent the camera follows visible.
    ///
    /// The orbit never avoids geometry — a corridor map, and any map with a ceiling, will put a wall
    /// between the camera and the agent it is framing, and moving the camera there would throw away
    /// the framing the user asked for. So the wall steps out of the picture instead: its renderer
    /// switches to shadows only, which takes it out of the image while the scene keeps both its
    /// lighting and the shadow the wall throws. That is the difference between this and hiding the
    /// renderer, which would leave the floor lit as if the wall had never existed.
    ///
    /// Only geometry is revealed: another pedestrian standing in the way is part of the scene, not an
    /// obstacle to look through. Everything returns the moment the line of sight clears, with a short
    /// grace period so a wall at the edge of the ray does not strobe as the agent walks past a corner.
    /// </summary>
    public sealed class CameraOcclusionSolver
    {
        private readonly Camera _camera;
        private readonly LayerMask _mask;
        private readonly float _graceSeconds;
        private readonly float _eyeHeight;

        /// <summary>The renderers currently out of the picture, with the mode to give back and the time they may return.</summary>
        private readonly Dictionary<Renderer, Revealed> _revealed = new Dictionary<Renderer, Revealed>();
        private readonly List<Renderer> _finished = new List<Renderer>();

        private struct Revealed
        {
            public ShadowCastingMode Mode;
            public float Until;
        }

        public CameraOcclusionSolver(Camera camera, LayerMask mask, float graceSeconds, float eyeHeight)
        {
            _camera = camera;
            _mask = mask;
            _graceSeconds = Mathf.Max(0f, graceSeconds);
            _eyeHeight = Mathf.Max(0.1f, eyeHeight);
        }

        /// <summary>How many renderers are out of the picture right now. Read by the tests.</summary>
        public int RevealedCount => _revealed.Count;

        /// <summary>
        /// Reveals whatever stands between the camera and <paramref name="target"/>. A null target —
        /// nothing is followed — puts every wall back, because there is no subject to keep in sight.
        /// </summary>
        public void Tick(Transform target)
        {
            if (_camera == null || target == null)
            {
                RestoreAll();
                return;
            }

            float now = Time.unscaledTime;
            Vector3 origin = _camera.transform.position;
            Vector3 anchor = target.position + Vector3.up * _eyeHeight;
            Vector3 ray = anchor - origin;
            float distance = ray.magnitude;

            if (distance > 0.01f)
            {
                RaycastHit[] hits = Physics.RaycastAll(origin, ray / distance, distance, _mask, QueryTriggerInteraction.Ignore);
                for (int index = 0; index < hits.Length; index++)
                    Consider(hits[index], target, now);
            }

            RestoreFinished(now);
        }

        /// <summary>Puts every wall back, whatever it was blocking.</summary>
        public void RestoreAll()
        {
            foreach (KeyValuePair<Renderer, Revealed> entry in _revealed)
                Restore(entry.Key, entry.Value);

            _revealed.Clear();
        }

        /// <summary>
        /// Takes one hit into account. Anything that belongs to an agent is skipped: the followed one
        /// is what we are looking at, and the others are people, not walls.
        /// </summary>
        private void Consider(RaycastHit hit, Transform target, float now)
        {
            Collider collider = hit.collider;
            if (collider == null) return;
            if (collider.transform.IsChildOf(target)) return;
            if (collider.GetComponentInParent<RobotSNAP.Agents.HumanAgent>() != null) return;
            if (collider.GetComponentInParent<RobotSNAP.Agents.Robot>() != null) return;

            Renderer renderer = collider.GetComponent<Renderer>();
            if (renderer == null) renderer = collider.GetComponentInParent<Renderer>();
            if (renderer == null) return;

            if (!_revealed.TryGetValue(renderer, out Revealed state))
                state = new Revealed { Mode = renderer.shadowCastingMode };

            state.Until = now + _graceSeconds;
            _revealed[renderer] = state;

            if (renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly)
                renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }

        /// <summary>Gives back the walls the ray no longer crosses.</summary>
        private void RestoreFinished(float now)
        {
            _finished.Clear();

            foreach (KeyValuePair<Renderer, Revealed> entry in _revealed)
            {
                if (entry.Value.Until >= now) continue;

                Restore(entry.Key, entry.Value);
                _finished.Add(entry.Key);
            }

            foreach (Renderer renderer in _finished)
                _revealed.Remove(renderer);
        }

        private static void Restore(Renderer renderer, Revealed state)
        {
            // A scenario change destroys the walls of the previous map, so the renderer may be gone.
            if (renderer == null) return;

            renderer.shadowCastingMode = state.Mode;
        }
    }
}
