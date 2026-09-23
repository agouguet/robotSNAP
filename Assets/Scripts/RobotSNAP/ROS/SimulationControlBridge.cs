using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using RosMessageTypes.Std;
using RobotSNAP.Core;
using RobotSNAP.Core.Scenario;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using UnityEngine;

namespace RobotSNAP.ROS
{
    /// <summary>
    /// Drives a running session from outside Unity: it listens on one command topic, runs what it carries
    /// through <see cref="SimulationCommandRouter"/>, and answers on a second topic with what happened. One
    /// topic carries every command there is - session and crowd alike - so a client has a single place to
    /// send to and a single place to read the answer from.
    ///
    /// The bodies are JSON carried in std_msgs/String rather than a message of their own, so the same client
    /// drives the scene whether the peer is a ROS2 ros_tcp_endpoint or the pure-Python server of
    /// robotSNAP_ws/src/robotsnap/bridge, and no message has to be generated for either of them.
    ///
    ///   /simulation/control         {"command":"pause"}
    ///                                {"command":"set_robot_goal","x":4.5,"z":2.0}
    ///                                {"command":"humans","commands":[{"id":3,"vx":1.0,"vz":0.0},{"id":4,"stop":true}]}
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
        [Tooltip("Topic carrying one command per message, session or crowd.")]
        [SerializeField] private string controlTopic = RobotSNAPTopics.SimulationControl;

        [Tooltip("Topic the answer to every command is published on.")]
        [SerializeField] private string resultTopic = RobotSNAPTopics.SimulationControlResult;

        [Header("Debug")]
        [SerializeField] private bool logPublishEvents = false;

        [Header("References")]
        [SerializeField] private EnvROS _envROS;

        // ROS side
        private ROSConnection _ros;
        private string _prefix = "";
        private string _controlTopicName;
        private string _resultTopicName;
        private bool _initialized;

        // Application side. The router keeps no reference between two commands, and the manager is resolved
        // lazily: this component lives beside the connection and outlives every environment, while the
        // ScenarioManager is a scene object that can be replaced between two commands.
        private readonly SimulationCommandRouter _router = new SimulationCommandRouter();
        private ScenarioManager _scenarioManager;

        #region Unity Lifecycle

        private void Start()
        {
            Initialize();
        }

        private void OnDestroy()
        {
            if (_scenarioManager != null)
                _scenarioManager.OnScenarioApplied -= OnScenarioApplied;

            // Deliberately nothing to the topics here. This component is created beside the connection and
            // destroyed with it, at the end of the session; a listener that unsubscribed on the way out would
            // race the next session's own subscription, which may well run before this OnDestroy does. The
            // next bridge clears the topic for itself in Subscribe, so a callback left behind is handled.
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Resolves the connection, registers the result publisher and subscribes to the command topic.
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

            _initialized = true;

            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Listening for simulation control:\n" +
                          $"  Commands: {_controlTopicName}\n" +
                          $"  Answers: {_resultTopicName}");
            }
        }

        /// <summary>
        /// Builds the two topic names from the EnvROS prefix and registers the result topic. Loading a
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
        /// Topic name in the house shape (/prefix/topic), built by the one rule of the project so this bridge
        /// and the client cannot disagree on a name. See <see cref="RobotSNAPTopics.Full"/>.
        /// </summary>
        private static string BuildTopic(string prefix, string topic)
            => RobotSNAPTopics.Full(topic, prefix);

        /// <summary>
        /// Subscribes to the command topic, through the connection rather than through
        /// <see cref="EnvROS.RegisterSubscriber{T}"/>: the names are already built from the prefix here, and
        /// the EnvROS registration would prepend the prefix of its own instance a second time, which would
        /// also ignore the configured prefix and listen on the wrong topic. The topic is taken over from a
        /// bridge of a previous environment when there is one.
        /// </summary>
        private void Subscribe()
        {
            if (_ros == null) return;

            // This project runs with "Enter Play Mode Options" and no domain reload, so the connection - and
            // the callbacks its topic states carry - survive a Play session. A new bridge looking at a name
            // that already holds the callback of the session before would find it busy and leave itself
            // unsubscribed, while every command went to the dead bridge. This topic belongs to this component
            // alone, so it clears it and takes it over rather than asking whether it is free.
            Unsubscribe(_controlTopicName);

            SubscribeTo(_controlTopicName, OnControlMessage);
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
            // A bridge the scenario reload destroyed is still held by the connection until the next one takes
            // the topic over. This component compares equal to null once destroyed, so its commands stop here
            // instead of being run by a session that is gone.
            if (this == null) return;

            CommandResult result = _router.Execute(message != null ? message.data : null);
            PublishResult(result.Command, result.Ok, result.Message, result.UnknownIds, result.Robot);
        }

        #endregion

        #region Result

        /// <summary>
        /// Publishes the answer to one command: the command that ran, whether it did what it asked, what
        /// happened, and the simulation time it happened at. The crowd command also carries the ids the scene
        /// does not hold, so a client can spot a typo instead of waiting for a human that never moves.
        /// A robot command carries the id it addressed, so a caller can confirm which robot answered
        /// without re-reading the snapshot.
        /// </summary>
        private void PublishResult(
            string command,
            bool ok,
            string message,
            IReadOnlyList<int> unknownIds,
            string robot)
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

            if (!string.IsNullOrEmpty(robot))
                payload["robot"] = robot;

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
                      $"  Answers: {_resultTopicName}\n" +
                      $"  ROS connection: {(_ros != null ? "OK" : "Missing")}\n" +
                      $"  EnvROS: {(_envROS != null ? "OK" : "Missing")}\n" +
                      $"  Scenario manager: {(_scenarioManager != null ? "OK" : "Missing")}");
        }

        #endregion
    }
}
