using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RosMessageTypes.Std;
using RobotSNAP.Agents;
using RobotSNAP.Core;
using RobotSNAP.Core.Scenario;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using UnityEngine;
using SimMsgs = RosMessageTypes.Simulation;

namespace RobotSNAP.ROS
{
    /// <summary>
    /// Drives a running session from outside Unity: it listens on two command topics, runs what they carry -
    /// through <see cref="SimulationCommandRouter"/> for the session, through <see cref="HumanManager"/> for
    /// the crowd - and answers on a third topic with what happened.
    ///
    /// The bodies are JSON carried in std_msgs/String rather than a message of their own, so the same client
    /// drives the scene whether the peer is a ROS2 ros_tcp_endpoint or the pure-Python server of
    /// robotSNAP_ws/src/robotsnap/bridge, and no message has to be generated for either of them.
    ///
    ///   /simulation/control         {"command":"pause"}
    ///                                {"command":"set_robot_goal","x":4.5,"z":2.0}
    ///   /simulation/humans/control  {"commands":[{"id":3,"vx":1.0,"vz":0.0},{"id":4,"stop":true}]}
    ///   /simulation/control_result  {"command":"pause","ok":true,"message":"simulation paused",
    ///                                "sim_time_seconds":12.34}
    ///
    /// Nothing here may throw when no ROS server is listening, which is how this project usually runs: the
    /// connector raises an exception on a topic that has no registered publisher, so every publish goes
    /// through <see cref="CanPublish"/> first. A body that cannot be read comes back as ok=false rather than
    /// as an exception, so a client always sees why its command was refused.
    /// </summary>
    public class SimulationControlBridge : MonoBehaviour
    {
        [Header("ROS Configuration")]
        [Tooltip("Take the topic prefix from EnvROS, like the other publishers of this folder.")]
        [SerializeField] private bool autoDetectPrefix = true;

        [Tooltip("Prefix used when auto detection is off.")]
        [SerializeField] private string customPrefix = "";

        [Header("Topic Names (the EnvROS prefix is prepended)")]
        [Tooltip("Topic carrying one session command per message.")]
        [SerializeField] private string controlTopic = "/simulation/control";

        [Tooltip("Topic carrying a batch of crowd commands.")]
        [SerializeField] private string humansControlTopic = "/simulation/humans/control";

        [Tooltip("Topic the answer to every command is published on.")]
        [SerializeField] private string resultTopic = "/simulation/control_result";

        [Header("Debug")]
        [SerializeField] private bool logPublishEvents = false;

        [Header("References")]
        [SerializeField] private EnvROS _envROS;

        // ROS side
        private ROSConnection _ros;
        private string _prefix = "";
        private string _controlTopicName;
        private string _humansControlTopicName;
        private string _resultTopicName;
        private bool _initialized;

        // Application side. The router keeps no reference between two commands, and the manager is refreshed
        // lazily because a scenario load destroys and rebuilds the environments, and this hook with them.
        private readonly SimulationCommandRouter _router = new SimulationCommandRouter();
        private ScenarioManager _scenarioManager;

        // The command of the crowd topic, as it is named in its answers.
        private const string HumansCommand = "humans/control";

        // Command topics are owned by one bridge at a time: the connection outlives the environments, so the
        // callbacks of a bridge that went away with the previous scenario would still be called, and every
        // command would run twice.
        private static SimulationControlBridge s_active;

        #region Unity Lifecycle

        private void Start()
        {
            Initialize();
        }

        private void OnDestroy()
        {
            if (_scenarioManager != null)
                _scenarioManager.OnScenarioApplied -= OnScenarioApplied;

            // A bridge that was replaced by the one of a new environment leaves the topics to it.
            if (s_active == this)
                ReleaseTopics();
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Resolves the connection, registers the result publisher and subscribes to the two command topics.
        /// Called from Start, and once more from the inspector menu to retry by hand.
        /// </summary>
        public void Initialize()
        {
            _ros = ROSConnection.GetOrCreateInstance();
            if (_ros == null)
            {
                Debug.LogError($"[{name}] ROSConnection unavailable, simulation control will not be answered");
                enabled = false;
                return;
            }

            ResolveTopics(force: true);

            _scenarioManager ??= FindAnyObjectByType<ScenarioManager>();
            if (_scenarioManager != null)
            {
                _scenarioManager.OnScenarioApplied -= OnScenarioApplied;
                _scenarioManager.OnScenarioApplied += OnScenarioApplied;
            }

            Subscribe();

            RegisterServices();

            _initialized = true;

            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Listening for simulation control:\n" +
                          $"  Commands: {_controlTopicName}\n" +
                          $"  Crowd: {_humansControlTopicName}\n" +
                          $"  Answers: {_resultTopicName}");
            }
        }

        /// <summary>
        /// Builds the three topic names from the EnvROS prefix and registers the result topic. Loading a
        /// scenario destroys and rebuilds the environments, so the EnvROS instance - and the prefix it
        /// carries - can change mid-session; the names are rebuilt, and the topics re-registered, only when it
        /// did change, because the connector warns about a topic that is registered twice.
        /// </summary>
        private void ResolveTopics(bool force)
        {
            // A destroyed EnvROS compares equal to null, so this looks the new one up after a scenario load.
            _envROS ??= FindAnyObjectByType<EnvROS>();

            string prefix = "";
            if (autoDetectPrefix && _envROS != null)
                prefix = _envROS.Prefix;
            else if (!string.IsNullOrEmpty(customPrefix))
                prefix = customPrefix;

            prefix = (prefix ?? "").Trim('/');
            if (!force && prefix == _prefix && _controlTopicName != null)
                return;

            _prefix = prefix;
            _controlTopicName = BuildTopic(prefix, controlTopic);
            _humansControlTopicName = BuildTopic(prefix, humansControlTopic);
            _resultTopicName = BuildTopic(prefix, resultTopic);

            RegisterTopic<StringMsg>(_resultTopicName);
        }

        /// <summary>
        /// Registers a topic unless it already carries a publisher: the connector warns on a topic registered
        /// twice, and initializing the component again must stay a quiet operation.
        /// </summary>
        private void RegisterTopic<T>(string topicName) where T : Message
        {
            if (_ros == null || string.IsNullOrEmpty(topicName)) return;

            RosTopicState topic = _ros.GetTopic(topicName);
            if (topic != null && topic.IsPublisher) return;

            _ros.RegisterPublisher<T>(topicName);
        }

        /// <summary>
        /// Topic name in the house shape (/prefix/topic). The prefix is trimmed so a configured value carrying
        /// its own slashes cannot produce a topic with a doubled separator.
        /// </summary>
        private static string BuildTopic(string prefix, string topic)
        {
            string stream = (topic ?? "").Trim('/');
            if (string.IsNullOrEmpty(stream)) return null;
            return string.IsNullOrEmpty(prefix) ? $"/{stream}" : $"/{prefix}/{stream}";
        }

        /// <summary>
        /// Answers the two ROS2 services with the same router the command topic uses, so a roboticist whose
        /// client already calls /unity/reset and /unity/play does not have to learn the JSON topics as well.
        /// Both were declared on EnvROS and never registered, so calling them did nothing at all.
        /// </summary>
        private void RegisterServices()
        {
            if (_envROS == null || !_envROS.IsInitialized) return;

            _envROS.RegisterResetService(OnResetService);
            _envROS.RegisterPausePlayService(OnPausePlayService);
        }

        /// <summary>
        /// Reset asked over the service: the dataset of the request names the scenario to load, and an empty
        /// one simply replays the scenario that is already loaded.
        /// </summary>
        private SimMsgs.ResetResponse OnResetService(SimMsgs.ResetRequest request)
        {
            string scenario = request.dataset?.Trim();
            string body = string.IsNullOrEmpty(scenario)
                ? "{\"command\":\"reset\"}"
                : $"{{\"command\":\"reset\",\"scenario\":\"{scenario}\"}}";

            CommandResult result = _router.Execute(body);
            if (!result.Ok)
                Debug.LogWarning($"[{name}] Reset service refused: {result.Message}");

            // The scenario has just been put back on its marks, so the distance that matters is the one the
            // robot still has to cover: from where it stands now to its goal. Zero when it has none.
            Robot robot = FindAnyObjectByType<Robot>();
            float distance = robot != null && robot.HasGoal ? Vector3.Distance(robot.Position, robot.Goal) : 0f;

            return new SimMsgs.ResetResponse(result.Ok, distance);
        }

        /// <summary>Play or pause asked over the service.</summary>
        private SimMsgs.PausePlayResponse OnPausePlayService(SimMsgs.PausePlayRequest request)
        {
            string body = request.play ? "{\"command\":\"play\"}" : "{\"command\":\"pause\"}";
            CommandResult result = _router.Execute(body);

            return new SimMsgs.PausePlayResponse(result.Ok, result.Message);
        }

        /// <summary>
        /// Subscribes to the two command topics, through the connection rather than through
        /// <see cref="EnvROS.RegisterSubscriber{T}"/>: the names are already built from the prefix here, and
        /// the EnvROS registration would prepend the prefix of its own instance a second time, which would
        /// also ignore the configured prefix and listen on the wrong topic. The topic is taken over from a
        /// bridge of a previous environment when there is one.
        /// </summary>
        private void Subscribe()
        {
            if (_ros == null) return;

            if (s_active != null && s_active != this)
                s_active.ReleaseTopics();

            s_active = this;

            SubscribeTo(_controlTopicName, OnControlMessage);
            SubscribeTo(_humansControlTopicName, OnHumansControlMessage);
        }

        /// <summary>
        /// Registers one subscription. The connector appends a callback to its topic state without
        /// deduplicating it, so a name that already carries one is left alone; for a subscriber the guard is
        /// the same idea as the IsPublisher guard used before publishing, on a topic state that exposes
        /// HasSubscriberCallback instead.
        /// </summary>
        private void SubscribeTo(string topicName, Action<StringMsg> callback)
        {
            if (_ros == null || string.IsNullOrEmpty(topicName)) return;

            RosTopicState topic = _ros.GetTopic(topicName);
            if (topic != null && topic.HasSubscriberCallback) return;

            _ros.Subscribe<StringMsg>(topicName, callback);

            if (logPublishEvents)
                Debug.Log($"[{name}] Subscribed to {topicName}");
        }

        /// <summary>
        /// Gives the command topics back to the connection. A destroyed bridge whose callback stayed
        /// registered would answer commands from a scene that no longer exists, or answer them twice when the
        /// next environment subscribes. The connection drops every subscriber of a topic at once, which is
        /// what these two topics want: they are the command channel of this component and of nothing else.
        /// </summary>
        private void ReleaseTopics()
        {
            Unsubscribe(_controlTopicName);
            Unsubscribe(_humansControlTopicName);

            if (s_active == this)
                s_active = null;
        }

        /// <summary>Removes the subscribers of a topic, and does nothing without a connection or a name.</summary>
        private void Unsubscribe(string topicName)
        {
            if (_ros == null || string.IsNullOrEmpty(topicName)) return;

            _ros.Unsubscribe(topicName);
        }

        /// <summary>
        /// True when a topic can be published to. The connector throws on a topic that holds no publisher,
        /// which is exactly the state of a session running without a ROS server, so asking first keeps that
        /// case quiet.
        /// </summary>
        private bool CanPublish(string topicName)
        {
            if (_ros == null || string.IsNullOrEmpty(topicName)) return false;

            RosTopicState topic = _ros.GetTopic(topicName);
            return topic != null && topic.IsPublisher;
        }

        #endregion

        #region Scenario Event

        /// <summary>
        /// A new scenario rebuilds the environment, and with it the EnvROS this component reads its prefix
        /// from. When the prefix moved, the subscriptions move with the names.
        /// </summary>
        private void OnScenarioApplied(ScenarioData scenario)
        {
            if (!_initialized) return;

            string previousControlTopic = _controlTopicName;
            ResolveTopics(force: false);
            if (_controlTopicName == previousControlTopic) return;

            ReleaseTopics();
            Subscribe();
        }

        #endregion

        #region Commands

        /// <summary>
        /// One command from outside: the router runs it and its answer goes back on the result topic, so a
        /// client that never got an answer knows its message did not reach Unity.
        /// </summary>
        private void OnControlMessage(StringMsg message)
        {
            CommandResult result = _router.Execute(message != null ? message.data : null);
            PublishResult(result.Command, result.Ok, result.Message, null);
        }

        /// <summary>
        /// One batch of crowd commands. Every entry carries an id and a velocity in world metres per second,
        /// or "stop": true, which gives the human back to its own controller. The answer reports what was
        /// applied and which ids are not in the scene, because a client cannot see the crowd it is driving.
        /// </summary>
        private void OnHumansControlMessage(StringMsg message)
        {
            var unknownIds = new List<int>();
            bool ok = ApplyHumanCommands(message != null ? message.data : null, unknownIds, out string summary);
            PublishResult(HumansCommand, ok, summary, unknownIds);
        }

        /// <summary>
        /// Applies a crowd body and builds the sentence that describes it. Ids the scene does not hold are
        /// collected rather than dropped, and an entry that cannot be read makes the answer a refusal.
        /// </summary>
        private static bool ApplyHumanCommands(string json, List<int> unknownIds, out string summary)
        {
            summary = "";
            unknownIds.Clear();

            if (string.IsNullOrWhiteSpace(json))
            {
                summary = "empty command body";
                return false;
            }

            JObject body = SimulationCommandRouter.ParseBody(json, out string parseError);
            if (body == null)
            {
                summary = parseError;
                return false;
            }

            if (!SimulationCommandRouter.HasValue(body, "commands"))
            {
                summary = "missing key 'commands'";
                return false;
            }

            JToken commands = body["commands"];
            if (commands.Type != JTokenType.Array)
            {
                summary = "key 'commands' must be an array";
                return false;
            }

            HumanManager manager = FindAnyObjectByType<HumanManager>();
            if (manager == null)
            {
                summary = "no human manager in the scene";
                return false;
            }

            int applied = 0;
            int refused = 0;

            foreach (JToken entry in commands)
            {
                if (!TryReadHumanCommand(entry, out int id, out bool stop, out float vx, out float vz))
                {
                    refused++;
                    continue;
                }

                bool known = stop
                    ? manager.ClearExternalVelocity(id)
                    : manager.SetExternalVelocity(id, new Vector2(vx, vz));

                if (known)
                    applied++;
                else
                    unknownIds.Add(id);
            }

            // Counts are written with the invariant culture, so the answer reads the same in every locale.
            var parts = new List<string>
            {
                applied == 1 ? "1 command applied" : FormattableString.Invariant($"{applied} commands applied"),
                unknownIds.Count == 1
                    ? "1 unknown id"
                    : FormattableString.Invariant($"{unknownIds.Count} unknown ids")
            };

            if (refused > 0)
            {
                parts.Add(refused == 1
                    ? "1 entry ignored"
                    : FormattableString.Invariant($"{refused} entries ignored"));
            }

            summary = string.Join(", ", parts);

            return unknownIds.Count == 0 && refused == 0;
        }

        /// <summary>
        /// Reads one crowd entry. A missing velocity reads as zero, which is what a "stop" entry sends, and
        /// "stop" wins over a velocity given in the same entry. An entry whose keys are there but unreadable,
        /// or that carries no id, is refused rather than applied to agent 0.
        /// </summary>
        private static bool TryReadHumanCommand(JToken entry, out int id, out bool stop, out float vx, out float vz)
        {
            id = 0;
            stop = false;
            vx = 0f;
            vz = 0f;

            if (entry == null || entry.Type != JTokenType.Object)
                return false;

            var command = (JObject)entry;
            if (!SimulationCommandRouter.TryGetInt(command, "id", out id))
                return false;

            if (SimulationCommandRouter.TryGetBool(command, "stop", false, out bool wantsStop) && wantsStop)
            {
                stop = true;
                return true;
            }

            if (SimulationCommandRouter.HasValue(command, "stop"))
                return false;

            if (SimulationCommandRouter.HasValue(command, "vx")
                && !SimulationCommandRouter.TryGetFloat(command, "vx", out vx))
                return false;

            if (SimulationCommandRouter.HasValue(command, "vz")
                && !SimulationCommandRouter.TryGetFloat(command, "vz", out vz))
                return false;

            return true;
        }

        #endregion

        #region Result

        /// <summary>
        /// Publishes the answer to one command: the command that ran, whether it did what it asked, what
        /// happened, and the simulation time it happened at. Crowd commands also carry the ids the scene does
        /// not hold, so a client can spot a typo instead of waiting for a human that never moves.
        /// </summary>
        private void PublishResult(string command, bool ok, string message, List<int> unknownIds)
        {
            if (!CanPublish(_resultTopicName))
            {
                if (logPublishEvents)
                    Debug.Log($"[{name}] Nothing listening on {_resultTopicName}, answer to '{command}' dropped: {message}");

                return;
            }

            var payload = new Dictionary<string, object>
            {
                { "command", command ?? "" },
                { "ok", ok },
                { "message", message ?? "" },
                { "sim_time_seconds", SimulationTimeSeconds() }
            };

            if (unknownIds != null)
                payload["unknown_ids"] = unknownIds;

            string json = JsonConvert.SerializeObject(payload, Formatting.None);
            _ros.Publish(_resultTopicName, new StringMsg(json));

            if (logPublishEvents)
                Debug.Log($"[{name}] Published control result to {_resultTopicName}: {json}");
        }

        /// <summary>
        /// Time of the simulation clock, in seconds, rounded to the millisecond, or null when the scene has no
        /// clock. It is read from the clock rather than from Unity's own time, so a paused simulation reports
        /// the moment it stopped at.
        /// </summary>
        private static object SimulationTimeSeconds()
        {
            Clock clock = Clock.Instance;
            if (clock == null)
                clock = FindAnyObjectByType<Clock>();

            return clock != null ? (object)Math.Round(clock.CurrentTimeSeconds, 3) : null;
        }

        #endregion

        #region Editor Utilities

        [ContextMenu("Log Configuration")]
        private void EditorLogConfiguration()
        {
            Debug.Log($"[{name}] Configuration:\n" +
                      $"  Prefix: '{_prefix}'\n" +
                      $"  Commands: {_controlTopicName}\n" +
                      $"  Crowd: {_humansControlTopicName}\n" +
                      $"  Answers: {_resultTopicName}\n" +
                      $"  ROS connection: {(_ros != null ? "OK" : "Missing")}\n" +
                      $"  EnvROS: {(_envROS != null ? "OK" : "Missing")}\n" +
                      $"  Scenario manager: {(_scenarioManager != null ? "OK" : "Missing")}");
        }

        #endregion
    }
}
