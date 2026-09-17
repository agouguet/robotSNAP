using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Rosgraph;
using RosMessageTypes.BuiltinInterfaces;
using RobotSNAP.Core;
using System;

namespace RobotSNAP.ROS
{
    /// <summary>
    /// ROS bridge for Clock - publishes clock time to ROS topic.
    /// This is the only ROS-dependent component for time synchronization.
    /// </summary>
    public class ROSClockPublisher : MonoBehaviour
    {
        [Header("ROS Configuration")]
        [SerializeField] private bool autoDetectPrefix = true;
        [SerializeField] private string customPrefix = "";
        [SerializeField] private string topicName = RobotSNAPTopics.Clock;
        
        [Header("Publishing")]
        [SerializeField] private bool publishInFixedUpdate = true;
        [SerializeField] private bool publishOnlyOnChange = true;
        
        [Header("Debug")]
        [SerializeField] private bool logPublishEvents = false;
        
        [SerializeField] private EnvROS _envROS;
        private Clock _clock;
        private string _fullTopicName;
        private double _lastPublishedTime;
        private bool _registered;
        private float _nextResolveTime;

        //: How often a clock that found no environment looks for one, in seconds.
        private const float ResolveInterval = 0.5f;
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            // Ensure Clock exists
            if (Clock.Instance == null)
            {
                var clockGO = new GameObject("Clock");
                clockGO.AddComponent<Clock>();
            }
            
            _clock = Clock.Instance;
        }
        
        private void Start()
        {
            Initialize();
        }
        
        private void FixedUpdate()
        {
            if (publishInFixedUpdate && _envROS != null && _envROS.IsInitialized)
            {
                Publish();
            }
        }
        
        private void Update()
        {
            // The environment is built by the scenario load, which runs after this scene object has started:
            // giving up here used to disable the component for the whole session, and the documented /clock
            // topic never carried anything. Looking again until the bridge exists costs one lookup every
            // half second, and stops for good once it is found.
            if (!_registered && Time.unscaledTime >= _nextResolveTime)
            {
                _nextResolveTime = Time.unscaledTime + ResolveInterval;
                Initialize();
            }

            if (!publishInFixedUpdate && _registered && _envROS != null && _envROS.IsInitialized)
            {
                Publish();
            }
        }
        
        #endregion
        
        #region Initialization
        
        public void Initialize()
        {
            if (_registered) return;

            // Find EnvROS. A destroyed one compares equal to null, so this picks up the environment a scenario
            // load has just rebuilt.
            _envROS ??= FindAnyObjectByType<EnvROS>();
            
            if (_envROS == null)
            {
                return;  // Update looks again; the environment may not be built yet.
            }
            
            // Get prefix
            string prefix = "";
            if (autoDetectPrefix && _envROS != null)
            {
                prefix = _envROS.Prefix;
            }
            else if (!string.IsNullOrEmpty(customPrefix))
            {
                prefix = customPrefix;
            }
            
            // Build topic name with the one rule the whole project joins names with: with a prefix, `clock`
            // used to come out as `/myenvclock` instead of `/myenv/clock`.
            _fullTopicName = RobotSNAPTopics.Full(topicName, prefix);
            
            // Register publisher
            _envROS.RegisterPublisher<ClockMsg>(_fullTopicName);
            _registered = true;
            
            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Publishing clock to {_fullTopicName}");
            }
        }
        
        #endregion
        
        #region Publishing
        
        /// <summary>
        /// Publish the current time to ROS
        /// </summary>
        public void Publish()
        {
            if (_envROS == null || !_envROS.IsInitialized || _clock == null)
                return;
            
            double currentTime = _clock.CurrentTimeMillis;
            
            // Skip if time hasn't changed
            if (publishOnlyOnChange && Math.Abs(currentTime - _lastPublishedTime) < 0.001)
                return;
            
            _lastPublishedTime = currentTime;
            
            // Create and publish message using ROSTimeUtils
            var timeMsg = ROSTimeUtils.MillisecondsToTimeMsg(currentTime);
            var clockMsg = new ClockMsg
            {
                clock = new TimeMsg
                {
                    sec = timeMsg.sec,
                    nanosec = timeMsg.nanosec
                }
            };
            
            _envROS.Publish(_fullTopicName, clockMsg);
            
            if (logPublishEvents && Time.frameCount % 60 == 0)
            {
                Debug.Log($"[{name}] Published clock: {timeMsg.sec}.{timeMsg.nanosec:D9}s");
            }
        }
        
        /// <summary>
        /// Force an immediate publish regardless of time change
        /// </summary>
        public void ForcePublish()
        {
            _lastPublishedTime = -1; // Reset to force publish
            Publish();
        }
        
        #endregion
        
        #region Editor Utilities
        
        [ContextMenu("Force Publish")]
        private void EditorForcePublish()
        {
            ForcePublish();
            Debug.Log($"[{name}] Force published clock");
        }
        
        [ContextMenu("Log Configuration")]
        private void EditorLogConfiguration()
        {
            Debug.Log($"[{name}] Configuration:\n" +
                      $"  Topic: {_fullTopicName}\n" +
                      $"  Auto Prefix: {autoDetectPrefix}\n" +
                      $"  Publish in FixedUpdate: {publishInFixedUpdate}\n" +
                      $"  Publish Only on Change: {publishOnlyOnChange}\n" +
                      $"  EnvROS: {(_envROS != null ? "OK" : "Missing")}\n" +
                      $"  Clock: {(_clock != null ? "OK" : "Missing")}");
        }
        
        #endregion
    }
}
