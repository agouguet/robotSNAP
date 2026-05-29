using UnityEngine;

namespace RobotSNAP.CameraControl
{
    public class TopDownMode : ICameraMode
    {
        private float _currentHeight;   // hauteur actuelle (offset Y)

        public void Enter(CameraController controller)
        {
            // Initialise la hauteur à partir de topDownOffset.y
            _currentHeight = controller.topDownOffset.y;
            _currentHeight = Mathf.Clamp(_currentHeight, controller.minHeight, controller.maxHeight);
            controller.mainCamera.orthographic = true;
        }

        public void Update(CameraController controller, float deltaTime)
        {
            if (controller.CurrentFollowTarget == null) return;

            // Zoom avec la molette
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0)
            {
                _currentHeight -= scroll * controller.zoomSpeed;
                _currentHeight = Mathf.Clamp(_currentHeight, controller.minHeight, controller.maxHeight);
            }

            // Calcul de la position cible
            Vector3 targetPos = controller.CurrentFollowTarget.position;
            targetPos.y += _currentHeight;   // applique la hauteur variable
            targetPos += controller.topDownOffset;
            targetPos.x = controller.CurrentFollowTarget.position.x + controller.topDownOffset.x;
            targetPos.z = controller.CurrentFollowTarget.position.z + controller.topDownOffset.z;

            // Lissage
            controller.mainCamera.transform.position = Vector3.Lerp(
                controller.mainCamera.transform.position,
                targetPos,
                controller.smoothTime * 2
            );
            controller.mainCamera.transform.rotation = Quaternion.Slerp(
                controller.mainCamera.transform.rotation,
                Quaternion.Euler(90, 0, 0),
                controller.smoothTime
            );
        }

        public void Exit(CameraController controller) { }
    }
}