using UnityEngine;

namespace RobotSNAP.CameraControl
{
    public class ThirdPersonMode : ICameraMode
    {
        private float _currentDistance;
        private float _currentHeight;
        private Vector3 _originalOffset; // stockage de l'offset original (thirdPersonOffset)
        private float _lookAheadDistance = 2f; // distance devant le robot pour le point de mire

        public void Enter(CameraController controller)
        {
            controller.mainCamera.orthographic = false;

            // Utiliser l'offset thirdPersonOffset défini dans CameraController
            _originalOffset = controller.thirdPersonOffset;
            _currentDistance = Mathf.Abs(_originalOffset.z);
            _currentHeight = _originalOffset.y;
            _currentDistance = Mathf.Clamp(_currentDistance, controller.minDistance, controller.maxDistance);
            _currentHeight = Mathf.Clamp(_currentHeight, controller.minHeight, controller.maxHeight);
        }

        public void Update(CameraController controller, float deltaTime)
        {
            if (controller.CurrentFollowTarget == null) return;

            // Zoom molette : ajuste la distance (mais on garde l'offset Y proportionnel ? optionnel)
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0)
            {
                _currentDistance -= scroll * controller.zoomSpeed * deltaTime;
                _currentDistance = Mathf.Clamp(_currentDistance, controller.minDistance, controller.maxDistance);
                // On pourrait aussi ajuster la hauteur proportionnellement, mais laissons simple.
            }

            // Position désirée : derrière l'agent selon son orientation, avec distance et hauteur ajustées
            Vector3 desiredPos = controller.CurrentFollowTarget.position
                                 - controller.CurrentFollowTarget.forward * _currentDistance
                                 + Vector3.up * _currentHeight;

            // Évitement des obstacles (SphereCast)
            Vector3 origin = controller.CurrentFollowTarget.position;
            Vector3 toCamera = desiredPos - origin;
            float distance = toCamera.magnitude;
            RaycastHit hit;
            if (Physics.SphereCast(origin, controller.collisionRadius, toCamera.normalized, out hit, distance, controller.obstacleMask))
            {
                desiredPos = origin + toCamera.normalized * (hit.distance - controller.collisionRadius);
            }

            // Lissage
            controller.mainCamera.transform.position = Vector3.SmoothDamp(
                controller.mainCamera.transform.position,
                desiredPos,
                ref controller.velocity,
                controller.smoothTime
            );

            // Point de mire : devant le robot (sur son axe forward)
            Vector3 lookAtPoint = controller.CurrentFollowTarget.position 
                                  + controller.CurrentFollowTarget.forward * _lookAheadDistance;
            // On peut aussi ajouter un petit offset vertical pour ne pas regarder trop bas
            lookAtPoint.y += 0.5f; // optionnel

            controller.mainCamera.transform.LookAt(lookAtPoint);
        }

        public void Exit(CameraController controller) { }
    }
}