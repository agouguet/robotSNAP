using System.Collections;
using UnityEngine;

namespace RobotSNAP.CameraControl
{
    public class CinematicMode : ICameraMode
    {
        private Coroutine _activeSequence;
        
        public void Enter(CameraController controller) { }
        public void Update(CameraController controller, float deltaTime) { }
        public void Exit(CameraController controller) { }
        
        public IEnumerator PlaySequence(CameraController controller, Transform[] waypoints, float duration)
        {
            foreach (var waypoint in waypoints)
            {
                Vector3 startPos = controller.mainCamera.transform.position;
                Quaternion startRot = controller.mainCamera.transform.rotation;
                float elapsed = 0;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / duration;
                    t = Mathf.SmoothStep(0, 1, t);
                    controller.mainCamera.transform.position = Vector3.Lerp(startPos, waypoint.position, t);
                    controller.mainCamera.transform.rotation = Quaternion.Slerp(startRot, waypoint.rotation, t);
                    yield return null;
                }
            }
        }
    }
}