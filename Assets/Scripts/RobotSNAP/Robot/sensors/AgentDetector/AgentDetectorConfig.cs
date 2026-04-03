using UnityEngine;

namespace RobotSNAP
{
    /// <summary>
    /// Configuration for AgentDetector - allows sharing settings across multiple detectors
    /// </summary>
    [CreateAssetMenu(fileName = "AgentDetectorConfig", menuName = "RobotSNAP/Agent Detector Config")]
    public class AgentDetectorConfig : ScriptableObject
    {
        [Header("Detection Settings")]
        public float defaultRadius = 5f;
        public float defaultAngle = 90f;
        
        [Header("Filtering")]
        public string defaultTargetTag = "";
        public string[] defaultTargetTags = new string[] { };
        
        [Header("Performance")]
        public float defaultUpdateInterval = 0.2f;
        
        [Header("ROS Publishing")]
        public float defaultPublishFrequencyHz = 10f;
        public int defaultNumberOfClosestAgents = 5;
        public string defaultFrameId = "base_link";
    }
}