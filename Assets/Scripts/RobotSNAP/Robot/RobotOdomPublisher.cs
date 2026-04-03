using UnityEngine;
using RobotSNAP.ROS;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;

namespace RobotSNAP
{
    /// <summary>
    /// Publishes robot odometry to ROS.
    /// Separated from Robot core logic.
    /// </summary>
    [RequireComponent(typeof(Robot))]
    public class RobotOdomPublisher : MonoBehaviour
    {
        [Header("ROS Configuration")]
        [SerializeField] private bool autoDetectPrefix = true;
        [SerializeField] private string customPrefix = "";
        [SerializeField] private string odomTopic = "/odom";
        [SerializeField] private float publishFrequencyHz = 30f;
        
        [Header("Frame IDs")]
        [SerializeField] private string frameId = "odom";
        [SerializeField] private string childFrameId = "base_link";
        
        [Header("Covariance")]
        [SerializeField] private bool publishCovariance = true;
        
        private EnvROS _envROS;
        private Robot _robot;
        private string _fullTopicName;
        private float _publishInterval;
        private float _lastPublishTime;
        private RosMessageTypes.Nav.OdometryMsg _message;
        
        private void Start()
        {
            Initialize();
        }
        
        public void Initialize()
        {
            _robot = GetComponent<Robot>();
            if (_robot == null)
            {
                Debug.LogError($"[{name}] Robot component not found!");
                enabled = false;
                return;
            }
            
            _envROS = FindObjectOfType<EnvROS>();
            if (_envROS == null)
            {
                Debug.LogWarning($"[{name}] EnvROS not found, odometry will not be published");
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
            
            _fullTopicName = string.IsNullOrEmpty(prefix) ? odomTopic : $"/{prefix}{odomTopic}";
            
            // Register publisher
            _envROS.RegisterPublisher<RosMessageTypes.Nav.OdometryMsg>(_fullTopicName);
            
            // Initialize message
            InitializeMessage(prefix);
            
            _publishInterval = 1f / publishFrequencyHz;
            
            Debug.Log($"[{name}] Publishing odometry to {_fullTopicName} at {publishFrequencyHz} Hz");
        }
        
        private void InitializeMessage(string prefix)
        {
            string fullFrameId = string.IsNullOrEmpty(prefix) ? frameId : $"/{prefix}{frameId}";
            string fullChildFrameId = string.IsNullOrEmpty(prefix) ? childFrameId : $"/{prefix}{childFrameId}";
            
            _message = new RosMessageTypes.Nav.OdometryMsg
            {
                header = new RosMessageTypes.Std.HeaderMsg { frame_id = fullFrameId },
                child_frame_id = fullChildFrameId,
                pose = new RosMessageTypes.Geometry.PoseWithCovarianceMsg(),
                twist = new RosMessageTypes.Geometry.TwistWithCovarianceMsg()
            };
            
            if (publishCovariance)
            {
                var identity = new double[36];
                identity[0] = identity[7] = identity[14] = 0.01;
                identity[21] = identity[28] = identity[35] = 0.01;
                _message.pose.covariance = identity;
                _message.twist.covariance = identity;
            }
        }
        
        private void FixedUpdate()
        {
            if (_envROS == null || !_envROS.IsInitialized) return;
            
            if (Time.time >= _lastPublishTime + _publishInterval)
            {
                PublishOdometry();
                _lastPublishTime = Time.time;
            }
        }
        
        private void PublishOdometry()
        {
            if (_robot == null) return;
            
            // Update header timestamp
            ROSTimeUtils.UpdateHeader(_message.header);
            
            // Position and orientation
            _message.pose.pose.position = Util.Geometry.GetGeometryPoint(_robot.Position.To<FLU>());
            _message.pose.pose.orientation = Util.Geometry.GetGeometryQuaternion(_robot.Rotation.To<FLU>());
            
            // Velocity
            _message.twist.twist.linear = Util.Geometry.GetGeometryVector3(new Vector3(_robot.CurrentLinearSpeed, 0, 0).To<FLU>());
            _message.twist.twist.angular = Util.Geometry.GetGeometryVector3(new Vector3(0, -_robot.CurrentAngularSpeed, 0).To<FLU>());
            
            // Publish
            _envROS.Publish(_fullTopicName, _message);
        }
    }
}