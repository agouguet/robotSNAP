using UnityEngine;
using RobotSNAP.Movement.Interfaces;
using RobotSNAP.Movement.Predictors;
using RobotSNAP.Human;

namespace RobotSNAP.Movement.Controllers
{
    public class ONNXPredictionController : IMovementController
    {
        private HumanConfig config;
        private ONNXHumanPredictor predictor;
        private Vector2[] currentPredictions;
        private float lastUpdateTime;
        private float confidence = 0.5f;
        
        public ONNXPredictionController(HumanConfig config)
        {
            this.config = config;
            predictor = new ONNXHumanPredictor(config.onnxModelPath, config.onnxUseGPU);
            currentPredictions = new Vector2[4];
        }
        
        public Vector2 ComputeVelocity(
            Vector2 currentPosition,
            Vector2 currentVelocity,
            Vector2 goalPosition,
            Vector2[] neighbors,
            Vector2[] neighborVelocities,
            float deltaTime)
        {
            Debug.Log($"Computing ONNX velocity. Current position: {currentPosition}, velocity: {currentVelocity}, goal: {goalPosition}, neighbors: {neighbors.Length}");
            // Mettre à jour la prédiction périodiquement
            if (Time.time - lastUpdateTime > 1f / config.predictionUpdateRate)
            {
                lastUpdateTime = Time.time;
                
                // Construire l'historique (simplifié - à adapter selon ton dataset)
                Vector2[] trajectory = new Vector2[8];
                for (int i = 0; i < 8 && i < currentPredictions.Length; i++)
                {
                    trajectory[i] = currentPosition - currentVelocity * (i + 1) * deltaTime;
                }
                
                var result = predictor.PredictTrajectory(trajectory, neighbors, goalPosition, config.predictionSamples);
                Debug.Log($"ONNX Prediction: {result?.predictions.Length} samples, confidence: {result?.GetMeanConfidence():F2}");
                if (result != null && result.predictions.Length > 0)
                {
                    currentPredictions = result.predictions[0];
                    confidence = result.GetMeanConfidence();
                }
            }
            
            // Utiliser la prédiction pour la direction
            Vector2 predictedDirection = currentPredictions[0] - currentPosition;
            if (predictedDirection.magnitude > 0.1f)
                predictedDirection.Normalize();
            
            // Ajouter du bruit pour la diversité
            float noiseAmount = (1f - confidence) * 0.3f;
            predictedDirection += Random.insideUnitCircle * noiseAmount;
            predictedDirection.Normalize();
            
            // Calculer la vélocité
            float speed = config.sfmMaxSpeed * (0.5f + confidence * 0.5f);
            return predictedDirection * speed;
        }
        
        public void Reset()
        {
            System.Array.Clear(currentPredictions, 0, currentPredictions.Length);
            confidence = 0.5f;
        }
        
        public float GetConfidence() => confidence;
    }
}