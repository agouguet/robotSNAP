using NUnit.Framework;
using RobotSNAP.Agents;
using UnityEngine;
using UnityEngine.AI;

namespace RobotSNAP.Tests.Editor
{
    public sealed class SpawnPlacementTests
    {
        [Test]
        public void ProjectWithin_KeepsTheAuthoredSlotWhenTheSceneHasNoNavMesh()
        {
            AssumeNoNavMeshInTestScene();

            var anchor = new Vector2(1f, -2f);
            var slot = new Vector2(0.25f, -3.05f);

            Assert.That(SpawnPlacement.ProjectWithin(slot, anchor, 2f), Is.EqualTo(slot));
        }

        [Test]
        public void ProjectWithin_DoesNotPullAnOutOfRangeSlotOntoTheAnchor()
        {
            AssumeNoNavMeshInTestScene();

            var slot = new Vector2(9f, 0f);

            Assert.That(SpawnPlacement.ProjectWithin(slot, Vector2.zero, 2f), Is.EqualTo(slot));
        }

        [Test]
        public void SnapToNavMesh_ReturnsTheAuthoredAnchorWhenTheSceneHasNoNavMesh()
        {
            AssumeNoNavMeshInTestScene();

            var anchor = new Vector3(3f, 0f, -4f);

            Assert.That(SpawnPlacement.SnapToNavMesh(anchor), Is.EqualTo(anchor));
        }

        /// <summary>
        /// These behaviours are only defined without a NavMesh: with a baked surface the projection
        /// legitimately moves the point. The Editor test scene has no baked NavMesh, so guard anyway.
        /// </summary>
        private static void AssumeNoNavMeshInTestScene()
        {
            if (NavMesh.SamplePosition(Vector3.zero, out _, 100f, NavMesh.AllAreas))
                Assert.Ignore("The Editor test scene exposes a NavMesh; projection behaviour applies instead.");
        }
    }
}
