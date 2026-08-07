using UnityEngine;

namespace RobotSNAP.CameraControl
{
    public class FreeCameraMode : ICameraMode
    {
        public void Enter(CameraController controller)
        {
            controller.mainCamera.orthographic = false;
        }
        
        public void Update(CameraController controller, float deltaTime)
        {
            if (controller.mainCamera == null) return;
            HandleKeyboardInput(controller);
            UpdateFreeCamera(controller);
        }
        
        private void HandleKeyboardInput(CameraController c)
        {
            if (!c.enableKeyboardControl) return;
            
            float speed = c.moveSpeed;
            if (Input.GetKey(c.fastMoveKey)) speed *= 3f;
            
            // --- Mouvement (WASD/QE) avec collision ---
            Vector3 move = Vector3.zero;
            if (Input.GetKey(c.forwardKey)) move += c.mainCamera.transform.forward;
            if (Input.GetKey(c.backwardKey)) move -= c.mainCamera.transform.forward;
            if (Input.GetKey(c.rightKey)) move += c.mainCamera.transform.right;
            if (Input.GetKey(c.leftKey)) move -= c.mainCamera.transform.right;
            if (Input.GetKey(c.upKey)) move += Vector3.up;
            if (Input.GetKey(c.downKey)) move -= Vector3.up;
            
            if (move != Vector3.zero)
            {
                Vector3 proposed = c.TargetPosition + move.normalized * speed * Time.unscaledDeltaTime;
                // Vérifier la collision depuis la position actuelle de la caméra vers la position proposée
                Vector3 origin = c.mainCamera.transform.position;
                if (c.AdjustPositionForCollision(proposed, origin, c.collisionRadius, out Vector3 adjusted))
                {
                    c.TargetPosition = adjusted;
                }
                else
                {
                    c.TargetPosition = proposed;
                }
            }
            
            // --- Rotation de la caméra (clic droit) ---
            if (Input.GetKeyDown(c.rotateKey))
            {
                c.IsRotating = true;
                c.LastMousePosition = Input.mousePosition;
            }
            if (Input.GetKeyUp(c.rotateKey))
                c.IsRotating = false;
            
            if (c.IsRotating)
            {
                Vector3 delta = Input.mousePosition - c.LastMousePosition;
                c.LastMousePosition = Input.mousePosition;
                c.CurrentRotationY += delta.x * c.rotateSpeed * Time.unscaledDeltaTime;
                c.CurrentRotationX -= delta.y * c.rotateSpeed * Time.unscaledDeltaTime;
                c.CurrentRotationX = Mathf.Clamp(c.CurrentRotationX, -90f, 90f);
                c.TargetRotation = Quaternion.Euler(c.CurrentRotationX, c.CurrentRotationY, 0);
            }
            
            // --- Zoom (molette) avec collision ---
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0)
            {
                if (c.mainCamera.orthographic)
                {
                    c.mainCamera.orthographicSize -= scroll * c.zoomSpeed * Time.unscaledDeltaTime;
                }
                else
                {
                    // Déplacement avant/arrière selon la direction de la caméra
                    Vector3 proposed = c.TargetPosition + c.mainCamera.transform.forward * scroll * c.zoomSpeed;
                    Vector3 origin = c.mainCamera.transform.position;
                    if (c.AdjustPositionForCollision(proposed, origin, c.collisionRadius, out Vector3 adjusted))
                    {
                        c.TargetPosition = adjusted;
                    }
                    else
                    {
                        c.TargetPosition = proposed;
                    }
                }
            }
        }

        private void UpdateFreeCamera(CameraController c)
        {
            // Appliquer la position (pas de lissage dans votre version)
            c.mainCamera.transform.position = c.TargetPosition;
            
            // Appliquer la rotation
            if (!c.IsRotating)
            {
                c.mainCamera.transform.rotation = Quaternion.Slerp(
                    c.mainCamera.transform.rotation,
                    c.TargetRotation,
                    c.smoothTime * 2
                );
            }
            else
            {
                c.mainCamera.transform.rotation = c.TargetRotation;
            }
        }
        
        public void Exit(CameraController controller) { }
    }
}