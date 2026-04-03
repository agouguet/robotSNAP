using UnityEngine;
using RobotSNAP.Movement.Predictors;

namespace RobotSNAP.Human
{
    [CreateAssetMenu(fileName = "HumanConfig", menuName = "RobotSNAP/Human/Config")]
    public class HumanConfig : ScriptableObject
    {
        [Header("Movement Controller Selection")]
        public MovementControllerType controllerType = MovementControllerType.Hybrid;
        
        [Header("Legacy SFM Parameters")]
        public float sfmForce = 10f;
        public float sfmMaxSpeed = 5f;
        public float sfmRelaxationTime = 0.5f;
        public float sfmInteractionRadius = 5f;
        
        [Header("ONNX Model Parameters")]
        public ONNXModelConfig onnxModelConfig;
        
        [Header("Hybrid Parameters")]
        [Range(0f, 1f)] public float predictionWeight = 0.6f;
        [Range(0f, 1f)] public float navigationWeight = 0.4f;
        public bool useAdaptiveWeighting = true;
        
        [Header("Animation Parameters")]
        public float animationSmoothing = 0.6f;
        public float idleSpeedThreshold = 0.5f;
        public float angularSpeed = 180f;
        
        [Header("Navigation")]
        public float goalReachedDistance = 0.2f;
        public float pathUpdateInterval = 0.2f;
        
        [Header("Visualization")]
        public bool showPredictionGizmos = true;
        public Color predictionColor = Color.yellow;
        public Color trajectoryColor = Color.blue;
        
        public string onnxModelPath => onnxModelConfig != null ? onnxModelConfig.modelPath : "Models/policy_lstm";
        public bool onnxUseGPU => onnxModelConfig != null ? onnxModelConfig.useGPU : true;
        public int predictionSamples => onnxModelConfig != null ? onnxModelConfig.numSamples : 5;
        public float predictionUpdateRate => onnxModelConfig != null ? onnxModelConfig.updateRate : 10f;
        
        private void OnValidate()
        {
            if (onnxModelConfig != null)
            {
                onnxModelConfig.Validate();
            }
        }
    }

    public enum MovementControllerType
    {
        LegacySFM,
        ONNXPrediction,
        Hybrid
    }
}