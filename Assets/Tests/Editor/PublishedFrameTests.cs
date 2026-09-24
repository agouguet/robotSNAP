using NUnit.Framework;
using RobotSNAP.Core.Scenario;
using RobotSNAP.ROS;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// The two streams whose frame was wrong until this lot: the lidar scan, whose bearings follow the ROS
    /// frame rather than Unity's, and the occupancy grid of /map, which is counted the way ROS counts a
    /// grid. Both are read here the way a ROS client reads them.
    /// </summary>
    public sealed class PublishedFrameTests
    {
        /// <summary>
        /// A scanner that answers a known scan instead of casting rays, so the order of the beams the
        /// message carries can be read without a physics scene.
        /// </summary>
        private sealed class OrderedScanner : RobotSNAP.RaycastLaserScanner
        {
            public float[] Values;
            public override float[] Scan() => Values;
        }

        [Test]
        public void TheScanIsPublishedMirroredWithItsBeamsInReverse()
        {
            var host = new GameObject("published_scan");
            try
            {
                OrderedScanner scanner = host.AddComponent<OrderedScanner>();
                // A span that is not symmetric about zero, so a message that forgot to mirror it could not
                // pass by accident.
                scanner.samples = 4;
                scanner.angle_min = -1.0f;
                scanner.angle_max = 0.5f;
                scanner.Init();
                scanner.Values = new[] { 1f, 2f, 3f, 4f };

                RosMessageTypes.Sensor.LaserScanMsg message = scanner.InitializeMessage("laser");
                float[] published = scanner.ScanInRosFrame();

                // Unity measures clockwise from +z, ROS counter-clockwise from +x: the span is mirrored.
                Assert.That(message.angle_min, Is.EqualTo(-scanner.angle_max).Within(1e-5f));
                Assert.That(message.angle_max, Is.EqualTo(-scanner.angle_min).Within(1e-5f));
                Assert.That(message.angle_increment, Is.EqualTo(scanner.angle_increment).Within(1e-6f));

                // Beam i of the message has to sit at angle_min + i * angle_increment, and that angle has to
                // be the negation of the Unity angle of the beam it carries.
                Assert.That(published, Is.EqualTo(new[] { 4f, 3f, 2f, 1f }));
                for (int i = 0; i < scanner.samples; i++)
                {
                    int measured = scanner.samples - 1 - i;
                    float unityAngle = scanner.angle_min + measured * scanner.angle_increment;
                    Assert.That(
                        message.angle_min + i * message.angle_increment,
                        Is.EqualTo(-unityAngle).Within(1e-5f),
                        $"beam {i} carries the Unity beam at {unityAngle} rad");
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>The value of the cell a ROS frame point falls in, read the way a client reads it.</summary>
        private static sbyte CellAt(sbyte[] data, int width, float resolution, float originX, float originY, float x, float y)
        {
            int column = Mathf.FloorToInt((x - originX) / resolution);
            int row = Mathf.FloorToInt((y - originY) / resolution);
            return data[row * width + column];
        }

        [Test]
        public void ThePublishedGridPutsAWalkableCellWhereTheRosFrameSaysItIs()
        {
            // Unity x runs over [-1.5, 1.5] in three cells and z over [-1, 1] in two, so the two axes have
            // different cell counts: a grid published without swapping them cannot pass this test.
            var bounds = new Bounds(Vector3.zero, new Vector3(3f, 0f, 2f));
            var walkable = new bool[6];
            // The walkability grid counts its first index towards -x: cell (x = 2, y = 0) is the one at the
            // Unity (min x, min z) corner of the free area.
            walkable[0 * 3 + 2] = true;
            OccupancyGrid grid = OccupancyGrid.FromMask(3, 2, bounds, walkable);

            sbyte[] data = SimulationStatePublisher.MapDataInRosOrder(grid, out int width, out int height);

            Assert.That(data, Is.Not.Null);
            Assert.That(width, Is.EqualTo(2), "the published width counts the ROS x axis, which is Unity z");
            Assert.That(height, Is.EqualTo(3), "the published height counts the ROS y axis, which is Unity x");

            float resolution = grid.CellSizeZ;
            float originX = bounds.min.z;   // ROS x is Unity z, and it grows away from the low corner.
            float originY = -bounds.max.x;  // ROS y is the negated Unity x.

            // That cell stands at Unity (-1, -0.5), so the ROS frame puts it at (-0.5, +1).
            Assert.That(CellAt(data, width, resolution, originX, originY, -0.5f, 1f), Is.EqualTo((sbyte)0));
            Assert.That(CellAt(data, width, resolution, originX, originY, -0.5f, -1f), Is.EqualTo((sbyte)100));
            Assert.That(CellAt(data, width, resolution, originX, originY, 0.5f, 1f), Is.EqualTo((sbyte)100));
        }
    }
}
