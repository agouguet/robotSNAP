using System.Collections.Generic;
using UnityEngine;
using RobotSNAP.Agents;
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

        // Overtaking: anticipatory, reciprocal avoidance of the agents in front
        private float _overtakingStrength;
        private float _anticipationHorizon;
        private float _anticipationMargin;

        // Damping
        private float _backwardDampening;
        private float _lateralDampening;

        // Goal approach
        private float _slowDownDistance;
        private float _goalReachedDistance;

        // Robot (the human yields to it instead of walking through it)
        private float _robotPerceptionRadius;
        private float _robotRepulsionStrength;
        private float _robotForceDistance;
        private float _robotDampeningMin;
        private float _robotDampeningMax;
        private float _robotAnticipationStrength;

        /// <summary>
        /// Range in which the robot is anticipated, well outside <c>robotPerceptionRadius</c>: the robot covers
        /// several metres during the anticipation horizon, and the repulsion alone only reacts under three.
        /// </summary>
        private const float RobotAnticipationRadius = 6f;

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
            _overtakingStrength = 2.4f;                                        // can be made configurable
            _anticipationHorizon = HumanAvoidance.DefaultHorizon;              // seconds
            _anticipationMargin = HumanAvoidance.DefaultMargin;                // metres

            _backwardDampening = config.backwardDampening;
            _lateralDampening = config.lateralDampening;

            _slowDownDistance = config.slowDownDistance;
            _goalReachedDistance = config.goalReachedDistance;

            _robotPerceptionRadius = config.robotPerceptionRadius;
            _robotRepulsionStrength = config.robotRepulsionStrength;
            _robotForceDistance = Mathf.Max(0.05f, config.robotForceDistance);
            _robotDampeningMin = config.robotRepulsionDampeningMin;
            _robotDampeningMax = config.robotRepulsionDampeningMax;
            // A robot is heavier and closes faster than a pedestrian: humans give way to it earlier and harder.
            _robotAnticipationStrength = 3.6f;
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
            // ---- 1. Goal attraction ----
            Vector2 toGoal = goalPosition - currentPosition;
            float distToGoal = toGoal.magnitude;

            if (distToGoal < _goalReachedDistance)
                return Vector2.zero;

            Vector2 desiredDir = toGoal / distToGoal;

            // A group follower may be asked for a higher cruise speed so it can close the gap;
            // the physical ceiling of the agent still applies.
            float cruiseSpeed = cruiseSpeedOverride > 0.01f
                ? Mathf.Min(cruiseSpeedOverride, _maxSpeed)
                : _desiredSpeed;

            float desiredSpeed = cruiseSpeed;
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

            int count = neighbors?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                Vector2 neighborPos = neighbors[i];
                Vector2 neighborVel = neighborVelocities[i];

                Vector2 toNeighbor = neighborPos - currentPosition;
                float distance = toNeighbor.magnitude;

                if (distance <= 0.001f || distance > _perceptionRadius)
                    continue;

                Vector2 dirToNeighbor = toNeighbor / distance;

                // ---- Anticipatory reciprocal avoidance ----
                // Looking a few seconds ahead is what the old rule missed: it only reacted under 1.5 m, with a
                // random side, so two agents met and stalled instead of choosing who goes where.
                HumanAvoidance.Prediction prediction = HumanAvoidance.Predict(
                    currentPosition,
                    currentVelocity,
                    _agentRadius,
                    neighborPos,
                    neighborVel,
                    _agentRadius,
                    desiredDir,
                    _anticipationHorizon,
                    _anticipationMargin);

                // ---- Social repulsion (anisotropic) ----
                float angle = Vector2.Angle(desiredDir, dirToNeighbor) * Mathf.Deg2Rad;
                float cosPhi = Mathf.Cos(angle);
                float angularFactor = ANISOTROPIC_FACTOR + (1f - ANISOTROPIC_FACTOR) * (1f + cosPhi) / 2f;

                // The repulsion is what stops a pedestrian dead when somebody walks at them, so it is relaxed
                // while the agent is already steering aside: the anticipation replaces part of the reaction.
                float repulsionScale = prediction.IsConflict
                    ? Mathf.Lerp(1f, 0.45f, prediction.Urgency)
                    : 1f;

                float socialForceMag = _socialForceA * Mathf.Exp((_agentRadius * 2f - distance) / _socialForceB);
                Vector2 socialForce = -socialForceMag * angularFactor * dirToNeighbor;
                socialAccel += socialForce * repulsionScale / _mass;

                if (prediction.IsConflict)
                {
                    Vector2 rightDir = HumanAvoidance.RightOf(desiredDir);
                    socialAccel += rightDir * (prediction.Side * _overtakingStrength * prediction.Urgency) / _mass;
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
            // Indexed iteration, same order as the previous foreach over an array: enumerating a list through
            // the interface would box its enumerator, i.e. allocate once per call.
            int obstacleCount = staticObstacles?.Count ?? 0;
            for (int i = 0; i < obstacleCount; i++)
            {
                Vector2 obstaclePos = staticObstacles[i];
                Vector2 toObstacle = obstaclePos - currentPosition;
                float distance = toObstacle.magnitude;

                if (distance <= 0.001f || distance > _obstaclePerceptionRadius)
                    continue;

                Vector2 dirToObstacle = toObstacle / distance;
                float forceMag = _obstacleForceStrength * Mathf.Exp((_agentRadius - distance) / _obstacleForceDistance);
                Vector2 obstacleForce = -forceMag * dirToObstacle;
                obstacleAccel += obstacleForce / _mass;
            }

            // ---- 3b. Robot: repulsion close up, anticipation from further out ----
            // The robot is not a wall nor a regular pedestrian: it gets its own force so humans step aside
            // instead of walking through it. It is also heavier and closes faster than a pedestrian, so the
            // avoidance starts outside the repulsion radius.
            Vector2 robotAccel = Vector2.zero;
            if (robot.IsVisible)
            {
                Vector2 toRobot = robot.Position - currentPosition;
                float distance = toRobot.magnitude;
                if (distance > 0.001f && distance < RobotAnticipationRadius)
                {
                    Vector2 dirToRobot = toRobot / distance;

                    // Same anticipation as between pedestrians: the robot is heavy, cannot dodge, and closing
                    // speeds are high, so waiting for the repulsion to bite means being walked into.
                    HumanAvoidance.Prediction prediction = HumanAvoidance.Predict(
                        currentPosition,
                        currentVelocity,
                        _agentRadius,
                        robot.Position,
                        robot.Velocity,
                        robot.Radius,
                        desiredDir,
                        _anticipationHorizon,
                        _anticipationMargin);

                    if (prediction.IsConflict)
                    {
                        // Step out of the path the robot is about to drive through, and do it early.
                        robotAccel += HumanAvoidance.RightOf(desiredDir) *
                                      (prediction.Side * _robotAnticipationStrength * prediction.Urgency) /
                                      _mass;
                    }

                    if (distance < _robotPerceptionRadius)
                    {
                        float combinedRadius = _agentRadius + robot.Radius;
                        float forceMagnitude = _robotRepulsionStrength *
                                               Mathf.Exp((combinedRadius - distance) / _robotForceDistance);
                        float proximity = Mathf.Clamp01(1f - distance / _robotPerceptionRadius);
                        float dampening = Mathf.Lerp(_robotDampeningMin, _robotDampeningMax, proximity);
                        float effectiveForce = forceMagnitude * dampening;

                        // The repulsion is what stops a pedestrian dead in front of the robot, so it is relaxed
                        // while the agent is already stepping aside for it.
                        float repulsionScale = prediction.IsConflict
                            ? Mathf.Lerp(1f, 0.5f, prediction.Urgency)
                            : 1f;
                        robotAccel += (-dirToRobot * effectiveForce * repulsionScale) / _mass;

                        if (!prediction.IsConflict)
                        {
                            // No conflict predicted: keep the reactive slide for a robot alongside us.
                            Vector2 robotHeading = robot.Velocity.sqrMagnitude > 0.01f
                                ? robot.Velocity.normalized
                                : dirToRobot;
                            Vector2 lateral = new Vector2(-robotHeading.y, robotHeading.x);
                            if (Vector2.Dot(currentPosition - robot.Position, lateral) < 0f)
                                lateral = -lateral;
                            float closingSpeed = Vector2.Dot(robot.Velocity - currentVelocity, dirToRobot);
                            if (closingSpeed > 0.05f)
                                robotAccel += lateral * effectiveForce * 0.4f / _mass;
                        }
                    }
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
            Vector2 totalAccel = attractionAccel + socialAccel + contactAccel + alignmentAccel + obstacleAccel + robotAccel + dampAccel;

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
            _robotPerceptionRadius = config.robotPerceptionRadius;
            _robotRepulsionStrength = config.robotRepulsionStrength;
            _robotForceDistance = Mathf.Max(0.05f, config.robotForceDistance);
            _robotDampeningMin = config.robotRepulsionDampeningMin;
            _robotDampeningMax = config.robotRepulsionDampeningMax;
            // Keep overtaking and noise as defined (could be extended from config)
        }
    }
}
