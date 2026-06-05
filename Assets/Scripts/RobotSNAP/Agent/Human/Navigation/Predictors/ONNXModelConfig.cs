using UnityEngine;

namespace RobotSNAP.Movement.Predictors
{
    [CreateAssetMenu(fileName = "ONNXModelConfig", menuName = "RobotSNAP/ONNX Model Config")]
    public class ONNXModelConfig : ScriptableObject
    {
        [Header("Model Settings")]
        [Tooltip("Chemin du fichier .onnx dans le dossier Resources (ex: Models/policy_lstm)")]
        public string modelPath = "Models/policy_lstm";
        
        [Tooltip("Utiliser le GPU pour l'inférence (recommandé)")]
        public bool useGPU = true;
        
        [Tooltip("Utiliser une prédiction de fallback en cas d'erreur")]
        public bool useFallback = true;
        
        [Header("Prediction Settings")]
        [Tooltip("Nombre d'échantillons à générer pour la diversité")]
        [Range(1, 20)]
        public int numSamples = 5;
        
        [Tooltip("Fréquence de mise à jour des prédictions (Hz)")]
        [Range(1f, 60f)]
        public float updateRate = 10f;
        
        [Header("Input Normalization")]
        public Vector2 mean = Vector2.zero;
        public Vector2 std = Vector2.one;
        
        [Header("Debug")]
        public bool logPredictions = false;
        public bool visualizePredictions = true;
        
        public void Validate()
        {
            numSamples = Mathf.Clamp(numSamples, 1, 20);
            updateRate = Mathf.Clamp(updateRate, 1f, 60f);
            
            if (std.x < 0.01f) std.x = 1f;
            if (std.y < 0.01f) std.y = 1f;
        }
    }
}