using UnityEngine;
using RobotSNAP.Movement.Predictors;

namespace RobotSNAP.Human
{
    [CreateAssetMenu(fileName = "HumanConfig", menuName = "RobotSNAP/Human/Config")]
    public class HumanConfig : ScriptableObject
    {
        [SerializeField] private bool showMovementParameters = true;
        [SerializeField] private bool showMovementController = true;
        [SerializeField] private bool showSFMParameters = true;
        [SerializeField] private bool showONNXParameters = true;
        [SerializeField] private bool showHybridParameters = true;
        [SerializeField] private bool showAnimationParameters = true;
        [SerializeField] private bool showNavigationParameters = true;
        [SerializeField] private bool showVisualizationParameters = true;

        // Movement Parameters
        public float desiredSpeed = 0.8f;
        public float maxSpeed = 1.0f;

        // Movement Controller Selection
        public MovementControllerType controllerType = MovementControllerType.Hybrid;
        
        // SFM Parameters
        [Header("SFM - Perception")]
        [Tooltip("Rayon de perception des agents et obstacles")]
        public float perceptionRadiusAgent = 2.5f;
        
        [Header("SFM - Social Forces")]
        [Tooltip("Temps de relaxation (inertie du mouvement)")]
        public float relaxationTime = 0.5f;
        [Tooltip("Force d'interaction sociale")]
        public float socialForceA = 1500f / 4f; // 375
        [Tooltip("Portée de l'interaction sociale")]
        public float socialForceB = 0.08f * 2f; // 0.16
        [Tooltip("Rigidité du contact")]
        public float contactStiffnessK = 1.2E5f;
        [Tooltip("Frottement de contact")]
        public float contactFrictionKappa = 2.4E5f;
        
        [Header("SFM - Wall Forces")]
        [Tooltip("Force d'interaction avec les murs")]
        public float wallForceA = 600f * 4f; // 2400
        [Tooltip("Portée de l'interaction avec les murs")]
        public float wallForceB = 0.04f * 3f; // 0.12
        [Tooltip("Rigidité du contact avec les murs")]
        public float wallContactStiffnessK = 1.2E5f;
        [Tooltip("Frottement de contact avec les murs")]
        public float wallContactFrictionKappa = 2.4E5f;
        
        [Header("SFM - Tangential Forces")]
        [Tooltip("Force tangentielle")]
        public float tangentialForceA = 2000f;
        [Tooltip("Portée de la force tangentielle")]
        public float tangentialForceB = 0.08f;
        
        [Header("SFM - Navigation")]
        [Tooltip("Distance pour atteindre un waypoint")]
        public float nextNavMinDistance = 0.3f;
        [Tooltip("Distance pour atteindre le but final")]
        public float closeEnoughMinDistance = 0.2f;
        
        [Header("SFM - Dampening")]
        [Tooltip("Amortissement du mouvement arrière")]
        public float backwardDampening = 20f;
        [Tooltip("Amortissement du mouvement latéral")]
        public float lateralDampening = 5f;
        [Tooltip("Amortissement min de répulsion des robots")]
        public float robotRepulsionDampeningMin = 0.5f;
        [Tooltip("Amortissement max de répulsion des robots")]
        public float robotRepulsionDampeningMax = 1.0f;
        
        // ONNX Model Parameters
        public ONNXModelConfig onnxModelConfig;
        
        // Hybrid Parameters
        [Range(0f, 1f)] public float predictionWeight = 0.6f;
        [Range(0f, 1f)] public float navigationWeight = 0.4f;
        public bool useAdaptiveWeighting = true;
        
        // Animation Parameters
        public RuntimeAnimatorController animationController;
        public float animationSmoothing = 0.6f;
        public float idleSpeedThreshold = 0.5f;
        public float angularSpeed = 180f;
        
        // Navigation
        public float goalReachedDistance = 0.2f;
        public float pathUpdateInterval = 0.2f;
        
        // Visualization
        public bool showPredictionGizmos = true;
        public Color predictionColor = Color.yellow;
        public Color trajectoryColor = Color.blue;
        
        // Read-only properties
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
        SFM,
        ONNXPrediction,
        Hybrid
    }
}