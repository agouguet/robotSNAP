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
        [SerializeField] private string topicName = "clock";
        
        [Header("Publishing")]
        [SerializeField] private bool publishInFixedUpdate = true;
        [SerializeField] private bool publishOnlyOnChange = true;
        
        [Header("Debug")]
        [SerializeField] private bool logPublishEvents = false;
        
        private EnvROS _envROS;
        private Clock _clock;
        private string _fullTopicName;
        private double _lastPublishedTime;
        
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
            if (!publishInFixedUpdate && _envROS != null && _envROS.IsInitialized)
            {
                Publish();
            }
        }
        
        #endregion
        
        #region Initialization
        
        public void Initialize()
        {
            // Find EnvROS
            _envROS = FindObjectOfType<EnvROS>();
            
            if (_envROS == null)
            {
                Debug.LogWarning($"[{name}] EnvROS not found, clock will not be published");
                enabled = false;
                return;
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
            
            // Build topic name
            _fullTopicName = string.IsNullOrEmpty(prefix) ? topicName : $"/{prefix}{topicName}";
            
            // Register publisher
            _envROS.RegisterPublisher<ClockMsg>(_fullTopicName);
            
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