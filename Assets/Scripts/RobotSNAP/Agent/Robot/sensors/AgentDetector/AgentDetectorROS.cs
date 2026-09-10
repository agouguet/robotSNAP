using System.Collections.Generic;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.Std;
using RosMessageTypes.Geometry;
using RobotSNAP.ROS;
using Newtonsoft.Json;

namespace RobotSNAP
{
    /// <summary>
    /// ROS bridge for AgentDetector - publishes detection results as JSON strings
    /// on std_msgs/String topics, and PoseArray for poses.
    /// No custom ROS messages are used.
    /// </summary>
    [RequireComponent(typeof(AgentDetector))]
    public class AgentDetectorROS : MonoBehaviour
    {
        [Header("ROS Configuration")]
        [SerializeField] private bool autoDetectPrefix = true;
        [SerializeField] private string customPrefix = "";
        
        [Header("Topics")]
        [SerializeField] private string localAgentsTopic = "/agents";
        [SerializeField] private string globalAgentsTopic = "/agents/global";
        [SerializeField] private string poseArrayTopic = "/agents/pose";
        
        [Header("Publishing")]
        [SerializeField] private float publishFrequencyHz = 10f;
        [SerializeField] private int numberOfClosestAgents = 5;
        [SerializeField] private string frameId = "base_link";
        
        [Header("Debug")]
        [SerializeField] private bool logPublishEvents = false;
        
        private EnvROS _envROS;
        private AgentDetector _detector;
        private string _fullLocalTopic;
        private string _fullGlobalTopic;
        private string _fullPoseArrayTopic;
        private float _publishInterval;
        private float _lastPublishTime;
        
        #region Unity Lifecycle
        
        private void Start() => Initialize();
        
        private void Update()
        {
            if (_envROS == null || !_envROS.IsInitialized) return;
            if (Time.time >= _lastPublishTime + _publishInterval)
            {
                PublishAll();
                _lastPublishTime = Time.time;
            }
        }
        
        #endregion
        
        #region Initialization
        
        public void Initialize()
        {
            _detector = GetComponent<AgentDetector>();
            if (_detector == null)
            {
                Debug.LogError($"[{name}] AgentDetector component not found!");
                enabled = false;
                return;
            }
            
            _envROS = FindObjectOfType<EnvROS>();
            if (_envROS == null)
            {
                Debug.LogWarning($"[{name}] EnvROS not found, will not publish");
                enabled = false;
                return;
            }
            
            string prefix = autoDetectPrefix ? _envROS.Prefix : customPrefix;
            _fullLocalTopic = string.IsNullOrEmpty(prefix) ? localAgentsTopic : $"/{prefix}{localAgentsTopic}";
            _fullGlobalTopic = string.IsNullOrEmpty(prefix) ? globalAgentsTopic : $"/{prefix}{globalAgentsTopic}";
            _fullPoseArrayTopic = string.IsNullOrEmpty(prefix) ? poseArrayTopic : $"/{prefix}{poseArrayTopic}";
            
            _envROS.RegisterPublisher<StringMsg>(_fullLocalTopic);
            _envROS.RegisterPublisher<StringMsg>(_fullGlobalTopic);
            _envROS.RegisterPublisher<PoseArrayMsg>(_fullPoseArrayTopic);
            
            _publishInterval = 1f / publishFrequencyHz;
            
            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Publishing JSON strings to:\n" +
                          $"  Local: {_fullLocalTopic}\n" +
                          $"  Global: {_fullGlobalTopic}\n" +
                          $"  PoseArray: {_fullPoseArrayTopic}\n" +
                          $"  Frequency: {publishFrequencyHz} Hz");
            }
        }
        
        #endregion
        
        #region Publishing
        
        private void PublishAll()
        {
            if (_detector == null) return;
            
            var closestAgents = _detector.GetClosestAgents(numberOfClosestAgents);
            Debug.Log($"[{name}] Publishing {closestAgents.Count} closest agents and {_detector.AgentsView.Count} total agents");
            var allAgents = GetVisibleAgentsWithStatus();
            
            PublishLocalAgentsAsJson(closestAgents);
            PublishGlobalAgentsAsJson(allAgents);
            PublishPoseArray(closestAgents);
        }
        
        private void PublishLocalAgentsAsJson(List<GameObject> agents)
        {
            var jsonArray = new List<string>();
            foreach (var agent in agents)
            {
                if (agent == null) continue;
                jsonArray.Add(SerializeAgentToJson(agent, visible: true));
            }
            PublishJsonArray(_fullLocalTopic, jsonArray);
        }
        
        private void PublishGlobalAgentsAsJson(List<(GameObject agent, bool visible)> agentsWithStatus)
        {
            var jsonArray = new List<string>();
            foreach (var (agent, visible) in agentsWithStatus)
            {
                if (agent == null) continue;
                jsonArray.Add(SerializeAgentToJson(agent, visible));
            }
            PublishJsonArray(_fullGlobalTopic, jsonArray);
        }
        
        private string SerializeAgentToJson(GameObject agent, bool visible)
        {
            // Position relative
            Vector3 relPos = transform.InverseTransformPoint(agent.transform.position);
            var fluPos = relPos.To<FLU>();     // type: Vector3<FLU>
            
            // Vélocité relative
            Vector3 relVel = Vector3.zero;
            var rb = agent.GetComponent<Rigidbody>();
            if (rb != null)
                relVel = transform.InverseTransformDirection(rb.linearVelocity);
            var fluVel = relVel.To<FLU>();     // type: Vector3<FLU>
            
            string id = agent.GetInstanceID().ToString();
            
            var jsonObj = new Dictionary<string, object>
            {
                ["id"] = id,
                ["visible"] = visible,
                ["position"] = new Dictionary<string, float>
                {
                    ["x"] = fluPos.x,
                    ["y"] = fluPos.y,
                    ["z"] = fluPos.z
                },
                ["velocity"] = new Dictionary<string, float>
                {
                    ["x"] = fluVel.x,
                    ["y"] = fluVel.y,
                    ["z"] = fluVel.z
                }
            };
            
            return JsonConvert.SerializeObject(jsonObj);
        }
        
        private void PublishJsonArray(string topic, List<string> jsonObjects)
        {
            string jsonString = "[" + string.Join(",", jsonObjects) + "]";
            var msg = new StringMsg { data = jsonString };
            _envROS.Publish(topic, msg);
            
            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Published {jsonObjects.Count} agents as JSON on {topic}");
            }
        }
        
        private void PublishPoseArray(List<GameObject> agents)
        {
            var poseArray = new PoseArrayMsg();
            poseArray.header.frame_id = GetFullFrameId();
            ROSTimeUtils.UpdateHeader(poseArray.header);
            
            poseArray.poses = new PoseMsg[agents.Count];
            
            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent == null) continue;
                
                Vector3 relPos = transform.InverseTransformPoint(agent.transform.position);
                Quaternion relRot = Quaternion.Inverse(transform.rotation) * agent.transform.rotation;
                
                poseArray.poses[i] = new PoseMsg
                {
                    position = relPos.To<FLU>(),
                    orientation = relRot.To<FLU>()
                };
            }
            
            _envROS.Publish(_fullPoseArrayTopic, poseArray);
        }
        
        private List<(GameObject agent, bool visible)> GetVisibleAgentsWithStatus()
        {
            var result = new List<(GameObject, bool)>();
            if (_detector.AgentsView == null) return result;
            
            foreach (var kvp in _detector.AgentsView)
            {
                if (kvp.Key != null && kvp.Key.activeSelf)
                    result.Add((kvp.Key, kvp.Value));
            }
            return result;
        }
        
        private string GetFullFrameId()
        {
            string prefix = autoDetectPrefix ? _envROS.Prefix : customPrefix;
            return string.IsNullOrEmpty(prefix) ? frameId : $"/{prefix}{frameId}";
        }
        
        #endregion
        
        #region Editor Utilities
        
        [ContextMenu("Force Publish")]
        private void EditorForcePublish() => PublishAll();
        
        [ContextMenu("Log Configuration")]
        private void EditorLogConfiguration()
        {
            Debug.Log($"[{name}] Configuration:\n" +
                      $"  Local Topic: {_fullLocalTopic}\n" +
                      $"  Global Topic: {_fullGlobalTopic}\n" +
                      $"  PoseArray Topic: {_fullPoseArrayTopic}\n" +
                      $"  Frequency: {publishFrequencyHz} Hz\n" +
                      $"  Closest Agents: {numberOfClosestAgents}\n" +
                      $"  Frame ID: {GetFullFrameId()}\n" +
                      $"  Detector: {(_detector != null ? "OK" : "Missing")}\n" +
                      $"  EnvROS: {(_envROS != null ? "OK" : "Missing")}");
        }
        
        #endregion
    }
}