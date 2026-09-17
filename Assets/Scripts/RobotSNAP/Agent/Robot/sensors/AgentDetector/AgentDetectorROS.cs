using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using RobotSNAP.Agents;
using RobotSNAP.ROS;
using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using UnityEngine;

namespace RobotSNAP
{
    /// <summary>
    /// ROS bridge for AgentDetector: every agent the detector tracks, as one JSON snapshot on
    /// <c>/simulation/agents</c>.
    ///
    /// The frame of the snapshot is the robot this component lives on, not the world: each agent is
    /// brought back into the robot frame with InverseTransformPoint and InverseTransformDirection, then
    /// converted to the ROS axis convention (x forward, y left, z up), which is what the "frame" field of
    /// the message names. No custom ROS message is used.
    ///
    /// The <c>id</c> of an entry is the id its <c>humans</c> entry carries in <c>/simulation/state</c> - the
    /// one the <c>humans</c> command of <c>/simulation/control</c> addresses - so a client can walk from what
    /// the robot sees to the human it wants to drive. An agent that is not a human has no such handle and
    /// keeps its Unity entity id.
    /// </summary>
    [RequireComponent(typeof(AgentDetector))]
    public class AgentDetectorROS : MonoBehaviour
    {
        [Header("ROS Configuration")]
        [SerializeField] private bool autoDetectPrefix = true;
        [SerializeField] private string customPrefix = "";

        [Header("Topics")]
        [Tooltip("Every agent the detector tracks, with the visibility it computed for it.")]
        [SerializeField] private string agentsTopic = RobotSNAPTopics.SimulationAgents;

        [Header("Publishing")]
        [SerializeField] private float publishFrequencyHz = 10f;

        [Header("Debug")]
        [SerializeField] private bool logPublishEvents = false;

        [SerializeField] private EnvROS _envROS;
        private AgentDetector _detector;
        private string _fullAgentsTopic;
        private float _publishInterval;
        private float _lastPublishTime;

        #region Unity Lifecycle

        private void Start() => Initialize();

        private void Update()
        {
            if (_envROS == null || !_envROS.IsInitialized) return;
            if (Time.time >= _lastPublishTime + _publishInterval)
            {
                PublishAgents();
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

            _envROS ??= FindAnyObjectByType<EnvROS>();
            if (_envROS == null)
            {
                Debug.LogWarning($"[{name}] EnvROS not found, will not publish");
                enabled = false;
                return;
            }

            string prefix = autoDetectPrefix ? _envROS.Prefix : customPrefix;
            // Joined by the topic table. This line used to concatenate the prefix and the name and rely on
            // the leading slash of the configured topic to separate them, so a name configured without one
            // came out as `/myenvsimulation/agents`.
            _fullAgentsTopic = RobotSNAPTopics.Full(agentsTopic, prefix);

            _envROS.RegisterPublisher<StringMsg>(_fullAgentsTopic);

            _publishInterval = 1f / publishFrequencyHz;

            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Publishing JSON strings to:\n" +
                          $"  Agents: {_fullAgentsTopic}\n" +
                          $"  Frequency: {publishFrequencyHz} Hz");
            }
        }

        #endregion

        #region Publishing

        /// <summary>
        /// Every active agent the detector tracks, in the frame of this robot. The visibility flag is the
        /// one the detector computed, so a client sees the agents the robot cannot: filtering by distance
        /// and by line of sight is the detector's business, not the stream's.
        /// </summary>
        private void PublishAgents()
        {
            if (_detector == null) return;

            var agents = new List<object>();
            foreach ((GameObject agent, bool visible) in VisibleAgents())
            {
                if (agent == null) continue;
                agents.Add(AgentJson(agent, visible));
            }

            var payload = new Dictionary<string, object>
            {
                ["agents"] = agents,
                ["frame"] = "robot"
            };

            string json = JsonConvert.SerializeObject(payload);
            _envROS.Publish(_fullAgentsTopic, new StringMsg { data = json });

            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Published {agents.Count} agents as JSON on {_fullAgentsTopic}");
            }
        }

        /// <summary>
        /// One agent, in the frame of this robot: position and velocity are both read relative to the
        /// robot transform and then expressed in the ROS axes, so x is forward for the robot and y is its
        /// left. An agent without a rigidbody has no velocity to report and goes out at rest.
        /// </summary>
        private object AgentJson(GameObject agent, bool visible)
        {
            Vector3<FLU> position = transform.InverseTransformPoint(agent.transform.position).To<FLU>();

            Vector3 relativeVelocity = Vector3.zero;
            Rigidbody body = agent.GetComponent<Rigidbody>();
            if (body != null)
                relativeVelocity = transform.InverseTransformDirection(body.linearVelocity);
            Vector3<FLU> velocity = relativeVelocity.To<FLU>();

            return new Dictionary<string, object>
            {
                ["id"] = AgentId(agent),
                ["x"] = position.x,
                ["y"] = position.y,
                ["z"] = position.z,
                ["vx"] = velocity.x,
                ["vy"] = velocity.y,
                ["vz"] = velocity.z,
                ["visible"] = visible
            };
        }

        /// <summary>
        /// The id a client can act on.
        ///
        /// A human answers its own <see cref="HumanAgent.agentId"/>, the number <c>/simulation/state</c>
        /// publishes and the <c>humans</c> command of <c>/simulation/control</c> resolves, so the two streams
        /// describe the same person under the same name. Publishing the entity id here used to make the crowd
        /// the robot sees impossible to join with the crowd it can drive, and it changed on every domain
        /// reload, so nothing could be tracked across a run either.
        /// </summary>
        private static string AgentId(GameObject agent)
        {
            HumanAgent human = agent.GetComponentInParent<HumanAgent>();
            if (human != null)
                return human.agentId.ToString(CultureInfo.InvariantCulture);
            return agent.GetEntityId().ToString();
        }

        /// <summary>
        /// Agents the detector tracks that are still simulated. A pooled agent is inactive and would
        /// otherwise be published as an agent standing at the pool's origin.
        /// </summary>
        private List<(GameObject agent, bool visible)> VisibleAgents()
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

        #endregion

        #region Editor Utilities

        [ContextMenu("Force Publish")]
        private void EditorForcePublish() => PublishAgents();

        [ContextMenu("Log Configuration")]
        private void EditorLogConfiguration()
        {
            Debug.Log($"[{name}] Configuration:\n" +
                      $"  Agents Topic: {_fullAgentsTopic}\n" +
                      $"  Frequency: {publishFrequencyHz} Hz\n" +
                      $"  Frame: robot\n" +
                      $"  Detector: {(_detector != null ? "OK" : "Missing")}\n" +
                      $"  EnvROS: {(_envROS != null ? "OK" : "Missing")}");
        }

        #endregion
    }
}
