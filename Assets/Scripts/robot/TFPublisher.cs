using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Tf2;
using RosMessageTypes.Geometry;
using System.Collections.Generic;
using Unity.Robotics.UrdfImporter;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.Std;
using System;
using System.Linq;


namespace ROS_DRL
{
    public class TFPublisher : MonoBehaviour
    {
        public string tfTopic = "/tf";
        [HideInInspector]
        public string prefix = "";
        List<string> m_GlobalFrameIds = new List<string> { "map", "odom" };
        public float publishRateHz = 10f;

        ROSConnection ros;

        double PublishPeriodSeconds => 1.0f / publishRateHz;
        double m_LastPublishTimeSeconds;
        bool ShouldPublishMessage => Supervisor.instance.clock.LastMSecs > m_LastPublishTimeSeconds + PublishPeriodSeconds;
        float timeBetweenPublishes;
        float timeElapsed;

        void Start()
        {
            EnvController envFoundScript = Utils.FindScriptInParents<EnvController>(transform);
            if (envFoundScript != null) { prefix = envFoundScript.prefix + "/"; }
            else
            { 
                Robot robotFoundScript = Utils.FindScriptInParents<Robot>(transform);
                if (robotFoundScript != null) {prefix = robotFoundScript.prefix + "/";}
            }

            ros = ROSConnection.GetOrCreateInstance();
            ros.RegisterPublisher<TFMessageMsg>(tfTopic);

            timeBetweenPublishes = 1f / publishRateHz;
            timeElapsed = 0f;
        }

        void Update()
        {
            if (ShouldPublishMessage)
            {
                PublishTFs();
            }
        }

        void PublishTFs()
        {
            List<TransformStampedMsg> tfList = new List<TransformStampedMsg>();


            // if (m_GlobalFrameIds.Count > 0)
            // {
            //     var tfRootToGlobal = new TransformStampedMsg(
            //         new RosMessageTypes.Std.HeaderMsg(Supervisor.instance.clock.Now(), m_GlobalFrameIds.Last()),
            //         gameObject.name,
            //         gameObject.transform.To<FLU>());
            //     tfList.Add(tfRootToGlobal);
            // }
            // else
            // {
            //     Debug.LogWarning($"No {m_GlobalFrameIds} specified, transform tree will be entirely local coordinates.");
            // }



            string parentFrame = "";

            // In case there are multiple "global" transforms that are effectively the same coordinate frame, 
            // treat this as an ordered list, first entry is the "true" global
            for (var i = 1; i < m_GlobalFrameIds.Count; ++i)
            {
                var tfGlobalToGlobal = new TransformStampedMsg(
                    new RosMessageTypes.Std.HeaderMsg(Supervisor.instance.clock.Now(), 
                    (prefix + m_GlobalFrameIds[i - 1]).TrimStart('/')),
                    (prefix + m_GlobalFrameIds[i]).TrimStart('/'),
                    // Initializes to identity transform
                    new TransformMsg());
                tfList.Add(tfGlobalToGlobal);
                parentFrame = (prefix + m_GlobalFrameIds[i]).TrimStart('/');
            }

            // Parcours récursif à partir de ce GameObject
            AddTransformsRecursive(transform, tfList, parentFrame);

            TFMessageMsg tfMessage = new TFMessageMsg(tfList.ToArray());
            ros.Send(tfTopic, tfMessage);
            m_LastPublishTimeSeconds = Supervisor.instance.clock.LastMSecs;
        }

        void AddTransformsRecursive(Transform current, List<TransformStampedMsg> tfList, string parentFrame)
        {
            UrdfLink link = current.GetComponent<UrdfLink>();
            if (link != null)
            {
                string currentFrame = prefix+current.name;

                Vector3<FLU> position = current.localPosition.To<FLU>();
                Quaternion<FLU> rotation = current.localRotation.To<FLU>();
                

                // Message ROS
                RosMessageTypes.Std.HeaderMsg header = new RosMessageTypes.Std.HeaderMsg
                {
                    stamp = Supervisor.instance.clock.Now(),
                    frame_id = parentFrame.TrimStart('/')
                };

                TransformMsg tf = new TransformMsg
                {
                    translation = new Vector3Msg(position.x, position.y, position.z),
                    rotation = new QuaternionMsg(rotation.x, rotation.y, rotation.z, rotation.w)
                };

                TransformStampedMsg tfStamped = new TransformStampedMsg
                {
                    header = header,
                    child_frame_id = currentFrame.TrimStart('/'),
                    transform = tf
                };

                tfList.Add(tfStamped);

                // On continue avec ce lien comme nouveau parentFrame
                parentFrame = currentFrame;
            }
            // Recurse children
            foreach (Transform child in current)
            {
                AddTransformsRecursive(child, tfList, parentFrame);
            }
        }
    }
}