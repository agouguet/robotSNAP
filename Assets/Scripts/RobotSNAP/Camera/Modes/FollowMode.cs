using UnityEngine;

namespace RobotSNAP.CameraControl
{
    public class FollowMode : ICameraMode
    {
        public void Enter(CameraController controller)
        {
            controller.RefreshFollowableTargets();
            if (controller.GetFollowableTargets().Count > 0)
                controller.SetFollowTarget(controller.GetFollowableTargets()[0]);
        }
        
        public void Update(CameraController controller, float deltaTime)
        {
            if (controller.CurrentFollowTarget == null) return;
            Vector3 targetPos = controller.CurrentFollowTarget.position + controller.followOffset;
            controller.mainCamera.transform.position = Vector3.SmoothDamp(
                controller.mainCamera.transform.position,
                targetPos,
                ref controller.velocity,
                controller.smoothTime
            );
            controller.mainCamera.transform.LookAt(controller.CurrentFollowTarget);
        }
        
        public void Exit(CameraController controller) { }
    }
}