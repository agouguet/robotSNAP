using UnityEngine;

namespace RobotSNAP.CameraControl
{
    public class TopDownMode : ICameraMode
    {
        private float _currentZoom;
        private Vector3 _targetPosition;
        private bool _isPanning;
        private Vector3 _lastMousePosition;
        private bool _isFocusActive;   // indique si on suit un agent

        public void Enter(CameraController controller)
        {
            controller.mainCamera.orthographic = true;

            // Initialisation selon présence d'un focus
            if (controller.CurrentFollowTarget != null)
            {
                _isFocusActive = true;
                _currentZoom = controller.topDownOffset.y / 2f;
                _currentZoom = Mathf.Clamp(_currentZoom, controller.minDistance, controller.maxDistance);
                controller.mainCamera.orthographicSize = _currentZoom;
                _targetPosition = controller.CurrentFollowTarget.position + controller.topDownOffset;
                _targetPosition.y = controller.topDownOffset.y;
            }
            else
            {
                _isFocusActive = false;
                _currentZoom = 10f;
                _currentZoom = Mathf.Clamp(_currentZoom, controller.minDistance, controller.maxDistance);
                controller.mainCamera.orthographicSize = _currentZoom;
                _targetPosition = controller.mainCamera.transform.position;
                if (_targetPosition == Vector3.zero) _targetPosition = new Vector3(0, controller.topDownOffset.y, 0);
                _targetPosition.y = controller.topDownOffset.y;
            }

            controller.mainCamera.transform.position = _targetPosition;
            controller.mainCamera.transform.rotation = Quaternion.Euler(90, 180, 0);
        }

        public void Update(CameraController controller, float deltaTime)
        {
            // Zoom (toujours actif)
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0)
            {
                _currentZoom -= scroll * controller.zoomSpeed * Time.deltaTime;
                _currentZoom = Mathf.Clamp(_currentZoom, controller.minDistance, controller.maxDistance);
                controller.mainCamera.orthographicSize = _currentZoom;
            }

            if (_isFocusActive)
            {
                // En mode focus : on vérifie si l'utilisateur interagit (pan ou WASD)
                bool userInteracted = CheckUserInteraction(controller);
                if (userInteracted)
                {
                    // Bascule en mode libre : on perd le focus
                    _isFocusActive = false;
                    // La position cible devient la position actuelle de la caméra
                    _targetPosition = controller.mainCamera.transform.position;
                    // On signale que le focus a changé (facultatif)
                    controller.CurrentFollowTarget = null;
                    controller.OnFollowTargetChanged?.Invoke(null);
                }
                else
                {
                    // Suivi de l'agent
                    Vector3 desiredPos = controller.CurrentFollowTarget.position + controller.topDownOffset;
                    desiredPos.y = controller.topDownOffset.y;
                    controller.mainCamera.transform.position = Vector3.Lerp(
                        controller.mainCamera.transform.position,
                        desiredPos,
                        controller.smoothTime * 2
                    );
                    return;
                }
            }

            // Mode libre : gestion des déplacements
            HandleFreeMove(controller, deltaTime);
            controller.mainCamera.transform.position = Vector3.SmoothDamp(
                controller.mainCamera.transform.position,
                _targetPosition,
                ref controller.velocity,
                controller.smoothTime
            );
        }

        private bool CheckUserInteraction(CameraController c)
        {
            // Vérifie si l'utilisateur a commencé un pan (clic droit)
            if (Input.GetKeyDown(c.rotateKey))
                return true;

            // Vérifie si une touche WASD est pressée
            if (c.enableKeyboardControl)
            {
                if (Input.GetKey(c.forwardKey) || Input.GetKey(c.backwardKey) ||
                    Input.GetKey(c.rightKey) || Input.GetKey(c.leftKey))
                    return true;
            }
            return false;
        }

        private void HandleFreeMove(CameraController c, float deltaTime)
        {
            // Pan with right mouse button
            if (Input.GetKeyDown(c.rotateKey))
            {
                _isPanning = true;
                _lastMousePosition = Input.mousePosition;
            }
            if (Input.GetKeyUp(c.rotateKey))
                _isPanning = false;

            if (_isPanning)
            {
                Vector3 delta = Input.mousePosition - _lastMousePosition;
                _lastMousePosition = Input.mousePosition;
                float speedFactor = _currentZoom * 0.01f;
                Vector3 move = new Vector3(delta.x * speedFactor, 0, delta.y * speedFactor);
                _targetPosition += move;
            }

            // WASD movement
            if (c.enableKeyboardControl)
            {
                float moveSpeed = c.moveSpeed * Time.deltaTime;
                Vector3 move = Vector3.zero;
                if (Input.GetKey(c.forwardKey)) move += Vector3.forward;
                if (Input.GetKey(c.backwardKey)) move -= Vector3.forward;
                if (Input.GetKey(c.rightKey)) move += Vector3.right;
                if (Input.GetKey(c.leftKey)) move -= Vector3.right;
                if (move != Vector3.zero)
                    _targetPosition += move * moveSpeed;
            }

            // Contrainte hauteur fixe
            _targetPosition.y = c.topDownOffset.y;
        }

        public void Exit(CameraController controller)
        {
            controller.mainCamera.orthographic = false;
        }
    }
}