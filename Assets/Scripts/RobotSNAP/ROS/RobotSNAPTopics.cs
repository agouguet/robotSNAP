namespace RobotSNAP.ROS
{
    /// <summary>
    /// The one place a topic of the RobotSNAP contract is named, and the one place a topic name is built
    /// from an environment prefix.
    ///
    /// It mirrors <c>robotSNAP_ws/src/robotsnap/topics.py</c> on the Python side: the same ten names, the
    /// same message types, the same idea that a client finds them without reading the scene. A component
    /// never writes a topic of its own any more, it takes the constant from here, so renaming a stream is
    /// one edit and the two sides of the socket cannot drift apart.
    ///
    /// The table, with the direction seen from Unity:
    ///
    ///   /clock                       rosgraph_msgs/Clock        out  simulation clock
    ///   /odom                        nav_msgs/Odometry          out  robot pose and twist
    ///   /scan                        sensor_msgs/LaserScan      out  lidar
    ///   /map                         nav_msgs/OccupancyGrid     out  occupancy grid of the scenario
    ///   /cmd_vel                     geometry_msgs/Twist        in   robot velocity command
    ///   /simulation/state            std_msgs/String, JSON      out  the session snapshot
    ///   /simulation/agents           std_msgs/String, JSON      out  every agent, in the robot frame
    ///   /simulation/control          std_msgs/String, JSON      in   every command
    ///   /simulation/control_result   std_msgs/String, JSON      out  the answer to one command
    ///   /reset_done                  std_msgs/Bool              out  the world is ready
    ///
    /// The custom <c>simulation_msgs</c> package is gone: one topic per kind of information and a standard
    /// message type wherever one exists is what keeps a session without ROS, and without generated
    /// messages, possible.
    /// </summary>
    public static class RobotSNAPTopics
    {
        // -- the ten topics, as they appear on the wire ------------------------

        /// <summary>Simulation clock, published by <c>ROSClockPublisher</c>.</summary>
        public const string Clock = "/clock";

        /// <summary>Robot pose and twist, published by <c>OdometryPublisher</c>.</summary>
        public const string Odom = "/odom";

        /// <summary>Lidar scan, published by <c>LaserScanPublisher</c>.</summary>
        public const string Scan = "/scan";

        /// <summary>Occupancy grid of the applied scenario, published by <c>SimulationStatePublisher</c>.</summary>
        public const string Map = "/map";

        /// <summary>Robot velocity command, consumed by <c>RobotInputController</c>.</summary>
        public const string CmdVel = "/cmd_vel";

        /// <summary>Whole session snapshot as JSON, published by <c>SimulationStatePublisher</c>.</summary>
        public const string SimulationState = "/simulation/state";

        /// <summary>Every agent in the robot frame as JSON, published by <c>AgentDetectorROS</c>.</summary>
        public const string SimulationAgents = "/simulation/agents";

        /// <summary>Every command, session and crowd alike, as JSON, consumed by <c>SimulationControlBridge</c>.</summary>
        public const string SimulationControl = "/simulation/control";

        /// <summary>Answer to one command, as JSON, published by <c>SimulationControlBridge</c>.</summary>
        public const string SimulationControlResult = "/simulation/control_result";

        /// <summary>Published once the world is ready, the handshake a client waits for.</summary>
        public const string ResetDone = "/reset_done";

        /// <summary>Every topic of the contract, for a tool that has to walk the surface.</summary>
        public static readonly string[] All =
        {
            Clock,
            Odom,
            Scan,
            Map,
            CmdVel,
            SimulationState,
            SimulationAgents,
            SimulationControl,
            SimulationControlResult,
            ResetDone,
        };

        // -- building a name ---------------------------------------------------

        /// <summary>
        /// A topic name in the shape <c>/prefix/topic</c>, the one every stream of an environment uses.
        ///
        /// A prefix is never glued to the topic: `scan` under the prefix `env` is `/env/scan`, not
        /// `/envscan`, so a client can guess the name the way ROS2 spells it. Slashes on either side are
        /// tolerated, so a name configured as `/scan/` or a prefix configured as `/env/` still come out
        /// right.
        ///
        /// The function is idempotent on purpose: builders hand a finished name to
        /// <see cref="EnvROS.RegisterPublisher{T}"/> and <see cref="EnvROS.Publish{T}"/>, which apply the
        /// prefix again, and an already prefixed name must survive that unchanged rather than gaining a
        /// second copy of the prefix.
        ///
        /// Returns <c>null</c> for a blank topic, which is how a component says it carries no stream.
        /// </summary>
        public static string Full(string topic, string prefix)
        {
            string stream = Normalize(topic);
            if (stream.Length == 0) return null;

            string root = Normalize(prefix);
            if (root.Length == 0) return $"/{stream}";

            // Already prefixed: handing this back untouched is what makes the function idempotent.
            if (stream.StartsWith(root + "/")) return $"/{stream}";

            return $"/{root}/{stream}";
        }

        /// <summary>
        /// A name without its surrounding slashes, the form the join is computed on.
        ///
        /// A null or blank name becomes the empty string, so a caller never has to check the prefix of an
        /// environment that carries none.
        /// </summary>
        public static string Normalize(string name)
        {
            return (name ?? string.Empty).Trim().Trim('/');
        }
    }
}
