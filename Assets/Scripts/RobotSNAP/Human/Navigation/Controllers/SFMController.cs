using UnityEngine;
using RobotSNAP.Movement.Interfaces;
using RobotSNAP.Human;

namespace RobotSNAP.Movement.Controllers
{
    /// <summary>
    /// Contrôleur SFM - Calcule uniquement la force sociale
    /// La navigation et les collisions sont gérées par HumanMovement
    /// </summary>
    public class LegacySFMController : IMovementController
    {
        // Paramètres SFM
        private const float A = 2000f;           // Force d'interaction
        private const float B = 0.08f;           // Échelle de distance
        private const float MASS = 80f;          // Masse
        private const float T = 0.5f;            // Temps de relaxation
        private const float LATERAL_DAMPENING = 5f;
        
        private HumanConfig config;
        
        public LegacySFMController(HumanConfig config)
        {
            this.config = config;
        }
        
        public Vector2 ComputeVelocity(
            Vector2 currentPosition,
            Vector2 currentVelocity,
            Vector2 goalPosition,
            Vector2[] neighbors,
            Vector2[] neighborVelocities,
            float deltaTime)
        {
            // Calculer la force d'attraction vers le goal
            Vector2 goalForce = CalculateGoalForce(currentPosition, currentVelocity, goalPosition);
            
            // Calculer la force de répulsion des voisins
            Vector2 neighborForce = CalculateNeighborForce(currentPosition, currentVelocity, goalPosition, neighbors, neighborVelocities);
            
            // Force totale
            Vector2 totalForce = goalForce + neighborForce;
            
            // Dampening latéral
            totalForce = ApplyLateralDamping(totalForce, currentVelocity);
            
            // Calculer l'accélération
            Vector2 acceleration = totalForce / MASS;
            
            // Force minimale pour "décoller" à basse vitesse
            float idleSpeedThreshold = 0.5f;
            float minAcceleration = 0.5f;
            if (currentVelocity.magnitude < idleSpeedThreshold && acceleration.magnitude < minAcceleration)
            {
                Vector2 toGoal = (goalPosition - currentPosition).normalized;
                acceleration = toGoal * minAcceleration;
            }
            
            // Nouvelle vélocité
            Vector2 newVelocity = currentVelocity + acceleration * deltaTime;
            
            // Limiter la vitesse maximale
            float maxSpeed = config.sfmMaxSpeed;
            if (newVelocity.sqrMagnitude > maxSpeed * maxSpeed)
            {
                newVelocity = newVelocity.normalized * maxSpeed;
            }
            
            // Lissage
            newVelocity = Vector2.Lerp(currentVelocity, newVelocity, 0.5f);
            
            return newVelocity;
        }
        
        private Vector2 CalculateGoalForce(Vector2 position, Vector2 velocity, Vector2 goal)
        {
            Vector2 direction = goal - position;
            if (direction.magnitude < 0.01f) return Vector2.zero;
            
            direction.Normalize();
            Vector2 desiredVelocity = direction * config.sfmMaxSpeed;
            
            return MASS * (desiredVelocity - velocity) / T;
        }
        
        private Vector2 CalculateNeighborForce(Vector2 position, Vector2 velocity, Vector2 goal, Vector2[] neighbors, Vector2[] neighborVelocities)
        {
            Vector2 totalForce = Vector2.zero;
            
            for (int i = 0; i < neighbors.Length; i++)
            {
                Vector2 dir = position - neighbors[i];
                float distance = dir.magnitude;
                
                if (distance >= config.sfmInteractionRadius || distance < 0.01f)
                    continue;
                
                dir.Normalize();
                float overlap = 2 * 0.25f - distance; // RADIUS = 0.25f
                overlap += 0.5f;
                
                // Force de répulsion exponentielle
                totalForce += A * Mathf.Exp(overlap / B) * dir;
                
                // Détection des agents devant et approchant
                Vector2 goalDir = (goal - position).normalized;
                Vector2 neighborVel = neighborVelocities.Length > i ? neighborVelocities[i] : Vector2.zero;
                
                bool inFront = Vector2.Dot(-dir, goalDir) >= 0.5;
                bool approaching = Vector2.Dot(goalDir, neighborVel.normalized) < 0;
                
                if (inFront && approaching && neighborVel.magnitude > 0.1f)
                {
                    float sideStepScale = -Vector2.Dot(-dir, goalDir);
                    totalForce += sideStepScale * A / 10f * Tangent(goalDir);
                }
            }
            
            return totalForce;
        }
        
        private Vector2 ApplyLateralDamping(Vector2 force, Vector2 velocity)
        {
            Vector2 forward = velocity.normalized;
            if (forward.sqrMagnitude < 0.01f) return force;
            
            Vector2 right = new Vector2(-forward.y, forward.x);
            
            float forwardDot = Vector2.Dot(forward, force);
            float rightDot = Vector2.Dot(right, force);
            
            Vector2 forwardProj = forward * forwardDot;
            Vector2 rightProj = right * rightDot;
            
            return forwardProj + rightProj / LATERAL_DAMPENING;
        }
        
        private Vector2 Tangent(Vector2 vector)
        {
            return new Vector2(-vector.y, vector.x);
        }
        
        public void Reset() { }
        public float GetConfidence() => 0.5f;
    }
}