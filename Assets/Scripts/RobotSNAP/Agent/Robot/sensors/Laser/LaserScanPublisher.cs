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
        [Tooltip("Scan rate in scans per simulated second: raising the session's time scale raises the wall rate with it, so the sweep keeps describing the same distance of travel.")]
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
        // The throttle reads the simulation clock (Time.fixedTime, which advances once per physics step), not
        // the wall: the rate above is a rate of the simulation. FixedUpdate runs timeScale times more often per
        // second of wall time, so a 10 Hz scan stays ten scans per simulated second however fast the session
        // runs. Pacing on Time.realtimeSinceStartup instead made the sweep sparser the faster the world went -
        // at a scale of five the robot travelled five times further between two scans.
        //
        // It is published from the fixed step rather than the frame because the frame rate is what a raised
        // time scale squeezes first: a 60 fps session at five times speed asks for fifty scans a second of
        // wall time, and a stream published once per frame cannot answer more than sixty however fast the
        // world is going. The physics loop runs several times per frame and keeps the rate.
        private float _publishInterval;
        private float _nextPublishTime;
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
            string baseTopic = RobotSNAPTopics.Resolve(RosTopicSlot.Scan, laserTopic);
            _fullTopicNames.AddRange(RobotIdentity.StreamNamesFor(this, baseTopic, prefix));
            
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
            
            float rate = RobotSNAPTopics.ResolveFrequency(publishFrequencyHz);
            _publishInterval = 1f / rate;
            _nextPublishTime = NextSlot(Time.fixedTime, _publishInterval);
            
            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Publishing laser scans to {string.Join(", ", _fullTopicNames)} at {rate} Hz");
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

            // Throttle publishing to the desired frequency, counted in simulated seconds.
            if (Time.fixedTime < _nextPublishTime)
                return;

            _nextPublishTime = NextSlot(Time.fixedTime, _publishInterval);
            PublishScan();
        }

        /// <summary>
        /// The first instant of the shared simulated grid that comes after <paramref name="now"/>: the next
        /// multiple of <paramref name="interval"/> counted from zero.
        ///
        /// The odometry of this robot is paced on that same grid, so a scan and the pose of the instant it
        /// measured come out of one physics step and carry one stamp. Paced apart - each stream starting its
        /// own interval when its component happened to wake up - they landed up to a whole period off, and a
        /// client had no pose to place a scan with that was not the pose of a moment the robot had already
        /// left: the lidar cloud turned with the robot instead of staying on the walls.
        /// </summary>
        private static float NextSlot(float now, float interval)
        {
            return interval <= 0f
                ? float.PositiveInfinity
                : (Mathf.Floor(now / interval) + 1f) * interval;
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
            
            // Perform scan and update ranges, in the ROS frame the message is published in.
            _message.ranges = _laserScanner.ScanInRosFrame();
            
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
                string baseTopic = RobotSNAPTopics.Resolve(RosTopicSlot.Scan, laserTopic);
                _fullTopicNames.AddRange(RobotIdentity.StreamNamesFor(this, baseTopic, newPrefix));
                
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
