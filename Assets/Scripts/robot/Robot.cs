using UnityEngine;
using Unity.Robotics.ROSTCPConnector;

namespace ROS_DRL
{
    public class Robot : MonoBehaviour
    {
        private EnvController env;
        protected ROSConnection ros;

        [Header("Navigation")]
        public string CommandVelocityTopic = "/cmd_vel";
        [HideInInspector]
        public string prefix = "";
        private float targetLinearSpeed;
        private float targetAngularSpeed;

        public float maxTimeDeltaSec = 0.5f;
        private float lastMessageTS = 0;

        [Header("Attribute")]
        public float radius = 0.16f;
        public GameObject base_link;
        public CameraAgentDetector detector;
        protected Rigidbody m_AgentRb;

        public void Start()
        {
            env = Utils.FindScriptInParents<EnvController>(transform);
            if (env != null){CommandVelocityTopic = env.prefix + CommandVelocityTopic;}

            ros = ROSConnection.GetOrCreateInstance();
            ros.Subscribe<RosMessageTypes.Geometry.TwistMsg>(prefix+CommandVelocityTopic, CmdVelMessage);
            m_AgentRb = base_link.GetComponent<Rigidbody>();
        }

        public void Reset()
        {
            detector.Restart();
        }

        void FixedUpdate()
        {
            if (Time.time - lastMessageTS > maxTimeDeltaSec * Time.timeScale || !env.play)
            {
                targetLinearSpeed = 0.0f;
                targetAngularSpeed = 0.0f;
            }

            if (m_AgentRb == null) return;

            Vector3 localAngularVelocity = new Vector3(0, targetAngularSpeed, 0);
            Vector3 worldAngularVelocity = m_AgentRb.transform.TransformDirection(localAngularVelocity);

            m_AgentRb.angularVelocity = worldAngularVelocity;

            m_AgentRb.linearVelocity = m_AgentRb.transform.forward * targetLinearSpeed;
        }

        private void CmdVelMessage(RosMessageTypes.Geometry.TwistMsg msg)
        {
            if (msg == null) { return; }
            targetLinearSpeed = (float)msg.linear.x;
            targetAngularSpeed = -(float)msg.angular.z;
            lastMessageTS = Time.time;
        }

        public void SetLaserSample(int laserSample)
        {
            RaycastLaserScanner m_laser = laser;
            m_laser.samples = laserSample;
            m_laser.Init();
            
            LaserScanPublisher m_laserPublisher = laserPublisher;
            m_laserPublisher.Init();
        }

        public RaycastLaserScanner laser
        {
            get
            {
                return FindInHierarchy<RaycastLaserScanner>(transform);
            }
        }

        public LaserScanPublisher laserPublisher
        {
            get
            {
                return FindInHierarchy<LaserScanPublisher>(transform);
            }
        }

        T FindInHierarchy<T>(Transform root) where T : Component
        {
            T match = root.GetComponent<T>();
            if (match != null)
                return match;

            foreach (Transform child in root)
            {
                match = FindInHierarchy<T>(child);
                if (match != null)
                    return match;
            }

            return null;
        }

        public new Transform transform
        {
            get
            {
                return base_link.transform;
            }
        }
        public Vector3 position
        {
            get
            {
                return base_link.transform.position;
            }
        }
        public Quaternion rotation
        {
            get
            {
                return base_link.transform.rotation;
            }
        }
        public override string ToString()
        {
            return gameObject.name;
        }
    }
}