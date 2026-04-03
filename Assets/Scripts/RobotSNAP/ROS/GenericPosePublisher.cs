using UnityEngine;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.Geometry;
using RosMessageTypes.Std;

namespace RobotSNAP.ROS
{
    /// <summary>
    /// Publie la pose d'un transform sur un topic ROS
    /// </summary>
    public class GenericPosePublisher : MonoBehaviour
    {
        [Header("ROS Configuration")]
        [SerializeField] private bool autoDetectPrefix = true;
        [SerializeField] private string customPrefix = "";
        [SerializeField] private string topicName = "/pose_topic";
        [SerializeField] private float publishFrequencyHz = 10f;
        
        [Header("Message Type")]
        [SerializeField] private MessageType messageType = MessageType.Pose;
        
        [Header("Transform Source")]
        [SerializeField] private Transform sourceTransform;
        [SerializeField] private bool useLocalPosition = false;
        
        [Header("Frame ID")]
        [SerializeField] private string frameId = "/map";
        
        public enum MessageType
        {
            Point,
            Pose,
            PoseStamped,
            PoseWithCovarianceStamped
        }
        
        private EnvROS _envROS;
        private string _fullTopicName;
        private float _publishInterval;
        private float _timeElapsed;
        
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
            
            // Register publisher based on message type
            switch (messageType)
            {
                case MessageType.Point:
                    _envROS.RegisterPublisher<PointMsg>(_fullTopicName);
                    break;
                case MessageType.Pose:
                    _envROS.RegisterPublisher<PoseMsg>(_fullTopicName);
                    break;
                case MessageType.PoseStamped:
                    _envROS.RegisterPublisher<PoseStampedMsg>(_fullTopicName);
                    break;
                case MessageType.PoseWithCovarianceStamped:
                    _envROS.RegisterPublisher<PoseWithCovarianceStampedMsg>(_fullTopicName);
                    break;
            }
            
            // Use source transform or this transform
            if (sourceTransform == null)
            {
                sourceTransform = transform;
            }
            
            _publishInterval = 1f / publishFrequencyHz;
            
            // Start publishing
            InvokeRepeating(nameof(PublishMessage), 1f, _publishInterval);
            
            Debug.Log($"[{name}] Publishing {messageType} to {_fullTopicName} at {publishFrequencyHz} Hz");
        }
        
        private void PublishMessage()
        {
            if (sourceTransform == null || !sourceTransform.gameObject.activeSelf)
                return;
            
            Vector3 position = useLocalPosition ? sourceTransform.localPosition : sourceTransform.position;
            Quaternion rotation = useLocalPosition ? sourceTransform.localRotation : sourceTransform.rotation;
            
            switch (messageType)
            {
                case MessageType.Point:
                    var pointMsg = Util.Geometry.GetGeometryPoint(position.To<FLU>());
                    _envROS.Publish(_fullTopicName, pointMsg);
                    break;
                    
                case MessageType.Pose:
                    var poseMsg = Util.Geometry.GetMPose(position, rotation);
                    _envROS.Publish(_fullTopicName, poseMsg);
                    break;
                    
                case MessageType.PoseStamped:
                    string fullFrameId = string.IsNullOrEmpty(_envROS.Prefix) ? frameId : $"/{_envROS.Prefix}{frameId}";
                    var poseStampedMsg = Util.Geometry.GetMPoseStamped(position, rotation, fullFrameId);
                    _envROS.Publish(_fullTopicName, poseStampedMsg);
                    break;
                    
                case MessageType.PoseWithCovarianceStamped:
                    string fullFrameIdCov = string.IsNullOrEmpty(_envROS.Prefix) ? frameId : $"/{_envROS.Prefix}{frameId}";
                    var poseWithCovStampedMsg = Util.Geometry.GetMPoseWithCovarianceStamped(position, rotation, fullFrameIdCov);
                    _envROS.Publish(_fullTopicName, poseWithCovStampedMsg);
                    break;
            }
        }
        
        private void OnDestroy()
        {
            CancelInvoke(nameof(PublishMessage));
        }
        
        [ContextMenu("Test Publish")]
        private void TestPublish()
        {
            PublishMessage();
            Debug.Log($"[{name}] Test publish sent to {_fullTopicName}");
        }
    }
}