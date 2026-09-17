using System.Collections.Generic;
using UnityEngine;
using RobotSNAP.ROS;
using RobotSNAP.Agents;

namespace RobotSNAP
{
    [RequireComponent(typeof(LaserScanner))]
    public class LaserScanPublisher : MonoBehaviour
    {
        [Header("ROS Configuration")]
        [SerializeField] private bool autoDetectPrefix = true;
        [SerializeField] private string customPrefix = "";
        [SerializeField] private string laserTopic = RobotSNAPTopics.Scan;
        [SerializeField] private float publishFrequencyHz = 10f;
        
        [Header("Frame ID")]
        [SerializeField] private string frameId = "laser";
        
        [Header("Debug")]
        [SerializeField] private bool logPublishEvents = false;
        
        [SerializeField] private EnvROS _envROS;
        private LaserScanner _laserScanner;
        /// <summary>
        /// Every name this scan answers on: the id of this robot, plus the legacy name when it is the first
        /// robot of the scenario. One robot has one name, so a crowd of robots costs no extra publish.
        /// </summary>
        private readonly List<string> _fullTopicNames = new List<string>(2);
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
            _envROS ??= FindAnyObjectByType<EnvROS>();
            
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
            
            // The names are built by the one topic table of the project and by the identity of the robot this
            // sensor belongs to: a second robot publishes under its own id, the first keeps the name the
            // project has always published.
            _fullTopicNames.Clear();
            _fullTopicNames.AddRange(RobotIdentity.StreamNamesFor(this, laserTopic, prefix));
            
            // Get laser scanner component
            _laserScanner = GetComponent<LaserScanner>();
            if (_laserScanner == null)
            {
                Debug.LogError($"[{name}] LaserScanner component not found!");
                enabled = false;
                return;
            }
            
            // Register publisher
            foreach (string topic in _fullTopicNames)
                _envROS.RegisterPublisher<RosMessageTypes.Sensor.LaserScanMsg>(topic);
            
            // Initialize message
            InitializeMessage(prefix);
            
            _publishInterval = 1f / publishFrequencyHz;
            _previousPublishTime = Time.realtimeSinceStartup;
            
            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Publishing laser scans to {string.Join(", ", _fullTopicNames)} at {publishFrequencyHz} Hz");
            }
        }
        
        /// <summary>
        /// Initialize the laser scan message
        /// </summary>
        private void InitializeMessage(string prefix)
        {
            string fullFrameId = RobotIdentity.FrameIdFor(this, frameId, prefix);
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
            
            // Publish, on every name this robot answers on.
            foreach (string topic in _fullTopicNames)
                _envROS.Publish(topic, _message);
            
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
                _fullTopicNames.Clear();
                _fullTopicNames.AddRange(RobotIdentity.StreamNamesFor(this, laserTopic, newPrefix));
                
                // Re-initialize message with new frame ID
                InitializeMessage(newPrefix);
                
                if (logPublishEvents)
                {
                    Debug.Log($"[{name}] Reinitialized with prefix: '{newPrefix}', topics: {string.Join(", ", _fullTopicNames)}");
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
        /// The first name being published to. A robot answers on one stream, the first robot of a scenario
        /// on two, so this is the one a client that names no robot reads.
        /// </summary>
        public string TopicName => _fullTopicNames.Count > 0 ? _fullTopicNames[0] : null;

        /// <summary>Every name being published to, the legacy one first when there is one.</summary>
        public IReadOnlyList<string> TopicNames => _fullTopicNames;

        /// <summary>
        /// Sets the publishing rate. The profile of a robot type carries it - a TurtleBot publishes its scan
        /// at 10 Hz, a Jackal at 20 - and applying a type has to reach the rate the publisher already
        /// computed its interval from.
        /// </summary>
        public void SetPublishFrequency(float hertz)
        {
            publishFrequencyHz = Mathf.Max(1f, hertz);
            _publishInterval = 1f / publishFrequencyHz;
        }
        
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
