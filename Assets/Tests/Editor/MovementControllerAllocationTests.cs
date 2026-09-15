using System.Collections.Generic;
using NUnit.Framework;
using RobotSNAP.Agents;
using RobotSNAP.Agents.Movement.Controllers;
using RobotSNAP.Agents.Movement.Interfaces;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// The movement path must stay allocation-free per physics step: neighbours and obstacles reach the
    /// controller as lists and are read in place, without being copied into arrays first. At 100 agents and
    /// 50 Hz, a copy per list and per step is about 15 000 temporary arrays per second.
    /// </summary>
    public sealed class MovementControllerAllocationTests
    {
        private const int NeighbourCount = 150;
        private const int WarmupCalls = 20;
        private const int MeasuredCalls = 200;
        private const long AllocationBudgetBytes = 20000;

        private static readonly Vector2 StartPosition = new Vector2(1f, 1f);
        private static readonly Vector2 StartVelocity = new Vector2(0.5f, 0.1f);
        private static readonly Vector2 GoalPosition = new Vector2(14f, 1f);
        private const float Step = 0.02f;

        private HumanConfig _config;
        private SFMController _controller;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<HumanConfig>();
            _controller = new SFMController(_config, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_config != null)
                Object.DestroyImmediate(_config);

            _config = null;
            _controller = null;
        }

        /// <summary>Radii from 0.4 m to 3.5 m, so both the kept and the filtered neighbours are exercised.</summary>
        private static List<Vector2> CreateNeighbourPositions()
        {
            List<Vector2> neighbours = new List<Vector2>(NeighbourCount);
            for (int i = 0; i < NeighbourCount; i++)
            {
                float angle = i * 0.7f;
                float radius = 0.4f + (i % 40) * 0.08f;
                neighbours.Add(StartPosition + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }

            return neighbours;
        }

        private static List<Vector2> CreateNeighbourVelocities()
        {
            List<Vector2> velocities = new List<Vector2>(NeighbourCount);
            for (int i = 0; i < NeighbourCount; i++)
                velocities.Add(new Vector2(-0.6f - 0.004f * i, 0.15f));

            return velocities;
        }

        private static List<Vector2> CreateObstacles()
        {
            return new List<Vector2>
            {
                StartPosition + new Vector2(1.4f, 0.6f),
                StartPosition + new Vector2(0.8f, -1.1f)
            };
        }

        [Test]
        public void SfmController_DoesNotAllocatePerCall()
        {
            List<Vector2> neighbours = CreateNeighbourPositions();
            List<Vector2> neighbourVelocities = CreateNeighbourVelocities();
            List<Vector2> obstacles = CreateObstacles();

            // Warm-up: JIT the call path before measuring, so its own allocations stay out of the count.
            for (int i = 0; i < WarmupCalls; i++)
                RunController(neighbours, neighbourVelocities, obstacles);

            Vector2 last = Vector2.zero;
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < MeasuredCalls; i++)
                last = RunController(neighbours, neighbourVelocities, obstacles);
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;

            AssertFinite(last);
            Assert.That(
                allocated,
                Is.LessThan(AllocationBudgetBytes),
                $"{MeasuredCalls} calls allocated {allocated} bytes; the budget is {AllocationBudgetBytes}.");
        }

        [Test]
        public void SfmController_ReadsTheNeighbours()
        {
            Vector2 alone = RunController(null, null, null);

            List<Vector2> neighbours = new List<Vector2> { StartPosition + new Vector2(0.4f, 0f) };
            List<Vector2> neighbourVelocities = new List<Vector2> { new Vector2(-0.6f, 0f) };
            Vector2 crowded = RunController(neighbours, neighbourVelocities, null);

            AssertFinite(alone);
            AssertFinite(crowded);
            Assert.That(
                Vector2.Distance(crowded, alone),
                Is.GreaterThan(0.05f),
                "A neighbour 0.4 m away must change the velocity: the list has to be read.");
        }

        [Test]
        public void ComputeVelocity_AcceptsNullLists()
        {
            Vector2 velocity = Vector2.one;

            Assert.DoesNotThrow(() => velocity = RunController(null, null, null));
            AssertFinite(velocity);

            // The real caller passes empty lists, not null, when the agent sees nothing.
            Assert.DoesNotThrow(() =>
                velocity = RunController(new List<Vector2>(), new List<Vector2>(), new List<Vector2>()));
            AssertFinite(velocity);
        }

        private Vector2 RunController(
            IReadOnlyList<Vector2> neighbours,
            IReadOnlyList<Vector2> neighbourVelocities,
            IReadOnlyList<Vector2> obstacles)
        {
            return _controller.ComputeVelocity(
                StartPosition,
                StartVelocity,
                GoalPosition,
                neighbours,
                neighbourVelocities,
                obstacles,
                RobotObservation.None,
                Step);
        }

        private static void AssertFinite(Vector2 value)
        {
            Assert.That(float.IsNaN(value.x) || float.IsInfinity(value.x), Is.False, "velocity.x is not finite");
            Assert.That(float.IsNaN(value.y) || float.IsInfinity(value.y), Is.False, "velocity.y is not finite");
        }
    }
}
