// OrbitMode.cs
using UnityEngine;

namespace RobotSNAP.CameraControl
{
    /// <summary>
    /// The only interactive view of the simulation: the camera turns around a pivot at a fixed
    /// distance. The pivot is the selected agent while one is selected, and the point the move tool
    /// last slid to otherwise. Every gesture comes from the view toolbar, so this mode reads no key
    /// of its own apart from the wheel.
    /// </summary>
    public class OrbitMode : ICameraMode
    {
        private float _currentDistance;

        public void Enter(CameraController controller)
        {
            // While an agent is followed, the shot carries on from where the camera stands, so
            // switching view never makes it jump. Without a target there is nothing to carry over:
            // the view opens at the authored orbit distance.
            float distance = controller.orbitDistance;
            if (controller.CurrentFollowTarget != null && controller.mainCamera != null)
                distance = Vector3.Distance(controller.mainCamera.transform.position, Pivot(controller));

            _currentDistance = Mathf.Clamp(distance, controller.minDistance, controller.maxDistance);
            controller.mainCamera.orthographic = false;
        }

        public void Update(CameraController controller, float deltaTime)
        {
            if (controller.mainCamera == null) return;

            HandleZoom(controller);
            UpdateCamera(controller);
        }

        /// <summary>The point the camera turns around: the selected agent, or the free pivot.</summary>
        private static Vector3 Pivot(CameraController c) =>
            c.CurrentFollowTarget != null ? c.CurrentFollowTarget.position : c.OrbitPivot;

        /// <summary>The wheel and the zoom tool both feed the same distance.</summary>
        private void HandleZoom(CameraController c)
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0)
                _currentDistance -= scroll * c.zoomSpeed * Time.deltaTime;

            if (c.PendingZoom != 0f)
            {
                _currentDistance += c.PendingZoom;
                c.PendingZoom = 0f;
            }

            _currentDistance = Mathf.Clamp(_currentDistance, c.minDistance, c.maxDistance);
        }

        private void UpdateCamera(CameraController c)
        {
            Vector3 pivot = Pivot(c);

            // Remembering the followed spot keeps the view still when the agent is released.
            if (c.CurrentFollowTarget != null)
                c.OrbitPivot = new Vector3(pivot.x, 0f, pivot.z);

            Quaternion rotation = Quaternion.Euler(c.CurrentRotationX, c.CurrentRotationY, 0);
            Vector3 direction = rotation * Vector3.back;
            Vector3 desiredPosition = pivot + direction * _currentDistance;

            // No obstacle avoidance on purpose. The maps of this project are corridors a couple of
            // metres wide: a sphere cast against the walls would glue the camera to the agent and
            // throw away the framing the user asked for. The orbit keeps its distance and the walls
            // are allowed to cross the shot, the way an editor camera behaves.

            // Lissage
            c.mainCamera.transform.position = Vector3.SmoothDamp(
                c.mainCamera.transform.position,
                desiredPosition,
                ref c.velocity,
                c.smoothTime
            );
            c.mainCamera.transform.LookAt(pivot);
        }
        
        public void Exit(CameraController controller) { }
    }
}
