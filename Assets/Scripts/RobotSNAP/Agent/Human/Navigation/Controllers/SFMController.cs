using UnityEngine;
using RobotSNAP.Agents.Movement.Interfaces;

namespace RobotSNAP.Agents.Movement.Controllers
{
    public class SFMController : IMovementController
    {
        private HumanConfig _config;
        private float _mass;
        private float _relaxationTime;
        private float _desiredSpeed;
        private float _maxSpeed;
        private float _agentRadius;

        // Paramètres sociaux
        private float _socialForceA;
        private float _socialForceB;
        private float _perceptionRadius;
        private float _alignmentStrength;
        private float _contactStiffnessK;
        private float _contactFrictionKappa;

        // Amortissement
        private float _backwardDampening;
        private float _lateralDampening;

        // Paramètres d'approche
        private float _slowDownDistance;
        private float _goalReachedDistance;

        // Stabilité numérique
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
            float deltaTime)
        {
            // ---- 1. Force d'attraction vers le but ----
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

            // ---- 2. Forces sociales et de contact ----
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

                // ---- Répulsion sociale (ANISOTROPE) ----
                float angle = Vector2.Angle(desiredDir, dirToNeighbor) * Mathf.Deg2Rad;
                float cosPhi = Mathf.Cos(angle);
                float angularFactor = ANISOTROPIC_FACTOR + (1f - ANISOTROPIC_FACTOR) * (1f + cosPhi) / 2f;

                float socialForceMag = _socialForceA * Mathf.Exp((_agentRadius * 2f - distance) / _socialForceB);
                // ICI LE SIGNE CORRIGÉ : on repousse dans la direction OPPOSÉE
                Vector2 socialForce = -socialForceMag * angularFactor * dirToNeighbor;
                socialAccel += socialForce / _mass;

                // ---- Alignement (cohésion) ----
                float alignmentWeight = Mathf.Exp(-distance / _perceptionRadius);
                Vector2 velDiff = neighborVel - currentVelocity;
                alignmentAccel += alignmentWeight * velDiff * _alignmentStrength / _mass;

                // ---- Contact (si les rayons se touchent) ----
                float overlap = _agentRadius * 2f - distance;
                if (overlap > 0)
                {
                    // Force normale (répulsive)
                    Vector2 normalForce = _contactStiffnessK * overlap * -dirToNeighbor;

                    // Frottement tangentiel
                    Vector2 relativeVel = neighborVel - currentVelocity;
                    Vector2 tangentDir = new Vector2(-dirToNeighbor.y, dirToNeighbor.x);
                    float tangentSpeed = Vector2.Dot(relativeVel, tangentDir);
                    Vector2 frictionForce = _contactFrictionKappa * overlap * tangentSpeed * tangentDir;

                    Vector2 contactForce = normalForce + frictionForce;
                    contactAccel += contactForce / _mass;
                }
            }

            // ---- 3. Amortissement directionnel ----
            Vector2 forwardDir = desiredDir;
            Vector2 lateralDir = new Vector2(-forwardDir.y, forwardDir.x);

            float forwardSpeed = Vector2.Dot(currentVelocity, forwardDir);
            float lateralSpeed = Vector2.Dot(currentVelocity, lateralDir);

            float dampForward = forwardSpeed < 0 ? _backwardDampening : 0f;
            float dampLateral = _lateralDampening;

            Vector2 dampAccel = -dampForward * forwardSpeed * forwardDir
                                - dampLateral * lateralSpeed * lateralDir;

            // ---- 4. Accélération totale ----
            Vector2 totalAccel = attractionAccel + socialAccel + contactAccel + alignmentAccel + dampAccel;

            // Limiter l'accélération
            float accelMag = totalAccel.magnitude;
            if (accelMag > MAX_ACCELERATION)
                totalAccel = (totalAccel / accelMag) * MAX_ACCELERATION;

            // ---- 5. Intégration ----
            Vector2 newVelocity = currentVelocity + totalAccel * deltaTime;

            // Limiter la vitesse
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
            _backwardDampening = config.backwardDampening;
            _lateralDampening = config.lateralDampening;
            _slowDownDistance = config.slowDownDistance;
            _goalReachedDistance = config.goalReachedDistance;
        }
    }
}