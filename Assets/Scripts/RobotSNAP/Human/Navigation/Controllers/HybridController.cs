using UnityEngine;
using RobotSNAP.Human;

namespace RobotSNAP.Movement.Controllers
{
    /// <summary>
    /// Contrôleur hybride fusionnant SFM et ONNX
    /// </summary>
    public class HybridController : RobotSNAP.Movement.Interfaces.IMovementController
    {
        private SFMController sfmController;
        private ONNXPredictionController onnxController;
        private HumanConfig config;
        private Vector2 lastHybridMovement;
        
        public HybridController(HumanConfig config)
        {
            this.config = config;
            sfmController = new SFMController(config);
            onnxController = new ONNXPredictionController(config);
        }

        public Vector2 ComputeVelocity(
            Vector2 currentPosition,
            Vector2 currentVelocity,
            Vector2 goalPosition,
            Vector2[] neighbors,
            Vector2[] neighborVelocities,
            float deltaTime)
        {
            return currentPosition;
        }
        
        public void Reset()
        {
            sfmController.Reset();
            onnxController.Reset();
            lastHybridMovement = Vector2.zero;
        }
        
        public float GetConfidence()
        {
            return Mathf.Lerp(
                sfmController.GetConfidence(),
                onnxController.GetConfidence(),
                config.predictionWeight
            );
        }
    
    }
}