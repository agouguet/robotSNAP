using UnityEngine;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RobotSNAP.Core;

namespace RobotSNAP.ROS
{
    /// <summary>
    /// Publie l'odométrie du robot sur un topic ROS
    /// </summary>
    public class OdometryPublisher : MonoBehaviour
    {
        [Header("ROS Configuration")]
        [SerializeField] private bool autoDetectPrefix = true;
        [SerializeField] private string customPrefix = "";
        [SerializeField] private string topicName = "/odom";
        [SerializeField] private float publishFrequencyHz = 10f;
        
        [Header("References")]
        [SerializeField] private Rigidbody robotRigidbody;
        [SerializeField] private Transform robotTransform;
        
        [Header("Frame IDs")]
        [SerializeField] private string frameId = "odom";
        [SerializeField] private string childFrameId = "base_link";
        
        [Header("Covariance")]
        [SerializeField] private bool publishCovariance = true;
        [SerializeField] private float positionCovariance = 0.01f;
        [SerializeField] private float orientationCovariance = 0.01f;
        [SerializeField] private float linearVelocityCovariance = 0.01f;
        [SerializeField] private float angularVelocityCovariance = 0.01f;
        
        private EnvROS _envROS;
        private string _fullTopicName;
        private float _publishInterval;
        private RosMessageTypes.Nav.OdometryMsg _message;
        private double[] _poseCovariance;
        private double[] _twistCovariance;
        
        private void Start()
        {
            // Find EnvROS
            _envROS = FindObjectOfType<EnvROS>();
            
            if (_envROS == null)
            {
                Debug.LogError($"[{name}] EnvROS not found in scene!");
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
            _fullTopicName = string.IsNullOrEmpty(prefix) ? topicName : $"/{prefix}{topicName}";
            
            // Register publisher
            _envROS.RegisterPublisher<RosMessageTypes.Nav.OdometryMsg>(_fullTopicName);
            
            // Get references
            if (robotRigidbody == null)
            {
                robotRigidbody = GetComponent<Rigidbody>();
                if (robotRigidbody == null)
                {
                    Debug.LogWarning($"[{name}] No Rigidbody found, velocity will be zero");
                }
            }
            
            if (robotTransform == null)
            {
                robotTransform = transform;
            }
            
            // Initialize covariance matrices
            InitializeCovariance();
            
            // Initialize message
            InitializeMessage();
            
            _publishInterval = 1f / publishFrequencyHz;
            
            // Start publishing
            InvokeRepeating(nameof(PublishOdometry), 1f, _publishInterval);
            
            Debug.Log($"[{name}] Publishing odometry to {_fullTopicName} at {publishFrequencyHz} Hz");
        }
        
        private void InitializeCovariance()
        {
            // Covariance pour la pose (6x6 = 36 éléments)
            _poseCovariance = new double[36];
            // Covariance pour le twist (6x6 = 36 éléments)
            _twistCovariance = new double[36];
            
            if (publishCovariance)
            {
                // Position covariance (x, y, z) - indices 0, 7, 14
                _poseCovariance[0] = positionCovariance;
                _poseCovariance[7] = positionCovariance;
                _poseCovariance[14] = positionCovariance;
                
                // Orientation covariance (roll, pitch, yaw) - indices 21, 28, 35
                _poseCovariance[21] = orientationCovariance;
                _poseCovariance[28] = orientationCovariance;
                _poseCovariance[35] = orientationCovariance;
                
                // Linear velocity covariance (x, y, z) - indices 0, 7, 14
                _twistCovariance[0] = linearVelocityCovariance;
                _twistCovariance[7] = linearVelocityCovariance;
                _twistCovariance[14] = linearVelocityCovariance;
                
                // Angular velocity covariance (roll, pitch, yaw) - indices 21, 28, 35
                _twistCovariance[21] = angularVelocityCovariance;
                _twistCovariance[28] = angularVelocityCovariance;
                _twistCovariance[35] = angularVelocityCovariance;
            }
        }
        
        private void InitializeMessage()
        {
            _message = new RosMessageTypes.Nav.OdometryMsg
            {
                header = new RosMessageTypes.Std.HeaderMsg(),
                pose = new RosMessageTypes.Geometry.PoseWithCovarianceMsg(),
                twist = new RosMessageTypes.Geometry.TwistWithCovarianceMsg(),
                child_frame_id = childFrameId.TrimStart('/')
            };
            
            if (publishCovariance)
            {
                _message.pose.covariance = _poseCovariance;
                _message.twist.covariance = _twistCovariance;
            }
        }
        
        private void PublishOdometry()
        {
            if (robotTransform == null || !robotTransform.gameObject.activeSelf)
                return;
            
            // Update header timestamp
            ROSTimeUtils.UpdateHeader(_message.header);
            _message.header.frame_id = string.IsNullOrEmpty(_envROS.Prefix) ? frameId : $"/{_envROS.Prefix}{frameId}";
            
            // Position and orientation
            Vector3 position = robotTransform.localPosition;
            Quaternion rotation = robotTransform.rotation;
            
            _message.pose.pose.position = Util.Geometry.GetGeometryPoint(position.To<FLU>());
            _message.pose.pose.orientation = Util.Geometry.GetGeometryQuaternion(rotation.To<FLU>());
            
            // Velocity (if Rigidbody is available)
            if (robotRigidbody != null)
            {
                // Linear velocity in local frame
                Vector3 linearVelocityWorld = robotRigidbody.linearVelocity;
                Vector3 linearVelocityLocal = robotTransform.InverseTransformDirection(linearVelocityWorld);
                _message.twist.twist.linear = Util.Geometry.GetGeometryVector3(linearVelocityLocal.To<FLU>());
                
                // Angular velocity in local frame
                Vector3 angularVelocityWorld = robotRigidbody.angularVelocity;
                Vector3 angularVelocityLocal = robotTransform.InverseTransformDirection(angularVelocityWorld);
                _message.twist.twist.angular = Util.Geometry.GetGeometryVector3(angularVelocityLocal.To<FLU>());
            }
            else
            {
                // Zero velocities if no Rigidbody
                _message.twist.twist.linear = Util.Geometry.GetGeometryVector3(Vector3.zero);
                _message.twist.twist.angular = Util.Geometry.GetGeometryVector3(Vector3.zero);
            }
            
            // Publish
            _envROS.Publish(_fullTopicName, _message);
        }
        
        private void OnDestroy()
        {
            CancelInvoke(nameof(PublishOdometry));
        }
        
        [ContextMenu("Test Publish")]
        private void TestPublish()
        {
            PublishOdometry();
            Debug.Log($"[{name}] Test odometry sent to {_fullTopicName}");
        }
    }
}