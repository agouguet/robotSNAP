using UnityEngine;
using Unity.Robotics.ROSTCPConnector;

namespace ROS_DRL
{
    [RequireComponent(typeof(LaserScanner))]
    public class LaserScanPublisher : MonoBehaviour
    {
        private EnvController env;
        ROSConnection ros;
        public string laserTopic = "/scan";
        private LaserScanner laserScanner;
        public string FrameId = "laser";

        public float publishFrequencyHz = 10;
        private float publishInterval => 1f / publishFrequencyHz;
        private float timeElapsed;

        [HideInInspector]
        public string prefix = "";

        private RosMessageTypes.Sensor.LaserScanMsg message;

        private float previousScanTime = 0;
        private float scanPeriod;

        void Start()
        {
            env = Utils.FindScriptInParents<EnvController>(transform);
            if (env != null) { prefix = env.prefix; }
            else
            {
                Robot robotFoundScript = Utils.FindScriptInParents<Robot>(transform);
                if (robotFoundScript != null) { prefix = robotFoundScript.prefix; }
            }

            ros = ROSConnection.GetOrCreateInstance();
            ros.RegisterPublisher<RosMessageTypes.Sensor.LaserScanMsg>(prefix + laserTopic);
            Init();
        }   

        public void Init()
        { 
            laserScanner = GetComponent<LaserScanner>();
            message = laserScanner.InitializeMessage(prefix + FrameId);
            scanPeriod = (float) publishInterval;
        }

        void UpdateMessage()
        {
            // if (!env.play) { return; }
            Supervisor.instance.clock.UpdateMHeader(message.header);
            message.ranges = laserScanner.Scan();
            ros.Publish(prefix + laserTopic, message);
        }
        
        private void FixedUpdate()
        {
            if (Time.realtimeSinceStartup >= previousScanTime + scanPeriod)
            {
                UpdateMessage();
                previousScanTime = Time.realtimeSinceStartup;
            }
        }
    }
}
