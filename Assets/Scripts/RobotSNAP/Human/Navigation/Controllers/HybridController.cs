using UnityEngine;
using RobotSNAP.Human;
using RobotSNAP.Movement.Interfaces;

namespace RobotSNAP.Movement.Controllers
{
    /// <summary>
    /// Contrôleur hybride fusionnant SFM et ONNX
    /// </summary>
    public class HybridController : IMovementController
    {
        private SFMController _sfmController;
        private ONNXPredictionController _onnxController;
        private HumanConfig _config;
        private Vector2 _lastHybridVelocity;
        private float _currentOnnxWeight;
        private float _currentSfmWeight;
        
        public HybridController(HumanConfig config)
        {
            _config = config;
            _sfmController = new SFMController(config);
            _onnxController = new ONNXPredictionController(config);
            _currentOnnxWeight = config.predictionWeight;
            _currentSfmWeight = 1f - config.predictionWeight;
        }
        
        public Vector2 ComputeVelocity(
            Vector2 currentPosition,
            Vector2 currentVelocity,
            Vector2 goalPosition,
            Vector2[] neighbors,
            Vector2[] neighborVelocities,
            float deltaTime)
        {
            // Calcul des vélocités des deux contrôleurs
            Vector2 sfmVelocity = _sfmController.ComputeVelocity(
                currentPosition, currentVelocity, goalPosition, neighbors, neighborVelocities, deltaTime);
            
            Vector2 onnxVelocity = _onnxController.ComputeVelocity(
                currentPosition, currentVelocity, goalPosition, neighbors, neighborVelocities, deltaTime);
            
            // Adaptation dynamique des poids si activée
            if (_config.useAdaptiveWeighting)
            {
                float sfmConfidence = _sfmController.GetConfidence();
                float onnxConfidence = _onnxController.GetConfidence();
                
                // Plus la confiance est élevée, plus le poids est grand
                float targetOnnxWeight = Mathf.Lerp(0.2f, 0.9f, onnxConfidence);
                targetOnnxWeight = Mathf.Clamp(targetOnnxWeight, 0.2f, 0.9f);
                
                _currentOnnxWeight = Mathf.Lerp(_currentOnnxWeight, targetOnnxWeight, _config.hybridAdaptationRate);
                _currentSfmWeight = 1f - _currentOnnxWeight;
            }
            
            // Fusion pondérée
            Vector2 hybridVelocity = (sfmVelocity * _currentSfmWeight) + (onnxVelocity * _currentOnnxWeight);
            
            // Lissage temporel pour éviter les changements brusques
            hybridVelocity = Vector2.Lerp(_lastHybridVelocity, hybridVelocity, 0.5f);
            _lastHybridVelocity = hybridVelocity;
            
            return hybridVelocity;
        }
        
        public void Reset()
        {
            _sfmController.Reset();
            _onnxController.Reset();
            _lastHybridVelocity = Vector2.zero;
            _currentOnnxWeight = _config.predictionWeight;
            _currentSfmWeight = 1f - _config.predictionWeight;
        }
        
        public float GetConfidence()
        {
            return Mathf.Lerp(
                _sfmController.GetConfidence(),
                _onnxController.GetConfidence(),
                _currentOnnxWeight
            );
        }
        
        public void UpdateParameters(HumanConfig config)
        {
            _config = config;
            _sfmController.UpdateParameters(config);
            _onnxController.UpdateParameters(config);
        }
    }
}