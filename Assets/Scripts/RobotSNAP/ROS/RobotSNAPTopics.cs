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
        // -- the ten topics of the contract, in the order a client reads them ------

        /// <summary>
        /// The slots of the ten-name contract, in the order <see cref="All"/> lists them. The metrics stream
        /// of <see cref="RosTopicNames"/> is deliberately absent: the contract is what a client answers on,
        /// and the metrics stream is the additive one a reader opts into.
        /// </summary>
        public static readonly RosTopicSlot[] ContractSlots =
        {
            RosTopicSlot.Clock,
            RosTopicSlot.Odom,
            RosTopicSlot.Scan,
            RosTopicSlot.Map,
            RosTopicSlot.CmdVel,
            RosTopicSlot.SimulationState,
            RosTopicSlot.SimulationAgents,
            RosTopicSlot.SimulationControl,
            RosTopicSlot.SimulationControlResult,
            RosTopicSlot.ResetDone,
        };

        /// <summary>The table the application ships with, which is what an unconfigured session speaks.</summary>
        private static readonly RosTopicNames Shipped = new RosTopicNames();

        private static RosTopicNames _names;

        /// <summary>
        /// The names in force. They are the shipped ones until a configuration replaces them, which is what
        /// makes a session nobody configured indistinguishable from the one the project has always run.
        /// </summary>
        public static RosTopicNames Names => _names ?? Shipped;

        /// <summary>
        /// Takes the table of a configuration as the one every stream of the session is built from. A null
        /// table puts the shipped names back, which is how a caller says "nothing was configured".
        ///
        /// <paramref name="publishFrequency"/> is the rate every scheduled stream adopts, or zero - the
        /// shipped answer - when each stream keeps the rate it was authored with. Zero is the default
        /// because the streams of one robot do not share a cadence: a lidar sweeping at twenty hertz beside
        /// an odometry at fifty is what a client expects, and one number for the whole session would flatten
        /// that.
        /// </summary>
        public static void Use(RosTopicNames names, float publishFrequency = 0f)
        {
            _names = names;
            PublishFrequency = publishFrequency > 0f ? publishFrequency : 0f;
        }

        /// <summary>Puts the shipped names back.</summary>
        public static void ForgetOverrides()
        {
            _names = null;
            PublishFrequency = 0f;
        }

        /// <summary>
        /// The rate every scheduled stream publishes at, or zero when each stream keeps its own. It is a
        /// session-wide setting only for a reader who wants one: leaving it at zero is what the application
        /// has always done.
        /// </summary>
        public static float PublishFrequency { get; private set; }

        /// <summary>
        /// The rate a publisher should run at: the session's when one was asked for, and the rate the
        /// component was authored with otherwise.
        /// </summary>
        public static float ResolveFrequency(float authored)
            => PublishFrequency > 0f ? PublishFrequency : authored;

        /// <summary>The name the table in force holds for one stream.</summary>
        public static string Of(RosTopicSlot slot) => Names.Get(slot);

        /// <summary>
        /// The name a component should register under: the configured one, and the name the component itself
        /// was authored with when the configuration carries none for that stream. It is what lets a publisher
        /// written before this table existed keep its own name until somebody renames the stream.
        /// </summary>
        public static string Resolve(RosTopicSlot slot, string authored)
        {
            string configured = Names.Get(slot);
            return string.IsNullOrWhiteSpace(configured) ? authored : configured;
        }

        /// <summary>Simulation clock, published by <c>ROSClockPublisher</c>.</summary>
        public static string Clock => Names.Clock;

        /// <summary>Robot pose and twist, published by <c>OdometryPublisher</c>.</summary>
        public static string Odom => Names.Odom;

        /// <summary>Lidar scan, published by <c>LaserScanPublisher</c>.</summary>
        public static string Scan => Names.Scan;

        /// <summary>Occupancy grid of the applied scenario, published by <c>SimulationStatePublisher</c>.</summary>
        public static string Map => Names.Map;

        /// <summary>Robot velocity command, consumed by <c>RobotInputController</c>.</summary>
        public static string CmdVel => Names.CmdVel;

        /// <summary>Whole session snapshot as JSON, published by <c>SimulationStatePublisher</c>.</summary>
        public static string SimulationState => Names.SimulationState;

        /// <summary>Every agent in the robot frame as JSON, published by <c>AgentDetectorROS</c>.</summary>
        public static string SimulationAgents => Names.SimulationAgents;

        /// <summary>Every command, session and crowd alike, as JSON, consumed by <c>SimulationControlBridge</c>.</summary>
        public static string SimulationControl => Names.SimulationControl;

        /// <summary>Answer to one command, as JSON, published by <c>SimulationControlBridge</c>.</summary>
        public static string SimulationControlResult => Names.SimulationControlResult;

        /// <summary>Published once the world is ready, the handshake a client waits for.</summary>
        public static string ResetDone => Names.ResetDone;

        /// <summary>Every topic of the contract, for a tool that has to walk the surface.</summary>
        public static string[] All => Names.All;

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
