using UnityEngine;

namespace RobotSNAP.CameraControl
{
    public class OrbitMode : ICameraMode
    {
        private float _currentDistance;

        public void Enter(CameraController controller)
        {
            // Initialise la distance à partir de orbitOffset (magnitude)
            _currentDistance = controller.orbitOffset.magnitude;
            _currentDistance = Mathf.Clamp(_currentDistance, controller.minDistance, controller.maxDistance);
            controller.mainCamera.orthographic = false;
        }

        public void Update(CameraController controller, float deltaTime)
        {
            if (controller.mainCamera == null) return;
            if (controller.CurrentFollowTarget == null) return;
            HandleKeyboardInput(controller);
            UpdateFreeCamera(controller);
        }
        
        private void HandleKeyboardInput(CameraController c)
        {
            if (!c.enableKeyboardControl) return;
            
            // Mouse rotation
            if (Input.GetKey(c.rotateKey))
            {
                Vector3 delta = Input.mousePosition - c.LastMousePosition;
                c.LastMousePosition = Input.mousePosition;
                c.CurrentRotationY += delta.x * c.rotateSpeed * Time.deltaTime;
                c.CurrentRotationX -= delta.y * c.rotateSpeed * Time.deltaTime;
                c.CurrentRotationX = Mathf.Clamp(c.CurrentRotationX, 10f, 80f);
            }
            c.LastMousePosition = Input.mousePosition;

            // Zoom (molette) – utilise la vitesse de zoom du contrôleur
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0)
            {
                _currentDistance -= scroll * c.zoomSpeed * Time.deltaTime;
                _currentDistance = Mathf.Clamp(_currentDistance, c.minDistance, c.maxDistance);
            }
        }

        private void UpdateFreeCamera(CameraController c)
        {
            Quaternion rotation = Quaternion.Euler(c.CurrentRotationX, c.CurrentRotationY, 0);
            Vector3 direction = rotation * Vector3.back;
            Vector3 desiredPosition = c.CurrentFollowTarget.position + direction * _currentDistance;

            // Raycast pour éviter les obstacles (depuis la cible vers la caméra)
            Vector3 origin = c.CurrentFollowTarget.position;
            Vector3 toCamera = desiredPosition - origin;
            float distance = toCamera.magnitude;
            RaycastHit hit;
            if (Physics.SphereCast(origin, c.collisionRadius, toCamera.normalized, out hit, distance, c.obstacleMask))
            {
                // Repositionner la caméra juste avant l'obstacle
                desiredPosition = origin + toCamera.normalized * (hit.distance - c.collisionRadius);
            }

            // Lissage
            c.mainCamera.transform.position = Vector3.SmoothDamp(
                c.mainCamera.transform.position,
                desiredPosition,
                ref c.velocity,
                c.smoothTime
            );
            c.mainCamera.transform.LookAt(c.CurrentFollowTarget);
        }
        
        public void Exit(CameraController controller) { }
    }
}