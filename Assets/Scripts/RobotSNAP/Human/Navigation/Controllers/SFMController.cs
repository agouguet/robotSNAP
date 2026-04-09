using UnityEngine;
using RobotSNAP.Movement.Interfaces;
using RobotSNAP.Human;

namespace RobotSNAP.Movement.Controllers
{
    /// <summary>
    /// Contrôleur SFM - Suit le point NavMesh fourni par HumanMovement
    /// </summary>
    public class SFMController : IMovementController
    {
        // Constantes physiques
        private const float MASS = 80f;
        private const float HUMAN_RADIUS = 0.25f;
        private const float ROBOT_RADIUS = 0.2f;
        
        private HumanConfig config;
        private Vector2 previousVelocity;
        
        public SFMController(HumanConfig config)
        {
            this.config = config;
            previousVelocity = Vector2.zero;
        }
        
        public Vector2 ComputeVelocity(
            Vector2 currentPosition,
            Vector2 currentVelocity,
            Vector2 goalPosition,
            Vector2[] neighbors,
            Vector2[] neighborVelocities,
            float deltaTime)
        {
            // 1. Force d'attraction vers le point NavMesh
            Vector2 goalForce = CalculateGoalForce(currentPosition, currentVelocity, goalPosition);
            
            // 2. Force de répulsion et d'anticipation des voisins
            Vector2 neighborForce = CalculateNeighborForce(currentPosition, currentVelocity, neighbors, neighborVelocities);
            
            // Force totale
            Vector2 totalForce = goalForce + neighborForce;
            
            // Appliquer les dampenings
            totalForce = ApplyDampening(totalForce, currentVelocity);
            
            // Calculer l'accélération
            Vector2 acceleration = totalForce / MASS;
            
            // Nouvelle vélocité
            Vector2 newVelocity = currentVelocity + acceleration * deltaTime;
            
            // Limiter la vitesse maximale
            if (newVelocity.sqrMagnitude > config.maxSpeed * config.maxSpeed)
            {
                newVelocity = newVelocity.normalized * config.maxSpeed;
            }
            
            // Lissage
            newVelocity = Vector2.Lerp(currentVelocity, newVelocity, config.animationSmoothing);
            
            previousVelocity = newVelocity;
            return newVelocity;
        }
        
        private Vector2 CalculateGoalForce(Vector2 position, Vector2 velocity, Vector2 goal)
        {
            Vector2 direction = goal - position;
            float distance = direction.magnitude;
            
            if (distance < config.goalReachedDistance) return Vector2.zero;
            
            direction.Normalize();
            
            // Vitesse désirée = vitesse configurée
            float desiredSpeed = config.desiredSpeed;
            
            Vector2 desiredVelocity = direction * desiredSpeed;
            
            // Force d'attraction standard
            return MASS * (desiredVelocity - velocity) / config.relaxationTime;
        }
        
        private Vector2 CalculateNeighborForce(Vector2 position, Vector2 velocity, Vector2[] neighbors, Vector2[] neighborVelocities)
        {
            Vector2 totalForce = Vector2.zero;
            
            for (int i = 0; i < neighbors.Length; i++)
            {
                Vector2 toNeighbor = neighbors[i] - position;
                float distance = toNeighbor.magnitude;
                
                if (distance >= config.perceptionRadiusAgent || distance < 0.01f)
                    continue;
                
                Vector2 dir = (position - neighbors[i]).normalized;
                float combinedRadius = HUMAN_RADIUS + ROBOT_RADIUS;
                float overlap = combinedRadius - distance;
                
                // Force sociale exponentielle
                float socialForceMagnitude = config.socialForceA * Mathf.Exp(overlap / config.socialForceB);
                Vector2 socialForce = socialForceMagnitude * dir;
                
                // Force de contact physique
                Vector2 contactForce = Vector2.zero;
                if (overlap > 0)
                {
                    // Force normale
                    contactForce += config.contactStiffnessK * overlap * dir;
                    
                    // Force tangentielle (frottement)
                    if (neighborVelocities.Length > i)
                    {
                        Vector2 relativeVelocity = velocity - neighborVelocities[i];
                        Vector2 tangentialVelocity = relativeVelocity - Vector2.Dot(relativeVelocity, dir) * dir;
                        contactForce += config.contactFrictionKappa * overlap * tangentialVelocity;
                    }
                }
                
                totalForce += socialForce + contactForce;
                
                // Force d'évitement anticipatif (basée sur le temps avant collision)
                if (neighborVelocities.Length > i)
                {
                    Vector2 relativeVelocity = velocity - neighborVelocities[i];
                    float approachSpeed = -Vector2.Dot(toNeighbor.normalized, relativeVelocity);
                    
                    if (approachSpeed > 0.1f)
                    {
                        float timeToCollision = distance / approachSpeed;
                        
                        // Anticiper la collision si elle arrive dans les 2 secondes
                        if (timeToCollision < 2.0f && timeToCollision > 0)
                        {
                            // Force d'évitement proportionnelle à l'inverse du temps
                            float avoidanceStrength = config.socialForceA * (1.0f / (timeToCollision + 0.5f));
                            
                            // Direction latérale pour contourner
                            Vector2 lateralDir = new Vector2(-toNeighbor.y, toNeighbor.x).normalized;
                            float sideSign = Mathf.Sign(Vector2.Dot(toNeighbor, lateralDir));
                            
                            Vector2 avoidanceForce = sideSign * avoidanceStrength * lateralDir;
                            totalForce += avoidanceForce;
                        }
                    }
                }
            }
            
            return totalForce;
        }
        
        private Vector2 ApplyDampening(Vector2 force, Vector2 velocity)
        {
            if (velocity.sqrMagnitude < 0.01f) return force;
            
            Vector2 forward = velocity.normalized;
            Vector2 right = new Vector2(-forward.y, forward.x);
            
            float forwardDot = Vector2.Dot(forward, force);
            float rightDot = Vector2.Dot(right, force);
            
            Vector2 forwardProj = forward * forwardDot;
            Vector2 rightProj = right * rightDot / config.lateralDampening;
            
            // Dampening arrière
            if (forwardDot < 0)
            {
                forwardProj /= config.backwardDampening;
            }
            
            return forwardProj + rightProj;
        }
        
        public void Reset() 
        { 
            previousVelocity = Vector2.zero;
        }
        
        public float GetConfidence() 
        { 
            return 0.5f; 
        }
    }
}