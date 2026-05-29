using UnityEngine;

namespace RobotSNAP.CameraControl
{
    public class FreeCameraMode : ICameraMode
    {
        public void Enter(CameraController controller)
        {
            // Rien de spécial
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
            
            Vector3 move = Vector3.zero;
            if (Input.GetKey(c.forwardKey)) move += c.mainCamera.transform.forward;
            if (Input.GetKey(c.backwardKey)) move -= c.mainCamera.transform.forward;
            if (Input.GetKey(c.rightKey)) move += c.mainCamera.transform.right;
            if (Input.GetKey(c.leftKey)) move -= c.mainCamera.transform.right;
            if (Input.GetKey(c.upKey)) move += Vector3.up;
            if (Input.GetKey(c.downKey)) move -= Vector3.up;
            
            if (move != Vector3.zero)
                c.TargetPosition += move.normalized * speed * Time.deltaTime;
            
            // Mouse rotation
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
                c.CurrentRotationY += delta.x * c.rotateSpeed * Time.deltaTime;
                c.CurrentRotationX -= delta.y * c.rotateSpeed * Time.deltaTime;
                c.CurrentRotationX = Mathf.Clamp(c.CurrentRotationX, -90f, 90f);
                c.TargetRotation = Quaternion.Euler(c.CurrentRotationX, c.CurrentRotationY, 0);
            }
            
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0)
            {
                if (c.mainCamera.orthographic)
                    c.mainCamera.orthographicSize -= scroll * c.zoomSpeed * Time.deltaTime;
                else
                    c.TargetPosition += c.mainCamera.transform.forward * scroll * c.zoomSpeed;
            }
        }
        
        private void UpdateFreeCamera(CameraController c)
        {
            c.mainCamera.transform.position = Vector3.SmoothDamp(
                c.mainCamera.transform.position,
                c.TargetPosition,
                ref c.velocity,
                c.smoothTime
            );
            
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