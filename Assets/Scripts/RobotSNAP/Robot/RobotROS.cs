using UnityEngine;
using RobotSNAP.ROS;

namespace RobotSNAP
{
    /// <summary>
    /// ROS bridge for Robot - translates ROS messages to Robot commands.
    /// This is the only ROS-dependent component for the robot.
    /// </summary>
    [RequireComponent(typeof(Robot))]
    public class RobotROS : MonoBehaviour
    {
        [Header("ROS Configuration")]
        [SerializeField] private bool autoDetectPrefix = true;
        [SerializeField] private string customPrefix = "";
        [SerializeField] private string commandVelocityTopic = "/cmd_vel";
        
        [Header("Mapping")]
        [SerializeField] private float linearSpeedMultiplier = 1.0f;
        [SerializeField] private float angularSpeedMultiplier = 1.0f;
        
        [Header("Debug")]
        [SerializeField] private bool logCommands = false;
        
        private EnvROS _envROS;
        private Robot _robot;
        private string _fullTopicName;
        
        private void Start()
        {
            Initialize();
        }
        
        public void Initialize()
        {
            // Get robot reference
            _robot = GetComponent<Robot>();
            if (_robot == null)
            {
                Debug.LogError($"[{name}] Robot component not found!");
                enabled = false;
                return;
            }
            
            // Find EnvROS
            _envROS = FindObjectOfType<EnvROS>();
            
            if (_envROS == null)
            {
                Debug.LogWarning($"[{name}] EnvROS not found, commands will not be received");
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
            _fullTopicName = string.IsNullOrEmpty(prefix) ? commandVelocityTopic : $"/{prefix}{commandVelocityTopic}";
            
            // Subscribe to command velocity topic
            _envROS.RegisterSubscriber<RosMessageTypes.Geometry.TwistMsg>(_fullTopicName, OnCmdVelMessage);
            
            if (logCommands)
            {
                Debug.Log($"[{name}] Listening to: {_fullTopicName}");
            }
        }
        
        private void OnCmdVelMessage(RosMessageTypes.Geometry.TwistMsg msg)
        {
            if (msg == null || _robot == null) return;
            
            // Convert ROS message to robot commands
            float linearSpeed = (float)msg.linear.x * linearSpeedMultiplier;
            float angularSpeed = -(float)msg.angular.z * angularSpeedMultiplier; // ROS to Unity coordinate conversion
            
            _robot.SetVelocity(linearSpeed, angularSpeed);
            
            if (logCommands)
            {
                Debug.Log($"[{name}] Received cmd_vel: linear={linearSpeed:F2}, angular={angularSpeed:F2}");
            }
        }
        
        private void OnDestroy()
        {
            // Cleanup if needed
            if (_envROS != null && _envROS.IsInitialized)
            {
                // Note: ROSConnection doesn't have Unsubscribe, so we just let it be
                // The robot will stop receiving commands when destroyed
            }
        }
        
        [ContextMenu("Log Configuration")]
        private void LogConfiguration()
        {
            Debug.Log($"[{name}] Configuration:\n" +
                      $"  Topic: {_fullTopicName}\n" +
                      $"  Linear Multiplier: {linearSpeedMultiplier}\n" +
                      $"  Angular Multiplier: {angularSpeedMultiplier}\n" +
                      $"  Auto Prefix: {autoDetectPrefix}\n" +
                      $"  Robot: {(_robot != null ? "OK" : "Missing")}\n" +
                      $"  EnvROS: {(_envROS != null ? "OK" : "Missing")}");
        }
    }
}