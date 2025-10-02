using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.Geometry;
using RosMessageTypes.Std;
using System;

namespace ROS_DRL
{
    public class GenericPosePublisher : MonoBehaviour
    {
        private EnvController env;

        public enum MessageType
        {
            Point,
            Pose,
            PoseStamped,
            PoseWithCovarianceStamped
        }
        public bool checkForPrefix = true;

        public string prefix = "";
        public string topicName = "/pose_topic";
        public float publishFrequencyHz = 10f;
        private float timeElapsed = 0f;
        private float publishInterval => 1f / publishFrequencyHz;

        public MessageType messageType = MessageType.Pose;
        public Transform sourceTransform;
        public string frameId = "/map";

        public bool isLocal = false;

        

        private ROSConnection ros;

        void Start()
        {
            env = Utils.FindScriptInParents<EnvController>(transform);
            
            if (checkForPrefix)
            {
                if (env != null) { prefix = env.prefix; }
                else
                {
                    Robot robotFoundScript = Utils.FindScriptInParents<Robot>(transform);
                    if (robotFoundScript != null) { prefix = robotFoundScript.prefix; }
                }
            }
            ros = ROSConnection.GetOrCreateInstance();

            // Register topic based on type
            switch (messageType)
            {
                case MessageType.Point:
                    ros.RegisterPublisher<PointMsg>(prefix + topicName);
                    break;
                case MessageType.Pose:
                    ros.RegisterPublisher<PoseMsg>(prefix + topicName);
                    break;
                case MessageType.PoseStamped:
                    ros.RegisterPublisher<PoseStampedMsg>(prefix + topicName);
                    break;
                case MessageType.PoseWithCovarianceStamped:
                    ros.RegisterPublisher<PoseWithCovarianceStampedMsg>(prefix + topicName);
                    break;
            }
            InvokeRepeating("PublishMessage", 1.0f, (float) publishInterval);
        }

        void PublishMessage()
        {
            if (!sourceTransform.gameObject.activeSelf) { return; }
            Vector3 position = isLocal ? sourceTransform.localPosition : sourceTransform.position ;
            Quaternion rotation = isLocal ? sourceTransform.localRotation : sourceTransform.rotation;

            switch (messageType)
            {
                case MessageType.Point:
                    var pointMsg = Util.Geometry.GetGeometryPoint(position.To<FLU>());
                    ros.Publish(prefix + topicName, pointMsg);
                    break;

                case MessageType.Pose:
                    var poseMsg = Util.Geometry.GetMPose(position, rotation);
                    ros.Publish(prefix + topicName, poseMsg);
                    break;

                case MessageType.PoseStamped:
                    var poseStampedMsg = Util.Geometry.GetMPoseStamped(position, rotation, prefix + frameId);
                    ros.Publish(prefix + topicName, poseStampedMsg);
                    break;

                case MessageType.PoseWithCovarianceStamped:
                    var poseWithCovStampedMsg = Util.Geometry.GetMPoseWithCovarianceStamped(position, rotation, prefix + frameId);
                    ros.Publish(prefix + topicName, poseWithCovStampedMsg);
                    break;
            }
        }
    }
}