using UnityEngine;
using System.Collections.Generic;
using RobotSNAP.Agents.Movement.Interfaces;
using RobotSNAP.Agents;

namespace RobotSNAP.Agents.Movement.Controllers
{
    /// <summary>
    /// Contrôleur SFM (Social Force Model) - Simule le mouvement basé sur les forces sociales
    /// Suit le point NavMesh fourni par HumanMovement
    /// </summary>
    public class SFMController : IMovementController, ISocialForceAgent
    {
        #region Constantes
        
        private const float DEFAULT_MASS = 80f;
        private const float DEFAULT_HUMAN_RADIUS = 0.25f;
        private const float DEFAULT_ROBOT_RADIUS = 0.2f;
        
        #endregion
        
        #region Paramètres personnalisables (pour le système de scénarios)
        
        private float _assertiveness = 0.5f;      // 0 = timide, 1 = agressif
        private float _personalSpace = 0.8f;       // Rayon d'espace personnel
        private float _interactionRadius = 1.5f;   // Rayon d'interaction sociale
        private float _reactionTime = 0.3f;        // Temps de réaction (secondes)
        
        #endregion
        
        #region Champs privés
        
        private HumanConfig _config;
        private Vector2 _previousVelocity;
        private float _confidence = 0.85f;
        private Transform _transform;
        private Rigidbody _rigidbody;
        private float _desiredSpeed;
        private float _mass = DEFAULT_MASS;
        private float _radius = DEFAULT_HUMAN_RADIUS;
        
        #endregion
        
        #region Propriétés (pour interface ISocialForceAgent)
        
        public Transform Transform => _transform;
        public Rigidbody RB => _rigidbody;
        public UnityEngine.AI.NavMeshPath NavPath { get; set; }
        public HashSet<GameObject> Neighbors { get; } = new HashSet<GameObject>();
        public HashSet<GameObject> Obstacles { get; } = new HashSet<GameObject>();
        public float DesiredSpeed => _desiredSpeed;
        public float Mass => _mass;
        public float Radius => _radius;
        public float RobotRadius => DEFAULT_ROBOT_RADIUS;
        public float RobotRepulsion => _config.robotRepulsionStrength;
        public Vector3 Velocity { get; set; }
        public Vector3 NearestGoalPoint { get; set; }
        
        #endregion
        
        #region Constructeur
        
        public SFMController(HumanConfig config, HumanAgent agent)
        {
            _config = config;
            _previousVelocity = Vector2.zero;
            _mass = config.agentMass > 0 ? config.agentMass : DEFAULT_MASS;
            _radius = config.agentRadius > 0 ? config.agentRadius : DEFAULT_HUMAN_RADIUS;
            
            _assertiveness = agent.Assertiveness;
            _personalSpace = agent.PersonalSpace;
            _interactionRadius = agent.InteractionRadius;
            _reactionTime = agent.ReactionTime;
            _desiredSpeed = agent.DesiredSpeed;
        }
        
        #endregion
        
        #region IMovementController Implementation
        
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
            
            // 3. Force d'évitement du robot (si détecté)
            Vector2 robotForce = CalculateRobotForce(currentPosition, currentVelocity);
            
            // Force totale
            Vector2 totalForce = goalForce + neighborForce + robotForce;
            
            // Appliquer les dampenings directionnels
            totalForce = ApplyDirectionalDampening(totalForce, currentVelocity);
            
            // Calculer l'accélération
            Vector2 acceleration = totalForce / _mass;
            
            // Nouvelle vélocité
            Vector2 newVelocity = currentVelocity + acceleration * deltaTime;
            
            // Limiter la vitesse maximale
            float maxSpeed = _config.maxSpeed * GetSpeedMultiplierFromBehavior();
            if (newVelocity.sqrMagnitude > maxSpeed * maxSpeed)
            {
                newVelocity = newVelocity.normalized * maxSpeed;
            }
            
            // Appliquer le temps de réaction (lissage)
            float reactionFactor = Mathf.Clamp01(deltaTime / _reactionTime);
            newVelocity = Vector2.Lerp(currentVelocity, newVelocity, reactionFactor);
            
            _previousVelocity = newVelocity;
            return newVelocity;
        }
        
        public void Reset()
        {
            _previousVelocity = Vector2.zero;
            _confidence = 0.85f;
            Neighbors.Clear();
            Obstacles.Clear();
        }
        
        public float GetConfidence() => _confidence;
        
        public void UpdateParameters(HumanConfig config)
        {
            _config = config;
            _desiredSpeed = config.desiredSpeed;
            _mass = config.agentMass > 0 ? config.agentMass : DEFAULT_MASS;
            _radius = config.agentRadius > 0 ? config.agentRadius : DEFAULT_HUMAN_RADIUS;
        }
        
        #endregion
        
        #region Calcul des forces
        
        private Vector2 CalculateGoalForce(Vector2 position, Vector2 velocity, Vector2 goal)
        {
            Vector2 direction = goal - position;
            float distance = direction.magnitude;
            
            if (distance < _config.goalReachedDistance) 
                return Vector2.zero;
            
            direction.Normalize();
            
            // Vitesse désirée = vitesse configurée * assertivité
            float desiredSpeed = _desiredSpeed * (0.5f + _assertiveness);
            
            // Ralentir à l'approche du goal
            if (distance < _config.slowDownDistance)
            {
                float t = distance / _config.slowDownDistance;
                desiredSpeed *= Mathf.Lerp(0.3f, 1f, t);
            }
            
            Vector2 desiredVelocity = direction * desiredSpeed;
            
            // Force d'attraction standard (F = m * (v_desired - v) / τ)
            return _mass * (desiredVelocity - velocity) / _config.relaxationTime;
        }
        
        private Vector2 CalculateNeighborForce(Vector2 position, Vector2 velocity, Vector2[] neighbors, Vector2[] neighborVelocities)
        {
            Vector2 totalForce = Vector2.zero;
            
            for (int i = 0; i < neighbors.Length; i++)
            {
                Vector2 toNeighbor = neighbors[i] - position;
                float distance = toNeighbor.magnitude;
                
                if (distance >= _interactionRadius || distance < 0.01f)
                    continue;
                
                Vector2 direction = (position - neighbors[i]).normalized;
                float combinedRadius = _radius + DEFAULT_HUMAN_RADIUS;
                float overlap = combinedRadius - distance;
                
                // Force sociale exponentielle (Helbing's model)
                float socialForceMagnitude = _config.socialForceA * Mathf.Exp(overlap / _config.socialForceB);
                socialForceMagnitude *= (1f - _assertiveness * 0.5f); // Les assertifs subissent moins de force sociale
                Vector2 socialForce = socialForceMagnitude * direction;
                
                // Force de contact physique (quand il y a collision)
                Vector2 contactForce = CalculateContactForce(overlap, direction, velocity, neighborVelocities, i);
                
                // Force d'évitement anticipatif
                Vector2 avoidanceForce = CalculateAvoidanceForce(position, velocity, toNeighbor, distance, neighborVelocities, i);
                
                totalForce += socialForce + contactForce + avoidanceForce;
            }
            
            return totalForce;
        }
        
        private Vector2 CalculateContactForce(float overlap, Vector2 direction, Vector2 velocity, Vector2[] neighborVelocities, int index)
        {
            Vector2 contactForce = Vector2.zero;
            
            if (overlap > 0)
            {
                // Force normale (répulsion)
                contactForce += _config.contactStiffnessK * overlap * direction;
                
                // Force tangentielle (frottement)
                if (neighborVelocities.Length > index)
                {
                    Vector2 relativeVelocity = velocity - neighborVelocities[index];
                    Vector2 tangentialVelocity = relativeVelocity - Vector2.Dot(relativeVelocity, direction) * direction;
                    contactForce += _config.contactFrictionKappa * overlap * tangentialVelocity;
                }
            }
            
            return contactForce;
        }
        
        private Vector2 CalculateAvoidanceForce(Vector2 position, Vector2 velocity, Vector2 toNeighbor, float distance, Vector2[] neighborVelocities, int index)
        {
            Vector2 avoidanceForce = Vector2.zero;
            
            if (neighborVelocities.Length > index)
            {
                Vector2 relativeVelocity = velocity - neighborVelocities[index];
                float approachSpeed = -Vector2.Dot(toNeighbor.normalized, relativeVelocity);
                
                if (approachSpeed > 0.1f)
                {
                    float timeToCollision = distance / approachSpeed;
                    
                    // Anticiper la collision si elle arrive dans le temps de réaction * 3
                    if (timeToCollision < _reactionTime * 3f && timeToCollision > 0)
                    {
                        // Force d'évitement proportionnelle à l'inverse du temps
                        float avoidanceStrength = _config.socialForceA * (1.0f / (timeToCollision + 0.5f));
                        avoidanceStrength *= _assertiveness; // Les assertifs évitent moins
                        
                        // Direction latérale pour contourner
                        Vector2 lateralDir = new Vector2(-toNeighbor.y, toNeighbor.x).normalized;
                        float sideSign = Mathf.Sign(Vector2.Dot(toNeighbor, lateralDir));
                        
                        avoidanceForce = sideSign * avoidanceStrength * lateralDir;
                    }
                }
            }
            
            return avoidanceForce;
        }
        
        private Vector2 CalculateRobotForce(Vector2 position, Vector2 velocity)
        {
            // Cette force sera calculée par HumanMovement qui a accès au robot
            // Retourne Vector2.zero ici, la force sera ajoutée dans HumanMovement
            return Vector2.zero;
        }
        
        private Vector2 ApplyDirectionalDampening(Vector2 force, Vector2 velocity)
        {
            if (velocity.sqrMagnitude < 0.01f) 
                return force;
            
            Vector2 forward = velocity.normalized;
            Vector2 right = new Vector2(-forward.y, forward.x);
            
            float forwardDot = Vector2.Dot(forward, force);
            float rightDot = Vector2.Dot(right, force);
            
            Vector2 forwardProj = forward * forwardDot;
            Vector2 rightProj = right * rightDot / _config.lateralDampening;
            
            // Dampening arrière (plus difficile de reculer)
            if (forwardDot < 0)
            {
                forwardProj /= _config.backwardDampening;
            }
            
            return forwardProj + rightProj;
        }
        
        private float GetSpeedMultiplierFromBehavior()
        {
            // La vitesse est gérée par HumanMovement, retourne 1 par défaut
            return 1f;
        }
        
        #endregion
        
        #region ISocialForceAgent Implementation
        
        public Vector3 ComputeGoalForce(Vector3 currentPos, Vector3 goalPos, float desiredSpeed)
        {
            Vector3 direction = (goalPos - currentPos).normalized;
            Vector3 desiredVelocity = direction * desiredSpeed;
            Vector3 currentVel = Velocity;
            return _mass * (desiredVelocity - currentVel) / _config.relaxationTime;
        }
        
        public Vector3 ComputeSocialForce()
        {
            Vector3 totalForce = Vector3.zero;
            
            foreach (var neighborGO in Neighbors)
            {
                if (neighborGO == null) continue;
                Debug.Log($"[SFMController] Computing social force from neighbor {neighborGO.name}");
                
                Vector3 toNeighbor = Transform.position - neighborGO.transform.position;
                float distance = toNeighbor.magnitude;
                
                if (distance >= _interactionRadius) continue;
                
                Vector3 direction = toNeighbor.normalized;
                float overlap = _radius + DEFAULT_HUMAN_RADIUS - distance;
                
                float forceMagnitude = _config.socialForceA * Mathf.Exp(overlap / _config.socialForceB);
                totalForce += direction * forceMagnitude;
            }
            
            return totalForce;
        }
        
        public Vector3 ComputeObstacleForce()
        {
            Vector3 totalForce = Vector3.zero;
            
            foreach (var obstacle in Obstacles)
            {
                if (obstacle == null) continue;
                
                Vector3 toObstacle = Transform.position - obstacle.transform.position;
                float distance = toObstacle.magnitude;
                
                if (distance >= _config.obstaclePerceptionRadius) continue;
                
                Vector3 direction = toObstacle.normalized;
                float forceMagnitude = _config.obstacleForceStrength * Mathf.Exp(-distance / _config.obstacleForceDistance);
                totalForce += direction * forceMagnitude;
            }
            
            return totalForce;
        }
        
        public Vector3 ComputeRobotForce()
        {
            // Force spécifique pour éviter le robot
            Vector3 totalForce = Vector3.zero;
            
            // Chercher le robot dans la scène
            var robot = GameObject.FindObjectOfType<Robot>();
            if (robot != null)
            {
                Vector3 toRobot = Transform.position - robot.transform.position;
                float distance = toRobot.magnitude;
                
                if (distance < _config.robotPerceptionRadius)
                {
                    Vector3 direction = toRobot.normalized;
                    float forceMagnitude = _config.robotRepulsionStrength * Mathf.Exp(-distance / _config.robotForceDistance);
                    totalForce += direction * forceMagnitude;
                }
            }
            
            return totalForce;
        }
        
        public void SetAssertiveness(float value)
        {
            _assertiveness = Mathf.Clamp(value, 0f, 1f);
        }
        
        public void SetPersonalSpace(float value)
        {
            _personalSpace = Mathf.Clamp(value, 0.3f, 2f);
        }
        
        public void SetInteractionRadius(float value)
        {
            _interactionRadius = Mathf.Clamp(value, 0.5f, 3f);
        }
        
        public void SetReactionTime(float value)
        {
            _reactionTime = Mathf.Clamp(value, 0.1f, 1f);
        }
        
        #endregion
    }
}