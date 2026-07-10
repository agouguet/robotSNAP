using UnityEngine;
using RobotSNAP.Agents.Movement.Interfaces;

namespace RobotSNAP.Agents.Movement.Controllers
{
    /// <summary>
    /// SFM minimaliste : va vers le but et s'arrête, sans interactions sociales.
    /// </summary>
    public class SFMController : IMovementController
    {
        private HumanConfig _config;
        private float _desiredSpeed;
        private float _mass;
        private float _relaxationTime;

        public SFMController(HumanConfig config, HumanAgent avatar)
        {
            _config = config;
            _desiredSpeed = config.desiredSpeed;
            _mass = config.agentMass;
            _relaxationTime = config.relaxationTime;
        }

        public Vector2 ComputeVelocity(
            Vector2 currentPosition,
            Vector2 currentVelocity,
            Vector2 goalPosition,
            Vector2[] neighbors,
            Vector2[] neighborVelocities,
            float deltaTime)
        {
            Vector2 toGoal = goalPosition - currentPosition;
            float distance = toGoal.magnitude;

            // Arrêt net si proche
            if (distance < _config.goalReachedDistance)
                return Vector2.zero;

            // Direction normalisée
            Vector2 direction = toGoal / distance;

            // Vitesse désirée : proportionnelle à la distance, avec un maximum
            float desiredSpeed = Mathf.Min(_desiredSpeed, distance * 2f); // si distance petite, on réduit

            // Si on est dans la zone de ralentissement, on réduit encore
            if (distance < _config.slowDownDistance)
            {
                float t = distance / _config.slowDownDistance;
                desiredSpeed *= t; // linéaire (ou t*t pour plus de douceur)
            }

            // On veut que la vitesse soit exactement direction * desiredSpeed, sans inertie
            // Pour éviter les oscillations, on peut directement définir la vélocité cible
            Vector2 targetVelocity = direction * desiredSpeed;

            // On peut appliquer un lissage minimal pour éviter les à-coups
            float smoothFactor = 0.9f; // 1 = sans lissage, plus petit = plus lisse
            Vector2 newVelocity = Vector2.Lerp(currentVelocity, targetVelocity, smoothFactor);

            // Limiter la vitesse max
            float maxSpeed = _config.maxSpeed;
            if (newVelocity.sqrMagnitude > maxSpeed * maxSpeed)
                newVelocity = newVelocity.normalized * maxSpeed;

            return newVelocity;
        }

        public void Reset() { }
        public float GetConfidence() => 1f;
        public void UpdateParameters(HumanConfig config)
        {
            _config = config;
            _desiredSpeed = config.desiredSpeed;
            _mass = config.agentMass;
            _relaxationTime = config.relaxationTime;
        }
    }
}