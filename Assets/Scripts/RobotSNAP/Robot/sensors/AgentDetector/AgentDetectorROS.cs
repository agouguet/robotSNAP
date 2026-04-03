using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.Geometry;
using RosMessageTypes.Agents;
using RobotSNAP.ROS;

namespace RobotSNAP
{
    /// <summary>
    /// ROS bridge for AgentDetector - publishes detection results to ROS topics.
    /// This is the only ROS-dependent component for agent detection.
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
        
        private void Start()
        {
            Initialize();
        }
        
        private void Update()
        {
            if (_envROS == null || !_envROS.IsInitialized) return;
            
            if (Time.time >= _lastPublishTime + _publishInterval)
            {
                PublishAll();
                _lastPublishTime = Time.time;
            }
        }
        
        private void OnDestroy()
        {
            // Cleanup
        }
        
        #endregion
        
        #region Initialization
        
        public void Initialize()
        {
            // Get detector reference
            _detector = GetComponent<AgentDetector>();
            if (_detector == null)
            {
                Debug.LogError($"[{name}] AgentDetector component not found!");
                enabled = false;
                return;
            }
            
            // Find EnvROS
            _envROS = FindObjectOfType<EnvROS>();
            
            if (_envROS == null)
            {
                Debug.LogWarning($"[{name}] EnvROS not found, will not publish");
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
            
            // Build topic names
            _fullLocalTopic = string.IsNullOrEmpty(prefix) ? localAgentsTopic : $"/{prefix}{localAgentsTopic}";
            _fullGlobalTopic = string.IsNullOrEmpty(prefix) ? globalAgentsTopic : $"/{prefix}{globalAgentsTopic}";
            _fullPoseArrayTopic = string.IsNullOrEmpty(prefix) ? poseArrayTopic : $"/{prefix}{poseArrayTopic}";
            
            // Register publishers
            _envROS.RegisterPublisher<AgentArrayMsg>(_fullLocalTopic);
            _envROS.RegisterPublisher<AgentArrayMsg>(_fullGlobalTopic);
            _envROS.RegisterPublisher<PoseArrayMsg>(_fullPoseArrayTopic);
            
            _publishInterval = 1f / publishFrequencyHz;
            
            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Publishing to:\n" +
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
            var allAgents = GetVisibleAgentsWithStatus();
            
            PublishLocalAgents(closestAgents);
            PublishGlobalAgents(allAgents);
            PublishPoseArray(closestAgents);
        }
        
        private void PublishLocalAgents(List<GameObject> agents)
        {
            var message = new AgentArrayMsg();
            message.header.frame_id = GetFullFrameId();
            ROSTimeUtils.UpdateHeader(message.header);
            
            message.agents = new AgentMsg[agents.Count];
            
            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent == null) continue;
                
                message.agents[i] = CreateAgentMessage(agent, i, true);
            }
            
            _envROS.Publish(_fullLocalTopic, message);
            
            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Published {agents.Count} local agents");
            }
        }
        
        private void PublishGlobalAgents(List<(GameObject agent, bool visible)> agents)
        {
            var message = new AgentArrayMsg();
            message.header.frame_id = GetFullFrameId();
            // Supervisor.Instance.clock.UpdateMHeader(message.header);
            ROSTimeUtils.UpdateHeader(message.header);
            
            message.agents = new AgentMsg[agents.Count];
            
            for (int i = 0; i < agents.Count; i++)
            {
                var (agent, visible) = agents[i];
                if (agent == null) continue;
                
                message.agents[i] = CreateAgentMessage(agent, i, visible);
            }
            
            _envROS.Publish(_fullGlobalTopic, message);
            
            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Published {agents.Count} global agents");
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
                
                // Relative position and rotation
                Vector3 relativePosition = transform.InverseTransformPoint(agent.transform.position);
                Quaternion relativeRotation = Quaternion.Inverse(transform.rotation) * agent.transform.rotation;
                
                poseArray.poses[i] = new PoseMsg
                {
                    position = relativePosition.To<FLU>(),
                    orientation = relativeRotation.To<FLU>()
                };
            }
            
            _envROS.Publish(_fullPoseArrayTopic, poseArray);
        }
        
        private AgentMsg CreateAgentMessage(GameObject agent, int id, bool isVisible)
        {
            var msg = new AgentMsg
            {
                id = (ulong)id,
                visible_by_robot = isVisible
            };
            
            // Relative position
            Vector3 relativePosition = transform.InverseTransformPoint(agent.transform.position);
            Quaternion relativeRotation = Quaternion.Inverse(transform.rotation) * agent.transform.rotation;
            
            msg.pose = new PoseMsg
            {
                position = relativePosition.To<FLU>(),
                orientation = relativeRotation.To<FLU>()
            };
            
            // Velocity
            var rb = agent.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Vector3 relativeVelocity = transform.InverseTransformDirection(rb.linearVelocity);
                msg.velocity = new TwistMsg
                {
                    linear = relativeVelocity.To<FLU>(),
                    angular = new Vector3Msg { x = 0, y = 0, z = 0 }
                };
            }
            
            return msg;
        }
        
        private List<(GameObject agent, bool visible)> GetVisibleAgentsWithStatus()
        {
            var result = new List<(GameObject, bool)>();
            
            foreach (var kvp in _detector.AgentsView)
            {
                if (kvp.Key != null && kvp.Key.activeSelf)
                {
                    result.Add((kvp.Key, kvp.Value));
                }
            }
            
            return result;
        }
        
        private string GetFullFrameId()
        {
            string prefix = autoDetectPrefix && _envROS != null ? _envROS.Prefix : customPrefix;
            return string.IsNullOrEmpty(prefix) ? frameId : $"/{prefix}{frameId}";
        }
        
        #endregion
        
        #region Editor Utilities
        
        [ContextMenu("Force Publish")]
        private void EditorForcePublish()
        {
            PublishAll();
            Debug.Log($"[{name}] Force published all topics");
        }
        
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