using System;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using RosMessageTypes.Std;
using RosMessageTypes.Geometry;
using NavMsgs = RosMessageTypes.Nav;
using SimMsgs = RosMessageTypes.Simulation;

namespace RobotSNAP.ROS
{
    /// <summary>
    /// Centralized ROS communication manager for the RobotSNAP environment.
    /// Handles publishers, subscribers, and service callbacks.
    /// </summary>
    public class EnvROS : MonoBehaviour
    {
        [Header("ROS Configuration")]
        [SerializeField] private string rosServiceName = "ros";
        [SerializeField] private string robotName = "robot";
        [SerializeField] private bool logPublishEvents = false;
        [SerializeField] private bool autoInitialize = true;
        
        [Header("Topic Names (will be prefixed)")]
        [SerializeField] private string resetDoneTopic = "/reset_done";
        [SerializeField] private string globalPathTopic = "/global_path";
        [SerializeField] private string localGoalFromRobotTopic = "/local_goal_from_robot";
        [SerializeField] private string localGoalFromMapTopic = "/local_goal_from_map";
        [SerializeField] private string mapTopic = "/map";
        
        [Header("Service Names (will be prefixed)")]
        [SerializeField] private string resetService = "/unity/reset";
        [SerializeField] private string playService = "/unity/play";
        
        // ROS Connection
        private ROSConnection _ros;
        private string _prefix = "";
        private bool _initialized;
        
        // Stored topic names with prefix
        private string _resetDoneTopicName;
        private string _globalPathTopicName;
        private string _localGoalFromRobotTopicName;
        private string _localGoalFromMapTopicName;
        private string _mapTopicName;
        private string _resetServiceName;
        private string _playServiceName;
        
        // Events for external systems
        public event Action<SimMsgs.ResetRequest> OnResetRequested;
        public event Action<bool> OnPlayStateChanged;
        
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
        }
        
        private void OnDestroy()
        {
            // Clean up event subscriptions
            OnResetRequested = null;
            OnPlayStateChanged = null;
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
            
            // Build topic names with prefix
            _resetDoneTopicName = BuildTopicName(resetDoneTopic);
            _globalPathTopicName = BuildTopicName(globalPathTopic);
            _localGoalFromRobotTopicName = BuildTopicName(localGoalFromRobotTopic);
            _localGoalFromMapTopicName = BuildTopicName(localGoalFromMapTopic);
            _mapTopicName = BuildTopicName(mapTopic);
            _resetServiceName = BuildTopicName(resetService);
            _playServiceName = BuildTopicName(playService);
            
            // Register publishers
            _ros.RegisterPublisher<BoolMsg>(_resetDoneTopicName);
            _ros.RegisterPublisher<NavMsgs.PathMsg>(_globalPathTopicName);
            _ros.RegisterPublisher<PointMsg>(_localGoalFromRobotTopicName);
            _ros.RegisterPublisher<PointMsg>(_localGoalFromMapTopicName);
            _ros.RegisterPublisher<NavMsgs.OccupancyGridMsg>(_mapTopicName);
            
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
            
            _resetDoneTopicName = BuildTopicName(resetDoneTopic);
            _globalPathTopicName = BuildTopicName(globalPathTopic);
            _localGoalFromRobotTopicName = BuildTopicName(localGoalFromRobotTopic);
            _localGoalFromMapTopicName = BuildTopicName(localGoalFromMapTopic);
            _mapTopicName = BuildTopicName(mapTopic);
            _resetServiceName = BuildTopicName(resetService);
            _playServiceName = BuildTopicName(playService);
            
            if (logPublishEvents)
            {
                Debug.Log($"[EnvROS] Prefix updated to: '{_prefix}'");
            }
        }
        
        private string BuildTopicName(string topic)
        {
            topic = topic.TrimStart('/');
            return string.IsNullOrEmpty(_prefix) ? $"/{topic}" : $"/{_prefix}/{topic}";
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
            _ros.Publish(fullTopic, message);
            
            if (logPublishEvents)
            {
                Debug.Log($"[EnvROS] Published to {fullTopic}: {message}");
            }
        }
        
        /// <summary>
        /// Alias for Publish (maintains compatibility)
        /// </summary>
        public void Send<T>(string topic, T message) where T : Message
        {
            Publish(topic, message);
        }
        
        #endregion
        
        #region Convenience Publish Methods
        
        /// <summary>
        /// Publish reset done status
        /// </summary>
        public void PublishResetDone(bool success)
        {
            if (!_initialized) return;
            _ros.Publish(_resetDoneTopicName, new BoolMsg(success));
            
            if (logPublishEvents)
            {
                Debug.Log($"[EnvROS] Published reset done: {success}");
            }
        }
        
        /// <summary>
        /// Publish global path
        /// </summary>
        public void PublishGlobalPath(NavMsgs.PathMsg path)
        {
            if (!_initialized) return;
            _ros.Publish(_globalPathTopicName, path);
        }
        
        /// <summary>
        /// Publish local goal from robot frame
        /// </summary>
        public void PublishLocalGoalFromRobot(PointMsg goal)
        {
            if (!_initialized) return;
            _ros.Publish(_localGoalFromRobotTopicName, goal);
        }
        
        /// <summary>
        /// Publish local goal from map frame
        /// </summary>
        public void PublishLocalGoalFromMap(PointMsg goal)
        {
            if (!_initialized) return;
            _ros.Publish(_localGoalFromMapTopicName, goal);
        }
        
        /// <summary>
        /// Publish occupancy grid map
        /// </summary>
        public void PublishMap(NavMsgs.OccupancyGridMsg map)
        {
            if (!_initialized) return;
            _ros.Publish(_mapTopicName, map);
        }
        
        #endregion
        
        #region Services
        
        /// <summary>
        /// Register the reset service callback
        /// </summary>
        public void RegisterResetService(Func<SimMsgs.ResetRequest, SimMsgs.ResetResponse> callback)
        {
            EnsureInitialized();
            _ros.ImplementService<SimMsgs.ResetRequest, SimMsgs.ResetResponse>(_resetServiceName, (req) =>
            {
                OnResetRequested?.Invoke(req);
                return callback(req);
            });
            
            if (logPublishEvents)
            {
                Debug.Log($"[EnvROS] Registered reset service on: {_resetServiceName}");
            }
        }
        
        /// <summary>
        /// Register the pause/play service callback
        /// </summary>
        public void RegisterPausePlayService(Func<SimMsgs.PausePlayRequest, SimMsgs.PausePlayResponse> callback)
        {
            EnsureInitialized();
            _ros.ImplementService<SimMsgs.PausePlayRequest, SimMsgs.PausePlayResponse>(_playServiceName, (req) =>
            {
                OnPlayStateChanged?.Invoke(req.play);
                return callback(req);
            });
            
            if (logPublishEvents)
            {
                Debug.Log($"[EnvROS] Registered pause/play service on: {_playServiceName}");
            }
        }
        
        /// <summary>
        /// Register a generic service
        /// </summary>
        public void RegisterService<TRequest, TResponse>(string serviceName, Func<TRequest, TResponse> callback)
            where TRequest : Message
            where TResponse : Message
        {
            EnsureInitialized();
            string fullServiceName = BuildTopicName(serviceName);
            _ros.ImplementService<TRequest, TResponse>(fullServiceName, callback);
            
            if (logPublishEvents)
            {
                Debug.Log($"[EnvROS] Registered service on: {fullServiceName}");
            }
        }
        
        #endregion
        
        #region Getters
        
        public string GetResetServiceName() => _resetServiceName;
        public string GetPlayServiceName() => _playServiceName;
        public string GetResetDoneTopicName() => _resetDoneTopicName;
        public string GetGlobalPathTopicName() => _globalPathTopicName;
        public string GetLocalGoalFromRobotTopicName() => _localGoalFromRobotTopicName;
        public string GetLocalGoalFromMapTopicName() => _localGoalFromMapTopicName;
        public string GetMapTopicName() => _mapTopicName;
        
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
                      $"  Reset Service: {_resetServiceName}\n" +
                      $"  Play Service: {_playServiceName}\n" +
                      $"  Reset Done Topic: {_resetDoneTopicName}\n" +
                      $"  Global Path Topic: {_globalPathTopicName}\n" +
                      $"  Local Goal (Robot) Topic: {_localGoalFromRobotTopicName}\n" +
                      $"  Local Goal (Map) Topic: {_localGoalFromMapTopicName}\n" +
                      $"  Map Topic: {_mapTopicName}\n" +
                      $"  Initialized: {_initialized}");
        }
        
        [ContextMenu("Test Publish Reset Done")]
        private void TestPublishResetDone()
        {
            PublishResetDone(true);
        }
        
        #endregion
    }
}