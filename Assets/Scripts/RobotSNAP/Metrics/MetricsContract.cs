namespace RobotSNAP.Metrics
{
    /// <summary>
    /// One place for the names the metrics layer hands to the outside world, so the recorder that writes an
    /// episode, the router that answers a question about it and the exporter that puts it on disk cannot
    /// disagree on a spelling.
    ///
    /// The stream this file adds is deliberately kept out of
    /// <see cref="RobotSNAP.ROS.RobotSNAPTopics"/>: that table is the ten-name contract a client of the
    /// simulation already speaks, guarded by a test on both sides of the socket, and a metrics stream bolted
    /// onto it would move a surface nobody asked to move. The episode stream is a second, additive contract -
    /// the same JSON-over-<c>std_msgs/String</c> shape as <c>/simulation/state</c>, on its own topic - and
    /// this file is where it is written down.
    ///
    /// The outcome vocabulary is the project's own, the one the scenario state and the Python environment
    /// already speak: an episode ends because the robot reached its goal, because it collided, because it left
    /// the map, because the mission's time ran out, or because the session was stopped. A recorder that
    /// cannot say which of those happened says <see cref="OutcomeUnknown"/> rather than guessing, because a
    /// metric whose cause is invented is worse than a metric that admits it is missing.
    /// </summary>
    public static class MetricsContract
    {
        /// <summary>Topic carrying one finished episode as JSON, published by <see cref="MetricsRecorder"/>.</summary>
        public const string EpisodeTopic = "/simulation/metrics";

        /// <summary>Folder, under <c>StreamingAssets</c>, the exported records are written to.</summary>
        public const string ExportFolder = "metrics";

        /// <summary>Name of the index listing every exported session.</summary>
        public const string IndexFileName = "metrics_index.json";

        /// <summary>The robot reached its goal.</summary>
        public const string OutcomeGoal = "goal";

        /// <summary>The robot came closer to an obstacle than the collision distance.</summary>
        public const string OutcomeCollision = "collision";

        /// <summary>The robot's centre left the footprint of the applied map.</summary>
        public const string OutcomeOutOfBounds = "out_of_bounds";

        /// <summary>The episode hit its own time limit before any of the above.</summary>
        public const string OutcomeTimeout = "timeout";

        /// <summary>The session was stopped, reset or reloaded while the episode was running.</summary>
        public const string OutcomeStopped = "stopped";

        /// <summary>No terminal condition was ever observed, which is a fact worth recording.</summary>
        public const string OutcomeUnknown = "unknown";

        /// <summary>
        /// Key one trajectory of the episode document is filed under: the robot's roster id (<c>robot_1</c>)
        /// and <c>human_&lt;id&gt;</c> for a crowd member, so a reader can tell the two apart without knowing
        /// which ids the scene happened to hand out. The points under that key are <c>[t, x, z]</c> with
        /// <c>t</c> in simulated seconds and <c>x</c>/<c>z</c> in the world metres of the map - the same frame
        /// <c>/map</c> publishes its origin in - so a top-down view needs no conversion.
        /// </summary>
        public static string RobotTrackKey(string robotId)
            => string.IsNullOrEmpty(robotId) ? "robot" : robotId;

        /// <inheritdoc cref="RobotTrackKey"/>
        public static string HumanTrackKey(int humanId) => "human_" + humanId;

        /// <summary>Name of the session document holding every episode of one session.</summary>
        public static string SessionFileName(string sessionId) => "session_" + sessionId + ".json";

        /// <summary>Name of the tabular companion of <see cref="SessionFileName"/>, without the trajectories.</summary>
        public static string SessionCsvFileName(string sessionId) => "session_" + sessionId + ".csv";

        /// <summary>Full topic name of the episode stream under an environment prefix, or the bare name.</summary>
        public static string EpisodeTopicFor(string prefix)
            => RobotSNAP.ROS.RobotSNAPTopics.Full(EpisodeTopic, prefix);
    }
}
