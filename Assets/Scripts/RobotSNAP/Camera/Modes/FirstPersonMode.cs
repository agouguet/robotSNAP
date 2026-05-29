using UnityEngine;

namespace RobotSNAP.CameraControl
{
    public class FirstPersonMode : ICameraMode
    {
        public void Enter(CameraController controller)
        {

        }
        
        public void Update(CameraController controller, float deltaTime)
        {
            if (controller.CurrentFollowTarget == null) return;
            Vector3 targetPos = controller.CurrentFollowTarget.position +
                controller.CurrentFollowTarget.rotation * controller.firstPersonOffset;
            controller.mainCamera.transform.position = targetPos;
            controller.mainCamera.transform.rotation = controller.CurrentFollowTarget.rotation;
        }
        
        public void Exit(CameraController controller) { }
    }
}