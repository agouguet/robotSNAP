// ICameraMode.cs
using UnityEngine;

namespace RobotSNAP.CameraControl
{
    public interface ICameraMode
    {
        void Enter(CameraController controller);
        void Update(CameraController controller, float deltaTime);
        void Exit(CameraController controller);
    }
}