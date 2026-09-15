using UnityEngine;
using System.Collections.Generic;
using RobotSNAP.Agents.Movement.Interfaces;
using RobotSNAP.Movement.Predictors;
using RobotSNAP.Agents;

namespace RobotSNAP.Agents.Movement.Controllers
{
    /// <summary>
    /// Contrôleur utilisant un modèle ONNX pour prédire la trajectoire future.
    /// La vélocité est dérivée de la prédiction du prochain point.
    /// </summary>
    public class ONNXPredictionController : IMovementController
    {
        private readonly HumanConfig _config;
        private readonly ONNXHumanPredictor _predictor;
        private Vector2[] _currentPredictions;   // Positions prédites [sampleIndex]
        private float _lastUpdateTime;
        private float _confidence = 0.5f;
        private Vector2[] _trajectoryBuffer;
        private int _bufferIndex;
        private readonly List<Vector2> _neighborsWithRobot = new List<Vector2>();
        // Tampons réutilisés par UpdatePrediction : le prédicteur n'accepte que des tableaux, on évite donc
        // d'en allouer un par mise à jour de prédiction.
        private readonly Vector2[] _historyBuffer = new Vector2[TRAJECTORY_LENGTH];
        private Vector2[] _neighborBuffer = new Vector2[0];
        private const int TRAJECTORY_LENGTH = 8;  // Nombre de points d'historique utilisés par le modèle

        public ONNXPredictionController(HumanConfig config)
        {
            _config = config;
            _predictor = new ONNXHumanPredictor(config.onnxModelPath, config.onnxUseGPU);
            _currentPredictions = new Vector2[config.predictionSamples];
            _trajectoryBuffer = new Vector2[TRAJECTORY_LENGTH];
            _bufferIndex = 0;
        }

        public Vector2 ComputeVelocity(
            Vector2 currentPosition,
            Vector2 currentVelocity,
            Vector2 goalPosition,
            IReadOnlyList<Vector2> neighbors,
            IReadOnlyList<Vector2> neighborVelocities,
            IReadOnlyList<Vector2> staticObstacles,
            RobotObservation robot,
            float deltaTime,
            float cruiseSpeedOverride = 0f)
        {
            // Mise à jour du buffer de trajectoire (historique)
            _trajectoryBuffer[_bufferIndex] = currentPosition;
            _bufferIndex = (_bufferIndex + 1) % TRAJECTORY_LENGTH;

            // Mise à jour périodique de la prédiction
            float updateInterval = 1f / Mathf.Max(1f, _config.predictionUpdateRate);
            if (Time.time - _lastUpdateTime >= updateInterval)
            {
                _lastUpdateTime = Time.time;
                UpdatePrediction(currentPosition, currentVelocity, goalPosition, WithRobot(neighbors, robot));
            }

            // Calcul de la direction à partir de la première prédiction (prochain point)
            Vector2 predictedDirection = (_currentPredictions.Length > 0 && _currentPredictions[0] != Vector2.zero)
                ? _currentPredictions[0] - currentPosition
                : goalPosition - currentPosition;

            if (predictedDirection.sqrMagnitude > 0.01f)
                predictedDirection.Normalize();
            else
                predictedDirection = (goalPosition - currentPosition).normalized;

            // Bruit pour diversité (plus de bruit quand la confiance est faible)
            float noiseStrength = (1f - _confidence) * 0.3f;
            if (noiseStrength > 0)
            {
                predictedDirection += Random.insideUnitCircle * noiseStrength;
                predictedDirection.Normalize();
            }

            // Vitesse adaptée à la confiance (plus confiant → plus rapide).
            // A group follower may be asked for a higher cruise speed so it can close the gap.
            float cruiseSpeed = cruiseSpeedOverride > 0.01f
                ? Mathf.Min(cruiseSpeedOverride, _config.maxSpeed)
                : _config.maxSpeed;
            float speed = cruiseSpeed * (0.6f + _confidence * 0.4f);
            return predictedDirection * speed;
        }

        /// <summary>
        /// Le robot est ajouté aux voisins observés pour que le modèle puisse réagir à lui.
        /// La liste interne est réutilisée : elle est vidée avant remplissage et consommée immédiatement
        /// par <see cref="UpdatePrediction"/>, donc aucun tableau temporaire n'est nécessaire.
        /// </summary>
        private IReadOnlyList<Vector2> WithRobot(IReadOnlyList<Vector2> neighbors, RobotObservation robot)
        {
            if (!robot.IsVisible)
                return neighbors;

            int count = neighbors?.Count ?? 0;
            _neighborsWithRobot.Clear();
            for (int index = 0; index < count; index++)
                _neighborsWithRobot.Add(neighbors[index]);
            _neighborsWithRobot.Add(robot.Position);
            return _neighborsWithRobot;
        }

        private void UpdatePrediction(Vector2 currentPos, Vector2 currentVel, Vector2 goal, IReadOnlyList<Vector2> neighbors)
        {
            // Construction de l'historique de trajectoire (ordre temporel), dans le tampon réutilisé
            for (int i = 0; i < TRAJECTORY_LENGTH; i++)
            {
                int idx = (_bufferIndex - i - 1 + TRAJECTORY_LENGTH) % TRAJECTORY_LENGTH;
                _historyBuffer[i] = _trajectoryBuffer[idx];
            }

            // ONNXHumanPredictor déduit ses features de neighbors.Length : le tampon passé doit donc
            // contenir exactement les voisins courants, sinon les features sont fausses.
            int neighborCount = neighbors?.Count ?? 0;
            if (_neighborBuffer.Length != neighborCount)
                System.Array.Resize(ref _neighborBuffer, neighborCount);
            for (int i = 0; i < neighborCount; i++)
                _neighborBuffer[i] = neighbors[i];

            // Appel au prédicteur ONNX
            var result = _predictor.PredictTrajectory(_historyBuffer, _neighborBuffer, goal, _config.predictionSamples);
            if (result != null && result.predictions != null && result.predictions.Length > 0)
            {
                // On garde la première prédiction (échantillon le plus probable)
                _currentPredictions = result.predictions[0];
                _confidence = result.GetMeanConfidence();
            }
            else
            {
                // Fallback : utiliser le goal direct
                _currentPredictions = new Vector2[] { goal };
                _confidence = 0.3f;
            }
        }

        public void Reset()
        {
            System.Array.Clear(_currentPredictions, 0, _currentPredictions.Length);
            System.Array.Clear(_trajectoryBuffer, 0, TRAJECTORY_LENGTH);
            _bufferIndex = 0;
            _confidence = 0.5f;
            _lastUpdateTime = 0f;
        }

        public float GetConfidence() => _confidence;

        public void UpdateParameters(HumanConfig config)
        {
            // Permet de mettre à jour la config à chaud (ex: changement de modèle)
            // Note : si le chemin du modèle change, il faudrait recharger le prédicteur.
            if (!string.Equals(_config.onnxModelPath, config.onnxModelPath))
            {
                // Recharger le modèle (logique à implémenter dans ONNXHumanPredictor)
                // _predictor.LoadModel(config.onnxModelPath, config.onnxUseGPU);
            }
            // Mise à jour de la référence (utilisée pour les paramètres de vitesse etc.)
            // On ne peut pas remplacer _config directement car readonly, mais on peut rafraîchir les paramètres utilisés.
        }
    }
}
