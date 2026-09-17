using System;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;

namespace RobotSNAP.ROS
{
    /// <summary>
    /// Plumbing of the ROS bridge of one environment: it creates the connection, owns the topic prefix every
    /// stream of the environment is named with, and creates the two components that speak on it - the
    /// publisher of the session state and the listener of the commands sent back.
    ///
    /// It publishes no message and implements no service of its own. The components it creates register the
    /// topics they own, either on the connection directly or through <see cref="RegisterPublisher{T}"/> and
    /// <see cref="Publish{T}"/>, which name those topics with the prefix held here.
    /// </summary>
    public class EnvROS : MonoBehaviour
    {
        [Header("ROS Configuration")]
        [SerializeField] private bool logPublishEvents = false;
        [SerializeField] private bool autoInitialize = true;
        [Tooltip("Publish the state of the whole application - scenario, map, crowd, play/pause - on the " +
                 "simulation topics. The publisher lives on this same object, so a client always sees the " +
                 "bridge that is actually running.")]
        [SerializeField] private bool publishSimulationState = true;
        [Tooltip("Listen for the commands a client sends back - play, pause, reset, load a scenario, drive the " +
                 "robot, drive a human - on the simulation control topic. Off means the run is read-only.")]
        [SerializeField] private bool acceptRemoteControl = true;
        
        // ROS Connection
        private ROSConnection _ros;
        private string _prefix = "";
        private bool _initialized;
        
        public ROSConnection Ros => _ros;
        public bool IsInitialized => _initialized;
        public string Prefix => _prefix;
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            _ros = ROSConnection.GetOrCreateInstance();
            
            if (autoInitialize)
            {
                Initialize(_prefix);
            }

            if (publishSimulationState)
            {
                EnsureSimulationStatePublisher();
            }

            if (acceptRemoteControl)
            {
                EnsureSimulationControlBridge();
            }
        }
        
        #endregion
        
        #region Initialization
        
        /// <summary>
        /// Initialize the ROS bridge with a prefix
        /// </summary>
        public void Initialize(string prefix = "")
        {
            if (_initialized)
            {
                Debug.LogWarning("[EnvROS] Already initialized, call Reinitialize() to change prefix");
                return;
            }
            
            _prefix = prefix?.Trim() ?? "";
            
            _initialized = true;
            
            if (logPublishEvents)
            {
                LogConfiguration();
            }
        }
        
        /// <summary>
        /// Reinitialize with a new prefix (clears existing registrations)
        /// </summary>
        public void Reinitialize(string newPrefix)
        {
            _initialized = false;
            Initialize(newPrefix);
        }
        
        /// <summary>
        /// Update the prefix for all topics (useful for multi-environment)
        /// </summary>
        public void UpdatePrefix(string newPrefix)
        {
            if (!_initialized)
            {
                Initialize(newPrefix);
                return;
            }
            
            _prefix = newPrefix?.Trim() ?? "";
            
            if (logPublishEvents)
            {
                Debug.Log($"[EnvROS] Prefix updated to: '{_prefix}'");
            }
        }
        
        /// <summary>
        /// The full name of a stream, in the one shape the whole project uses. The rule lives in
        /// <see cref="RobotSNAPTopics.Full"/>, together with the names themselves, so a prefix configured as
        /// `/env/` and a topic given as `/scan` still meet as `/env/scan`.
        /// </summary>
        private string BuildTopicName(string topic) => RobotSNAPTopics.Full(topic, _prefix);

        /// <summary>
        /// Creates the publisher of the whole application state on this object the first time the bridge
        /// starts. The component is created here rather than attached to the environment prefab so that it
        /// cannot outlive the EnvROS it reads its prefix from, and so that a scene built without ROS pays
        /// for it at all.
        /// </summary>
        private void EnsureSimulationStatePublisher()
        {
            if (!TryGetComponent(out SimulationStatePublisher publisher))
            {
                publisher = gameObject.AddComponent<SimulationStatePublisher>();
            }

            publisher.enabled = true;
        }

        /// <summary>
        /// Creates the component that listens for the commands coming back from the peer, on the same object
        /// and for the same reason as the state publisher: it has to be born and die with the EnvROS whose
        /// prefix it reads. The peer can be a ROS2 ros_tcp_endpoint or the pure-Python server of
        /// robotSNAP_ws/src/robotsnap/bridge, and neither side of this conversation needs to know which.
        /// </summary>
        private void EnsureSimulationControlBridge()
        {
            if (!TryGetComponent(out SimulationControlBridge bridge))
            {
                bridge = gameObject.AddComponent<SimulationControlBridge>();
            }

            bridge.enabled = true;
        }
        
        #endregion
        
        #region Publisher Registration
        
        /// <summary>
        /// Register a custom publisher for any message type
        /// </summary>
        public void RegisterPublisher<T>(string topic) where T : Message
        {
            EnsureInitialized();
            string fullTopic = BuildTopicName(topic);
            _ros.RegisterPublisher<T>(fullTopic);
            
            if (logPublishEvents)
            {
                Debug.Log($"[EnvROS] Registered publisher for topic: {fullTopic}");
            }
        }
        
        /// <summary>
        /// Register a custom subscriber
        /// </summary>
        public void RegisterSubscriber<T>(string topic, Action<T> callback) where T : Message
        {
            EnsureInitialized();
            string fullTopic = BuildTopicName(topic);
            _ros.Subscribe<T>(fullTopic, callback);
            
            if (logPublishEvents)
            {
                Debug.Log($"[EnvROS] Registered subscriber for topic: {fullTopic}");
            }
        }
        
        #endregion
        
        #region Publishing
        
        /// <summary>
        /// Publish a message to a topic
        /// </summary>
        public void Publish<T>(string topic, T message) where T : Message
        {
            if (!_initialized) return;
            string fullTopic = BuildTopicName(topic);

            // The connector throws when no publisher was registered for the topic, which is what a run
            // without ROS looks like. Skipping keeps the simulation usable instead of raising one
            // exception per publisher per frame.
            RosTopicState topicState = _ros.GetTopic(fullTopic);
            if (topicState == null || !topicState.IsPublisher)
                return;

            _ros.Publish(fullTopic, message);
            
            if (logPublishEvents)
            {
                Debug.Log($"[EnvROS] Published to {fullTopic}: {message}");
            }
        }
        
        #endregion
        
        #region Getters
        
        /// <summary>
        /// Get the full topic name with prefix
        /// </summary>
        public string GetTopicName(string baseTopic) => BuildTopicName(baseTopic);
        
        #endregion
        
        #region Private Helpers
        
        private void EnsureInitialized()
        {
            if (!_initialized)
            {
                Debug.LogWarning("[EnvROS] Not initialized, initializing with default settings");
                Initialize(_prefix);
            }
        }
        
        #endregion
        
        #region Editor Utilities
        
        [ContextMenu("Log Configuration")]
        private void LogConfiguration()
        {
            Debug.Log($"[EnvROS] Configuration:\n" +
                      $"  Prefix: '{_prefix}'\n" +
                      $"  Initialized: {_initialized}\n" +
                      $"  Publish simulation state: {publishSimulationState}\n" +
                      $"  Accept remote control: {acceptRemoteControl}");
        }
        
        #endregion
    }
}
