// Copyright (c) 2021, Members of Yale Interactive Machines Group, Yale University,
// Nathan Tsoi
// All rights reserved.
// This source code is licensed under the BSD-style license found in the
// LICENSE file in the root directory of this source tree. 

using UnityEngine;

namespace RobotSNAP
{
    public abstract class LaserScanner : MonoBehaviour
    {
        /// <summary>
        /// One scan in the scanner's own frame, beams in the order they were measured.
        /// </summary>
        public abstract float[] Scan();

        /// <summary>
        /// The same scan, in the ROS frame.
        ///
        /// ROS measures a scan counter-clockwise from <c>+x</c> in a right-handed frame, Unity
        /// clockwise from <c>+z</c> in a left-handed one, so every beam of a Unity scan sits at the
        /// opposite bearing in ROS and the beams come out in the reverse order. A <c>LaserScan</c>
        /// carries the ROS frame, so this is what a publisher hands it; the scanner keeps its own
        /// order for its visualiser and its gizmos.
        /// </summary>
        public abstract float[] ScanInRosFrame();

        public abstract float ScanPeriod();
        public abstract RosMessageTypes.Sensor.LaserScanMsg InitializeMessage(string FrameID);
    }
}
