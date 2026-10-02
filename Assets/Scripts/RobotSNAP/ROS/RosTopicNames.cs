using System;

namespace RobotSNAP.ROS
{
    /// <summary>
    /// One stream of the RobotSNAP contract, named so a settings page can walk the surface without
    /// reflection and without a second list that could drift from the first.
    /// </summary>
    public enum RosTopicSlot
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
        Metrics
    }

    /// <summary>
    /// The names of the streams a session speaks, as the reader may rename them.
    ///
    /// A name is a contract with whatever is on the other side of the socket, so the shipped spelling is the
    /// default and a reader who changes one is told what it costs. The table carries one name per
    /// <see cref="RosTopicSlot"/>; the ten of <see cref="RobotSNAPTopics.All"/> are the contract a client of
    /// the simulation already speaks, and the metrics stream is the additive one that
    /// <see cref="RobotSNAP.Metrics.MetricsContract"/> owns.
    ///
    /// The names are relative - they carry no robot id and no environment prefix, which are joined onto them
    /// where a stream is built. That is what keeps one table enough for a fleet: the same table names
    /// <c>/odom</c>, <c>/robot_2/odom</c> and <c>/env/odom</c>.
    /// </summary>
    [Serializable]
    public sealed class RosTopicNames
    {
        public string Clock = "/clock";
        public string Odom = "/odom";
        public string Scan = "/scan";
        public string Map = "/map";
        public string CmdVel = "/cmd_vel";
        public string SimulationState = "/simulation/state";
        public string SimulationAgents = "/simulation/agents";
        public string SimulationControl = "/simulation/control";
        public string SimulationControlResult = "/simulation/control_result";
        public string ResetDone = "/reset_done";
        public string Metrics = "/simulation/metrics";

        /// <summary>One row of the table: the slot, the name it ships with, and what it carries.</summary>
        public readonly struct Row
        {
            public readonly RosTopicSlot Slot;
            public readonly string Default;
            public readonly string Label;
            public readonly string Tooltip;

            public Row(RosTopicSlot slot, string shipped, string label, string tooltip)
            {
                Slot = slot;
                Default = shipped;
                Label = label;
                Tooltip = tooltip;
            }
        }

        /// <summary>
        /// Every stream, in the order a settings page shows them, with the name each one ships under. It is
        /// the one list of the surface: <see cref="Get"/> and <see cref="Set"/> read it, the page walks it,
        /// and a stream added to the contract is added here once.
        /// </summary>
        public static readonly Row[] Rows =
        {
            new Row(RosTopicSlot.Clock, "/clock", "Clock",
                "Simulated time, published for a client that stamps its own messages."),
            new Row(RosTopicSlot.Odom, "/odom", "Odometry",
                "Pose and twist of the robot. A second robot answers on its own copy, under its id."),
            new Row(RosTopicSlot.Scan, "/scan", "Lidar scan",
                "The lidar sweep of the robot."),
            new Row(RosTopicSlot.Map, "/map", "Occupancy grid",
                "The map of the applied scenario, as a grid a planner can read."),
            new Row(RosTopicSlot.CmdVel, "/cmd_vel", "Velocity command",
                "The command that drives the robot. This is the stream a policy writes to."),
            new Row(RosTopicSlot.SimulationState, "/simulation/state", "Session state",
                "The whole session - scenario, map, crowd, play or pause - as JSON."),
            new Row(RosTopicSlot.SimulationAgents, "/simulation/agents", "Agents",
                "Every agent of the scene in the frame of the robot, as JSON."),
            new Row(RosTopicSlot.SimulationControl, "/simulation/control", "Control commands",
                "Every command a client sends back - play, pause, reset, load a scenario, drive an agent."),
            new Row(RosTopicSlot.SimulationControlResult, "/simulation/control_result", "Control answers",
                "The answer to one command, as JSON."),
            new Row(RosTopicSlot.ResetDone, "/reset_done", "World ready",
                "Published once the world is built: the handshake a client waits for."),
            new Row(RosTopicSlot.Metrics, "/simulation/metrics", "Episode metrics",
                "One finished episode as JSON. Additive: it is not part of the ten-name contract.")
        };

        /// <summary>The name held for a stream.</summary>
        public string Get(RosTopicSlot slot)
        {
            switch (slot)
            {
                case RosTopicSlot.Clock: return Clock;
                case RosTopicSlot.Odom: return Odom;
                case RosTopicSlot.Scan: return Scan;
                case RosTopicSlot.Map: return Map;
                case RosTopicSlot.CmdVel: return CmdVel;
                case RosTopicSlot.SimulationState: return SimulationState;
                case RosTopicSlot.SimulationAgents: return SimulationAgents;
                case RosTopicSlot.SimulationControl: return SimulationControl;
                case RosTopicSlot.SimulationControlResult: return SimulationControlResult;
                case RosTopicSlot.ResetDone: return ResetDone;
                case RosTopicSlot.Metrics: return Metrics;
                default: return string.Empty;
            }
        }

        /// <summary>Holds a name for a stream, as given: callers normalize first.</summary>
        public void Set(RosTopicSlot slot, string value)
        {
            switch (slot)
            {
                case RosTopicSlot.Clock: Clock = value; break;
                case RosTopicSlot.Odom: Odom = value; break;
                case RosTopicSlot.Scan: Scan = value; break;
                case RosTopicSlot.Map: Map = value; break;
                case RosTopicSlot.CmdVel: CmdVel = value; break;
                case RosTopicSlot.SimulationState: SimulationState = value; break;
                case RosTopicSlot.SimulationAgents: SimulationAgents = value; break;
                case RosTopicSlot.SimulationControl: SimulationControl = value; break;
                case RosTopicSlot.SimulationControlResult: SimulationControlResult = value; break;
                case RosTopicSlot.ResetDone: ResetDone = value; break;
                case RosTopicSlot.Metrics: Metrics = value; break;
            }
        }

        /// <summary>The shipped name of a stream, whatever the table in force holds.</summary>
        public static string Shipped(RosTopicSlot slot)
        {
            foreach (Row row in Rows)
            {
                if (row.Slot == slot)
                    return row.Default;
            }

            return string.Empty;
        }

        /// <summary>
        /// The ten names of the simulation contract, in the order a client reads them. The metrics stream is
        /// deliberately absent: it is the additive one, and a client that walks this list walks the surface
        /// that answers the commands it sends.
        /// </summary>
        public string[] All
        {
            get
            {
                RosTopicSlot[] slots = RobotSNAPTopics.ContractSlots;
                var names = new string[slots.Length];
                for (int index = 0; index < names.Length; index++)
                    names[index] = Get(slots[index]);

                return names;
            }
        }

        /// <summary>An independent copy, so a loaded configuration is not the one being edited.</summary>
        public RosTopicNames Clone()
        {
            var copy = new RosTopicNames();
            CopyTo(copy);
            return copy;
        }

        /// <summary>Copies every name onto another table.</summary>
        public void CopyTo(RosTopicNames target)
        {
            if (target == null)
                return;

            foreach (Row row in Rows)
            {
                string name = Get(row.Slot);
                target.Set(row.Slot, string.IsNullOrWhiteSpace(name) ? row.Default : name);
            }
        }

        /// <summary>Puts every stream back to the name the application ships with.</summary>
        public void ResetToDefaults()
        {
            foreach (Row row in Rows)
                Set(row.Slot, row.Default);
        }

        /// <summary>
        /// Whether a typed name can be given to a stream, and the form it is held in.
        ///
        /// A topic name is a path on the wire, so what is refused is what ROS2 refuses rather than what this
        /// project happens to use: a name is made of letters, digits, underscores and separators, and the
        /// separators are re-joined here so that <c>odom</c>, <c>/odom</c> and <c>//odom//</c> are one name.
        /// The name stays relative: a robot id and an environment prefix are joined onto it later, and a name
        /// that already carried either would be joined twice.
        /// </summary>
        public static bool TryNormalize(string input, out string normalized, out string problem)
        {
            normalized = null;
            problem = null;

            string trimmed = (input ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                problem = "A topic name cannot be blank.";
                return false;
            }

            foreach (char character in trimmed)
            {
                bool allowed = (character >= 'a' && character <= 'z')
                               || (character >= 'A' && character <= 'Z')
                               || (character >= '0' && character <= '9')
                               || character == '_'
                               || character == '/';
                if (!allowed)
                {
                    problem = "A topic name may only carry letters, digits, '_' and '/'.";
                    return false;
                }
            }

            // The separators are re-joined rather than refused, so `odom`, `/odom` and `//odom//` are one
            // name and a reader who typed one of them gets what they meant.
            string collapsed = trimmed;
            while (collapsed.Contains("//"))
                collapsed = collapsed.Replace("//", "/");

            collapsed = collapsed.Trim('/');
            if (collapsed.Length == 0)
            {
                problem = "A topic name cannot be only separators.";
                return false;
            }

            normalized = "/" + collapsed;
            return true;
        }
    }
}
