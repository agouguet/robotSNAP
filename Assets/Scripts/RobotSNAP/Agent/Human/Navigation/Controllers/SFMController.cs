using UnityEngine;
using RobotSNAP.Agents.Movement.Interfaces;

namespace RobotSNAP.Agents.Movement.Controllers
{
    /// <summary>
    /// Social Force Model (SFM) controller with social repulsion, contact forces,
    /// static obstacle avoidance, and overtaking behavior to prevent deadlocks.
    /// </summary>
    public class SFMController : IMovementController
    {
        private HumanConfig _config;

        // Physical
        private float _mass;
        private float _relaxationTime;
        private float _desiredSpeed;
        private float _maxSpeed;
        private float _agentRadius;

        // Social
        private float _socialForceA;
        private float _socialForceB;
        private float _perceptionRadius;
        private float _alignmentStrength;
        private float _contactStiffnessK;
        private float _contactFrictionKappa;

        // Obstacles
        private float _obstaclePerceptionRadius;
        private float _obstacleForceStrength;
        private float _obstacleForceDistance;

        // Overtaking (face-to-face avoidance)
        private float _overtakingStrength;
        private float _noiseStrength;

        // Damping
        private float _backwardDampening;
        private float _lateralDampening;

        // Goal approach
        private float _slowDownDistance;
        private float _goalReachedDistance;

        // Numerical stability
        private const float MAX_ACCELERATION = 20f;
        private const float ANISOTROPIC_FACTOR = 0.5f;

        public SFMController(HumanConfig config, HumanAgent avatar)
        {
            _config = config;
            _mass = config.agentMass;
            _relaxationTime = config.relaxationTime;
            _desiredSpeed = config.desiredSpeed;
            _maxSpeed = config.maxSpeed;
            _agentRadius = config.agentRadius;

            _socialForceA = config.socialForceA;
            _socialForceB = config.socialForceB;
            _perceptionRadius = config.perceptionRadiusAgent;
            _alignmentStrength = config.alignmentStrength;
            _contactStiffnessK = config.contactStiffnessK;
            _contactFrictionKappa = config.contactFrictionKappa;

            _obstaclePerceptionRadius = config.obstaclePerceptionRadius;
            _obstacleForceStrength = config.obstacleForceStrength;
            _obstacleForceDistance = config.obstacleForceDistance;

            // Overtaking parameters (use defaults if not present in config)
            _overtakingStrength = 2.0f; // can be made configurable
            _noiseStrength = 0.05f;     // can be made configurable

            _backwardDampening = config.backwardDampening;
            _lateralDampening = config.lateralDampening;

            _slowDownDistance = config.slowDownDistance;
            _goalReachedDistance = config.goalReachedDistance;
        }

        public Vector2 ComputeVelocity(
            Vector2 currentPosition,
            Vector2 currentVelocity,
            Vector2 goalPosition,
            Vector2[] neighbors,
            Vector2[] neighborVelocities,
            Vector2[] staticObstacles,
            float deltaTime)
        {
            // ---- 1. Goal attraction ----
            Vector2 toGoal = goalPosition - currentPosition;
            float distToGoal = toGoal.magnitude;

            if (distToGoal < _goalReachedDistance)
                return Vector2.zero;

            Vector2 desiredDir = toGoal / distToGoal;

            float desiredSpeed = _desiredSpeed;
            if (distToGoal < _slowDownDistance)
            {
                float t = distToGoal / _slowDownDistance;
                desiredSpeed *= Mathf.Lerp(0.2f, 1f, t);
            }
            desiredSpeed = Mathf.Max(desiredSpeed, 0.05f);

            Vector2 desiredVelocity = desiredDir * desiredSpeed;
            Vector2 attractionAccel = (desiredVelocity - currentVelocity) / _relaxationTime;

            // ---- 2. Social and contact forces ----
            Vector2 socialAccel = Vector2.zero;
            Vector2 contactAccel = Vector2.zero;
            Vector2 alignmentAccel = Vector2.zero;

            int count = neighbors?.Length ?? 0;
            for (int i = 0; i < count; i++)
            {
                Vector2 neighborPos = neighbors[i];
                Vector2 neighborVel = neighborVelocities[i];

                Vector2 toNeighbor = neighborPos - currentPosition;
                float distance = toNeighbor.magnitude;

                if (distance <= 0.001f || distance > _perceptionRadius)
                    continue;

                Vector2 dirToNeighbor = toNeighbor / distance;

                // ---- Social repulsion (anisotropic) ----
                float angle = Vector2.Angle(desiredDir, dirToNeighbor) * Mathf.Deg2Rad;
                float cosPhi = Mathf.Cos(angle);
                float angularFactor = ANISOTROPIC_FACTOR + (1f - ANISOTROPIC_FACTOR) * (1f + cosPhi) / 2f;

                float socialForceMag = _socialForceA * Mathf.Exp((_agentRadius * 2f - distance) / _socialForceB);
                Vector2 socialForce = -socialForceMag * angularFactor * dirToNeighbor;
                socialAccel += socialForce / _mass;

                // ---- Overtaking force (face-to-face avoidance) ----
                // Detect if both agents are moving towards each other
                Vector2 otherVelocity = neighborVel;
                float dotForward = Vector2.Dot(desiredDir, dirToNeighbor);
                float dotOtherForward = Vector2.Dot(otherVelocity.normalized, -desiredDir);

                if (dotForward < -0.2f && dotOtherForward < -0.2f && distance < _perceptionRadius * 0.6f)
                {
                    // Right direction in agent's local frame
                    Vector2 rightDir = new Vector2(-desiredDir.y, desiredDir.x);
                    float overtakeWeight = Mathf.Exp(-distance / 1.2f);
                    float overtakeForce = _overtakingStrength * overtakeWeight;
                    socialAccel += rightDir * overtakeForce / _mass;

                    // Add slight random noise to break symmetry
                    float noise = (Random.Range(-1f, 1f) * _noiseStrength);
                    socialAccel += rightDir * noise / _mass;
                }

                // ---- Alignment ----
                float alignmentWeight = Mathf.Exp(-distance / _perceptionRadius);
                Vector2 velDiff = neighborVel - currentVelocity;
                alignmentAccel += alignmentWeight * velDiff * _alignmentStrength / _mass;

                // ---- Contact forces (when overlapping) ----
                float overlap = _agentRadius * 2f - distance;
                if (overlap > 0)
                {
                    Vector2 normalForce = _contactStiffnessK * overlap * -dirToNeighbor;
                    Vector2 relativeVel = neighborVel - currentVelocity;
                    Vector2 tangentDir = new Vector2(-dirToNeighbor.y, dirToNeighbor.x);
                    float tangentSpeed = Vector2.Dot(relativeVel, tangentDir);
                    Vector2 frictionForce = _contactFrictionKappa * overlap * tangentSpeed * tangentDir;
                    Vector2 contactForce = normalForce + frictionForce;
                    contactAccel += contactForce / _mass;
                }
            }

            // ---- 3. Static obstacle repulsion ----
            Vector2 obstacleAccel = Vector2.zero;
            if (staticObstacles != null)
            {
                foreach (Vector2 obstaclePos in staticObstacles)
                {
                    Vector2 toObstacle = obstaclePos - currentPosition;
                    float distance = toObstacle.magnitude;

                    if (distance <= 0.001f || distance > _obstaclePerceptionRadius)
                        continue;

                    Vector2 dirToObstacle = toObstacle / distance;
                    float forceMag = _obstacleForceStrength * Mathf.Exp((_agentRadius - distance) / _obstacleForceDistance);
                    Vector2 obstacleForce = -forceMag * dirToObstacle;
                    obstacleAccel += obstacleForce / _mass;
                }
            }

            // ---- 4. Directional damping ----
            Vector2 forwardDir = desiredDir;
            Vector2 lateralDir = new Vector2(-forwardDir.y, forwardDir.x);

            float forwardSpeed = Vector2.Dot(currentVelocity, forwardDir);
            float lateralSpeed = Vector2.Dot(currentVelocity, lateralDir);

            float dampForward = forwardSpeed < 0 ? _backwardDampening : 0f;
            float dampLateral = _lateralDampening;

            Vector2 dampAccel = -dampForward * forwardSpeed * forwardDir
                                - dampLateral * lateralSpeed * lateralDir;

            // ---- 5. Total acceleration ----
            Vector2 totalAccel = attractionAccel + socialAccel + contactAccel + alignmentAccel + obstacleAccel + dampAccel;

            // Clamp acceleration
            float accelMag = totalAccel.magnitude;
            if (accelMag > MAX_ACCELERATION)
                totalAccel = (totalAccel / accelMag) * MAX_ACCELERATION;

            // ---- 6. Integration ----
            Vector2 newVelocity = currentVelocity + totalAccel * deltaTime;

            // Clamp speed
            float speed = newVelocity.magnitude;
            if (speed > _maxSpeed)
                newVelocity = (newVelocity / speed) * _maxSpeed;

            if (newVelocity.sqrMagnitude < 0.0001f)
                newVelocity = Vector2.zero;

            return newVelocity;
        }

        public void Reset() { }

        public float GetConfidence() => 1f;

        public void UpdateParameters(HumanConfig config)
        {
            _config = config;
            _mass = config.agentMass;
            _relaxationTime = config.relaxationTime;
            _desiredSpeed = config.desiredSpeed;
            _maxSpeed = config.maxSpeed;
            _agentRadius = config.agentRadius;
            _socialForceA = config.socialForceA;
            _socialForceB = config.socialForceB;
            _perceptionRadius = config.perceptionRadiusAgent;
            _alignmentStrength = config.alignmentStrength;
            _contactStiffnessK = config.contactStiffnessK;
            _contactFrictionKappa = config.contactFrictionKappa;
            _obstaclePerceptionRadius = config.obstaclePerceptionRadius;
            _obstacleForceStrength = config.obstacleForceStrength;
            _obstacleForceDistance = config.obstacleForceDistance;
            _backwardDampening = config.backwardDampening;
            _lateralDampening = config.lateralDampening;
            _slowDownDistance = config.slowDownDistance;
            _goalReachedDistance = config.goalReachedDistance;
            // Keep overtaking and noise as defined (could be extended from config)
        }
    }
}