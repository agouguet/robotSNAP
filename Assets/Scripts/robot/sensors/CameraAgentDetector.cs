using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.Geometry;
using RosMessageTypes.Agents;
using RosMessageTypes.Std;
using System;

namespace ROS_DRL
{
    public class CameraAgentDetector : MonoBehaviour
    {
        private EnvController env;

        public float radius;
        [Range(0, 360)]
        public float angle;

        public Dictionary<GameObject, bool> agentsView = new Dictionary<GameObject, bool>();
        public GameObject agentPool;

        [TagSelector]
        public string TagFilter = "";

        [TagSelector]
        public string[] TagFilterArray = new string[] { };

        public LayerMask targetMask;
        public LayerMask obstructionMask;

        private ROSConnection ros;
        public bool publishToROS = true;
        public int numberOfClosestHumanPublish = 5;
        public string prefix = "";
        public string topicName = "/agents";
        public string poseArrayTopic = "/agents/pose";
        public string globalTopic = "/agents/global";
        public string frame = "/base_link";
        public float publishFrequencyHz = 10f;
        private float timeElapsed = 0f;
        private float publishInterval => 1f / publishFrequencyHz;


        void Start()
        {
            env = Utils.FindScriptInParents<EnvController>(transform);
            if (env != null){
                agentPool = env.humanPool;
                prefix = env.prefix;
            }
            else
            {
                Robot robotFoundScript = Utils.FindScriptInParents<Robot>(transform);
                if (robotFoundScript != null) { prefix = robotFoundScript.prefix; }
            }

            StartCoroutine(LateStart(0.2f));

            if (publishToROS)
            {
                ros = ROSConnection.GetOrCreateInstance();
                ros.RegisterPublisher<RosMessageTypes.Agents.AgentArrayMsg>(prefix + topicName);
                ros.RegisterPublisher<RosMessageTypes.Agents.AgentArrayMsg>(prefix + globalTopic);
                ros.RegisterPublisher<PoseArrayMsg>(prefix + poseArrayTopic);
                InvokeRepeating("PublishAgents", 1.0f, (float)publishInterval);
                InvokeRepeating("PublishGlobalAgents", 1.0f, (float)publishInterval);
                InvokeRepeating("PublishPoseArray", 1.0f, (float)publishInterval);
            }
        }

        IEnumerator LateStart(float waitTime)
        {
            yield return new WaitForSeconds(waitTime);

            foreach (Transform child in agentPool.transform)
            {
                if (child.gameObject.tag == TagFilter)
                {
                    agentsView.Add(child.gameObject, false);
                }
            }

            StartCoroutine(FOVRoutine());
        }

        public void Restart()
        {
            Clear();
            StartCoroutine(FOVRoutine());
        }

        private IEnumerator FOVRoutine()
        {
            WaitForSeconds wait = new WaitForSeconds(0.2f);

            while (true)
            {
                yield return wait;
                // if(env.play)
                FieldOfViewCheck();
            }
        }

        private void FieldOfViewCheck()
        {
            Collider[] rangeChecks = Physics.OverlapSphere(transform.position, radius, targetMask);

            if (rangeChecks.Length != 0)
            {
                foreach (Collider agentCollider in rangeChecks)
                {
                    Transform target = agentCollider.transform;
                    if (target.gameObject == this.gameObject || target.gameObject.tag != TagFilter)
                        continue;
                    Vector3 target_position = target.position + new Vector3(0f, 0.5f, 0f);
                    Vector3 directionToTarget = (target_position - transform.position).normalized;

                    if (Vector3.Angle(transform.forward, directionToTarget) < angle / 2)
                    {
                        float distanceToTarget = Util.Geometry.GroundPlaneDist(transform.position, target_position);
                        if (distanceToTarget <= 0)
                            continue;

                        // print(target + "    " + Physics.Raycast(transform.position, directionToTarget, distanceToTarget, obstructionMask));

                        if (!Physics.Raycast(transform.position, directionToTarget, distanceToTarget, obstructionMask) && distanceToTarget < radius)
                        {
                            agentsView[target.gameObject] = true;
                        }
                        else
                        {
                            agentsView[target.gameObject] = false;
                        }
                    }
                    else
                        agentsView[target.gameObject] = false;
                }
            }
            else
            {
                List<GameObject> keys = new List<GameObject>(agentsView.Keys);
                foreach (GameObject agent in keys)
                {
                    agentsView[agent] = false;
                }
            }
        }

        public List<GameObject> GetHumanPosition(int numberOfClosestAgents)
        {
            List<GameObject> agents = new List<GameObject>();

            Dictionary<GameObject, float> distanceToAgent = new Dictionary<GameObject, float>();
            foreach (var agent in agentsView)
            {
                // print(agent.Key + "   " + agent.Value + "   " + Vector3.Distance(this.transform.localPosition, new Vector3(agent.Key.transform.localPosition.x, this.transform.localPosition.y, agent.Key.transform.localPosition.z)));
                if (agent.Value && agent.Key != null)
                {
                    distanceToAgent.Add(agent.Key, Vector3.Distance(this.transform.localPosition, new Vector3(agent.Key.transform.localPosition.x, this.transform.localPosition.y, agent.Key.transform.localPosition.z)));
                }
            }

            var bottom = distanceToAgent.OrderBy(pair => pair.Value).Take(numberOfClosestAgents);

            for(int j = 0; j < bottom.Count(); j++) 
            {
                var agent = bottom.ElementAt(j);
                agents.Add(agent.Key);
            }

            return agents;
        }

        private void PublishAgents(){
            RosMessageTypes.Agents.AgentArrayMsg message = new RosMessageTypes.Agents.AgentArrayMsg();
            message.header.frame_id = (prefix + frame).TrimStart('/');
            Supervisor.instance.clock.UpdateMHeader(message.header);

            List<GameObject> agents = GetHumanPosition(numberOfClosestHumanPublish);

            message.agents = new RosMessageTypes.Agents.AgentMsg[agents.Count];
            int i = 0;

            foreach (GameObject agentGO in agents)
            {
                RosMessageTypes.Agents.AgentMsg agent = new RosMessageTypes.Agents.AgentMsg();
                agent.id = (ulong) i;
                // agent.pose = Util.Geometry.GetMPose(agentGO.transform);
                agent.pose = new PoseMsg();
                Vector3 relPos = transform.InverseTransformPoint(agentGO.transform.position);
                Quaternion relRot = Quaternion.Inverse(transform.rotation) * agentGO.transform.rotation;
                agent.pose.position = relPos.To<FLU>();
                agent.pose.orientation = relRot.To<FLU>();
                Rigidbody agentRb =  agentGO.GetComponent<Rigidbody>();
                agent.velocity = Util.Geometry.GetMTwist(agentRb.linearVelocity);
                agent.visible_by_robot = true;
                message.agents[i++] = agent;
            }
            ros.Publish(prefix + topicName, message);
        }

        private void PublishGlobalAgents()
        { 
            RosMessageTypes.Agents.AgentArrayMsg message = new RosMessageTypes.Agents.AgentArrayMsg();
            message.header.frame_id = (prefix + frame).TrimStart('/');
            Supervisor.instance.clock.UpdateMHeader(message.header);

            var agentsList = new List<RosMessageTypes.Agents.AgentMsg>();
            
            foreach (var agentItem in agentsView)
            {
                var agentGO = agentItem.Key;
                
                if (agentGO.activeSelf)
                {
                    RosMessageTypes.Agents.AgentMsg agent = new RosMessageTypes.Agents.AgentMsg();
                    agent.id = (ulong)agentsList.Count;

                    // agent.pose = Util.Geometry.GetMPose(agentGO.transform);
                    agent.pose = new PoseMsg();
                    Vector3 relPos = agentGO.transform.position;
                    Quaternion relRot = agentGO.transform.rotation;
                    agent.pose.position = relPos.To<FLU>();
                    agent.pose.orientation = relRot.To<FLU>();
                    Rigidbody agentRb = agentGO.GetComponent<Rigidbody>();
                    agent.velocity = Util.Geometry.GetMTwist(agentRb.linearVelocity);
                    agent.visible_by_robot = agentItem.Value;

                    agentsList.Add(agent);
                }
            }

            message.agents = agentsList.ToArray();
            ros.Publish(prefix + globalTopic, message);
        }


        private void PublishPoseArray()
        {
            List<GameObject> agents = GetHumanPosition(numberOfClosestHumanPublish);

            PoseArrayMsg poseArray = new PoseArrayMsg();
            poseArray.header.frame_id = (prefix + frame).TrimStart('/');;
            Supervisor.instance.clock.UpdateMHeader(poseArray.header);

            poseArray.poses = new PoseMsg[agents.Count];
            for (int i = 0; i < agents.Count; i++)
            {
                Vector3 relativePosition = transform.InverseTransformPoint(agents[i].transform.position);
                Quaternion relativeRotation = Quaternion.Inverse(transform.rotation) * agents[i].transform.rotation;

                PoseMsg relativePose = new PoseMsg();
                relativePose.position = relativePosition.To<FLU>();
                relativePose.orientation = relativeRotation.To<FLU>();

                poseArray.poses[i] = relativePose;
            }

            ros.Publish(prefix + poseArrayTopic, poseArray);
        }

        public void Clear()
        {
            List<GameObject> keys = new List<GameObject>(agentsView.Keys);
            foreach (GameObject agent in keys)
            {
                agentsView[agent] = false;
            }
            // agentsView = new Dictionary<GameObject, bool>();
        }
        
        protected void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0, 1, 0, 0.75F);
            List<GameObject> keys = new List<GameObject>(agentsView.Keys);
            foreach (var t in keys)
            {
                if (agentsView[t])
                {
                    Gizmos.DrawLine(transform.position, t.transform.position);
                }
            }
        }
    }
}