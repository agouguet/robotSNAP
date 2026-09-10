using UnityEngine;
using RobotSNAP.ROS;

namespace RobotSNAP
{
    [RequireComponent(typeof(LaserScanner))]
    public class LaserScanPublisher : MonoBehaviour
    {
        [Header("ROS Configuration")]
        [SerializeField] private bool autoDetectPrefix = true;
        [SerializeField] private string customPrefix = "";
        [SerializeField] private string laserTopic = "/scan";
        [SerializeField] private float publishFrequencyHz = 10f;
        
        [Header("Frame ID")]
        [SerializeField] private string frameId = "laser";
        
        [Header("Debug")]
        [SerializeField] private bool logPublishEvents = false;
        
        [SerializeField] private EnvROS _envROS;
        private LaserScanner _laserScanner;
        private string _fullTopicName;
        private float _publishInterval;
        private float _previousPublishTime;
        private RosMessageTypes.Sensor.LaserScanMsg _message;
        
        private void Start()
        {
            Initialize();
        }
        
        /// <summary>
        /// Initialize the laser scan publisher
        /// </summary>
        public void Initialize()
        {
            // Find EnvROS
            _envROS ??= FindFirstObjectByType<EnvROS>();
            
            if (_envROS == null)
            {
                Debug.LogError($"[{name}] EnvROS not found in scene! LaserScanPublisher will be disabled.");
                enabled = false;
                return;
            }
            
            // Detect prefix
            string prefix = "";
            if (autoDetectPrefix && _envROS != null)
            {
                prefix = _envROS.Prefix;
            }
            else if (!string.IsNullOrEmpty(customPrefix))
            {
                prefix = customPrefix;
            }
            
            // Build full topic name
            _fullTopicName = string.IsNullOrEmpty(prefix) ? laserTopic : $"/{prefix}{laserTopic}";
            
            // Get laser scanner component
            _laserScanner = GetComponent<LaserScanner>();
            if (_laserScanner == null)
            {
                Debug.LogError($"[{name}] LaserScanner component not found!");
                enabled = false;
                return;
            }
            
            // Register publisher
            _envROS.RegisterPublisher<RosMessageTypes.Sensor.LaserScanMsg>(_fullTopicName);
            
            // Initialize message
            InitializeMessage(prefix);
            
            _publishInterval = 1f / publishFrequencyHz;
            _previousPublishTime = Time.realtimeSinceStartup;
            
            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Publishing laser scans to {_fullTopicName} at {publishFrequencyHz} Hz");
            }
        }
        
        /// <summary>
        /// Initialize the laser scan message
        /// </summary>
        private void InitializeMessage(string prefix)
        {
            string fullFrameId = string.IsNullOrEmpty(prefix) ? frameId : $"/{prefix}{frameId}";
            _message = _laserScanner.InitializeMessage(fullFrameId);
            
            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Laser scan message initialized with frame_id: {fullFrameId}");
            }
        }
        
        private void FixedUpdate()
        {
            if (_envROS == null || !_envROS.IsInitialized)
                return;
            
            // Throttle publishing to desired frequency
            if (Time.realtimeSinceStartup >= _previousPublishTime + _publishInterval)
            {
                PublishScan();
                _previousPublishTime = Time.realtimeSinceStartup;
            }
        }
        
        /// <summary>
        /// Perform scan and publish the message
        /// </summary>
        private void PublishScan()
        {
            if (_laserScanner == null || _message == null)
                return;
            
            // Update header timestamp
            ROSTimeUtils.UpdateHeader(_message.header);
            
            // Perform scan and update ranges
            _message.ranges = _laserScanner.Scan();
            
            // Publish
            _envROS.Publish(_fullTopicName, _message);
            
            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Published laser scan with {_message.ranges.Length} rays");
            }
        }
        
        /// <summary>
        /// Force an immediate scan and publish (useful for debugging)
        /// </summary>
        [ContextMenu("Force Publish")]
        public void ForcePublish()
        {
            if (_envROS != null && _envROS.IsInitialized)
            {
                PublishScan();
                Debug.Log($"[{name}] Force published laser scan");
            }
            else
            {
                Debug.LogWarning($"[{name}] Cannot force publish: EnvROS not initialized");
            }
        }
        
        /// <summary>
        /// Reinitialize with a new prefix (useful for multi-environment)
        /// </summary>
        public void Reinitialize(string newPrefix)
        {
            if (_envROS != null)
            {
                // Build new topic name with new prefix
                _fullTopicName = string.IsNullOrEmpty(newPrefix) ? laserTopic : $"/{newPrefix}{laserTopic}";
                
                // Re-initialize message with new frame ID
                InitializeMessage(newPrefix);
                
                if (logPublishEvents)
                {
                    Debug.Log($"[{name}] Reinitialized with prefix: '{newPrefix}', topic: {_fullTopicName}");
                }
            }
        }
        
        private void OnDestroy()
        {
            // Clean up
            _message = null;
            
            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Destroyed");
            }
        }
        
        #region Public Properties
        
        /// <summary>
        /// Get the full topic name being published to
        /// </summary>
        public string TopicName => _fullTopicName;
        
        /// <summary>
        /// Get the publish frequency
        /// </summary>
        public float PublishFrequency => publishFrequencyHz;
        
        /// <summary>
        /// Get the laser scanner component
        /// </summary>
        public LaserScanner LaserScanner => _laserScanner;
        
        #endregion
    }
}
