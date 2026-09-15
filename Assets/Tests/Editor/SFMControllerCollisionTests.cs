using System.Collections.Generic;
using NUnit.Framework;
using RobotSNAP.Agents;
using RobotSNAP.Agents.Movement.Controllers;
using RobotSNAP.Agents.Movement.Interfaces;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// The crowd contract: agents walked by <see cref="SFMController"/> on their own must not share the same
    /// space. Nothing here needs a scene — the controller is stepped like <c>HumanMovement</c> steps it, and
    /// the agents are moved by the velocity it returns, so a regression in the avoidance shows up as two
    /// bodies whose centres come closer than their two radii.
    /// </summary>
    public sealed class SFMControllerCollisionTests
    {
        /// <summary>Physics step of the project.</summary>
        private const float DeltaTime = 1f / 50f;

        /// <summary>Body radius of the shipped HumanConfig asset.</summary>
        private const float Radius = 0.3f;

        /// <summary>Two bodies are in contact under this centre distance.</summary>
        private const float ContactDistance = Radius * 2f;

        [Test]
        public void HeadOnAgentsNeverShareTheSameSpace()
        {
            float closest = ClosestApproach(
                new List<Vector2> { new(-3f, 0f), new(3f, 0f) },
                new List<Vector2> { new(3f, 0f), new(-3f, 0f) },
                20f);

            Assert.That(closest, Is.GreaterThan(ContactDistance),
                "Two agents walking at each other must pass, not overlap.");
        }

        [Test]
        public void HeadOnAgentsSteerAsideWellBeforeTouching()
        {
            // The point of the anticipation: the encounter is resolved from a few metres away, not in the last
            // half metre. A gap barely above the body width means the avoidance only reacted at the last moment.
            float closest = ClosestApproach(
                new List<Vector2> { new(-3f, 0f), new(3f, 0f) },
                new List<Vector2> { new(3f, 0f), new(-3f, 0f) },
                20f);

            Assert.That(closest, Is.GreaterThan(ContactDistance + 0.3f),
                "The steering has to start early, not when the bodies are already touching.");
        }

        [Test]
        public void PerpendicularFlowsKeepTheirDistance()
        {
            float closest = ClosestApproach(
                new List<Vector2> { new(-3f, 0f), new(0f, -3f) },
                new List<Vector2> { new(3f, 0f), new(0f, 3f) },
                20f);

            Assert.That(closest, Is.GreaterThan(ContactDistance),
                "A perpendicular crossing is the shape of the traffic scenarios.");
        }

        [Test]
        public void DenseCrowdCrossingItselfKeepsTheAgentsApart()
        {
            List<Vector2> starts = Spread(12, 4242, 14f, 4f, -7f, -7f);
            List<Vector2> goals = Spread(12, 4243, 14f, 4f, -7f, 3f);

            float closest = ClosestApproach(starts, goals, 30f);

            Assert.That(closest, Is.GreaterThan(ContactDistance),
                "The agents of a crowd draw their own start and their own arrival, so their paths cross.");
        }

        [Test]
        public void ACrowdReachesItsGoalsInsteadOfFreezing()
        {
            List<Vector2> starts = Spread(12, 4242, 14f, 4f, -7f, -7f);
            List<Vector2> goals = Spread(12, 4243, 14f, 4f, -7f, 3f);

            int arrived = Simulate(starts, goals, 30f, out _);

            Assert.That(arrived, Is.EqualTo(starts.Count),
                "Avoiding each other must not cost the crowd its destinations.");
        }

        // ==================== Simulation ====================

        private static float ClosestApproach(
            List<Vector2> starts,
            List<Vector2> goals,
            float seconds)
        {
            Simulate(starts, goals, seconds, out float closest);
            return closest;
        }

        /// <summary>
        /// Walks the agents the way <c>HumanMovement</c> does: everybody decides from the positions of the
        /// previous step, then everybody moves. Returns how many reached their goal.
        /// </summary>
        private static int Simulate(List<Vector2> starts, List<Vector2> goals, float seconds, out float closest)
        {
            var positions = new List<Vector2>(starts);
            var velocities = new List<Vector2>();
            var controllers = new List<SFMController>();
            for (int index = 0; index < starts.Count; index++)
            {
                velocities.Add(Vector2.zero);
                controllers.Add(new SFMController(NewConfig(), null));
            }

            var neighbourPositions = new List<Vector2>();
            var neighbourVelocities = new List<Vector2>();
            int steps = Mathf.RoundToInt(seconds / DeltaTime);
            closest = float.MaxValue;

            for (int step = 0; step < steps; step++)
            {
                for (int index = 0; index < positions.Count; index++)
                {
                    neighbourPositions.Clear();
                    neighbourVelocities.Clear();
                    for (int other = 0; other < positions.Count; other++)
                    {
                        if (other == index)
                            continue;
                        neighbourPositions.Add(positions[other]);
                        neighbourVelocities.Add(velocities[other]);
                    }

                    velocities[index] = controllers[index].ComputeVelocity(
                        positions[index],
                        velocities[index],
                        goals[index],
                        neighbourPositions,
                        neighbourVelocities,
                        null,
                        RobotObservation.None,
                        DeltaTime);
                }

                for (int index = 0; index < positions.Count; index++)
                    positions[index] += velocities[index] * DeltaTime;

                for (int first = 0; first < positions.Count; first++)
                {
                    for (int second = first + 1; second < positions.Count; second++)
                    {
                        float distance = Vector2.Distance(positions[first], positions[second]);
                        if (distance < closest)
                            closest = distance;
                    }
                }
            }

            int arrived = 0;
            for (int index = 0; index < positions.Count; index++)
            {
                if (Vector2.Distance(positions[index], goals[index]) <= 0.35f)
                    arrived++;
            }

            return arrived;
        }

        /// <summary>
        /// The parameters the project ships in <c>Assets/Configs/HumanConfig.asset</c>. The defaults of the
        /// ScriptableObject differ from the asset, so they are written out rather than inherited.
        /// </summary>
        private static HumanConfig NewConfig()
        {
            HumanConfig config = ScriptableObject.CreateInstance<HumanConfig>();
            config.desiredSpeed = 0.8f;
            config.maxSpeed = 1.2f;
            config.slowDownDistance = 1.5f;
            config.agentMass = 80f;
            config.agentRadius = Radius;
            config.relaxationTime = 0.5f;
            config.perceptionRadiusAgent = 10f;
            config.socialForceA = 5000f;
            config.socialForceB = 0.08f;
            config.alignmentStrength = 0.3f;
            config.contactStiffnessK = 120000f;
            config.contactFrictionKappa = 240000f;
            config.obstaclePerceptionRadius = 3f;
            config.obstacleForceStrength = 100f;
            config.obstacleForceDistance = 0.3f;
            config.robotPerceptionRadius = 3f;
            config.robotRepulsionStrength = 10f;
            config.robotForceDistance = 0.8f;
            config.backwardDampening = 20f;
            config.lateralDampening = 0.001f;
            config.robotRepulsionDampeningMin = 0.5f;
            config.robotRepulsionDampeningMax = 1f;
            config.goalReachedDistance = 0.2f;
            return config;
        }

        /// <summary>
        /// Points drawn in a band, kept at least a body apart, the way the scenario applier draws the start and
        /// the arrival of every agent of a crowd.
        /// </summary>
        private static List<Vector2> Spread(int count, int seed, float width, float depth, float originX, float originY)
        {
            var random = new System.Random(seed);
            var points = new List<Vector2>(count);
            int guard = 0;
            while (points.Count < count && guard++ < 20000)
            {
                var candidate = new Vector2(
                    originX + (float)random.NextDouble() * width,
                    originY + (float)random.NextDouble() * depth);

                bool clear = true;
                foreach (Vector2 other in points)
                {
                    if (Vector2.Distance(candidate, other) < Radius * 4f)
                    {
                        clear = false;
                        break;
                    }
                }

                if (clear)
                    points.Add(candidate);
            }

            return points;
        }
    }
}
