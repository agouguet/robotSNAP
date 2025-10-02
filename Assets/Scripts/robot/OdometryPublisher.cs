// using UnityEngine;
// using Unity.Robotics.ROSTCPConnector;
// using Unity.Robotics.ROSTCPConnector.ROSGeometry;

// namespace ROS_DRL
// {
//     public class OdometryPublisher : MonoBehaviour
//     {
//         ROSConnection ros;
//         public float publishFrequencyHz = 10;
//         private float publishInterval => 1f / publishFrequencyHz;
//         private float timeElapsed;
//         public string topicName;

//         public Transform PublishedTransform;
//         public string FrameId = "map";

//         private RosMessageTypes.Nav.OdometryMsg message;

//         private float previousRealTime;
//         private Vector3 previousPosition = Vector3.zero;
//         private Quaternion previousRotation = Quaternion.identity;

//         private double[] identityMatrix = {1, 0, 0, 0, 0, 0,
//                                             0, 1, 0, 0, 0, 0,
//                                             0, 0, 1, 0, 0, 0,
//                                             0, 0, 0, 1, 0, 0,
//                                             0, 0, 0, 0, 1, 0,
//                                             0, 0, 0, 0, 0, 1};

//         void Start()
//         {
//             EnvController envFoundScript = Utils.FindScriptInParents<EnvController>(transform);
//             if (envFoundScript != null) { topicName = envFoundScript.prefix + topicName; }
//             else
//             {
//                 Robot robotFoundScript = Utils.FindScriptInParents<Robot>(transform);
//                 if (robotFoundScript != null) { topicName = robotFoundScript.prefix + topicName; }
//             }
//             ros = ROSConnection.GetOrCreateInstance();
//             ros.RegisterPublisher<RosMessageTypes.Nav.OdometryMsg>(topicName);

//             InitializeMessage();
//             InvokeRepeating("UpdateMessage", 1.0f, (float) publishInterval);

//         }

//         // private void FixedUpdate()
//         // {
//         //     UpdateMessage();
//         // }

//         private void InitializeMessage()
//         {
//             message = new RosMessageTypes.Nav.OdometryMsg();
//             message.pose.covariance = identityMatrix;
//             message.twist.covariance = identityMatrix;
//             message.child_frame_id = FrameId;
//         }

//         private void UpdateMessage()
//         {
//             float deltaTime = Time.fixedDeltaTime;
//             // timeElapsed += Time.deltaTime;

//             // if (timeElapsed <= publishMessageFrequency)
//             // {
//             //     return;
//             // }
            

//             // --- Calcul vitesse linéaire (en monde) ---
//             Vector3 currentPos = PublishedTransform.position;
//             Vector3 linearVelocity = (currentPos - previousPosition) / deltaTime;


//             // --- Calcul vitesse angulaire dans le repère local ---
//             Quaternion currentRot = PublishedTransform.rotation;
//             Quaternion deltaRot = currentRot * Quaternion.Inverse(previousRotation);

//             // Convertir en angle/axe
//             deltaRot.ToAngleAxis(out float angleDeg, out Vector3 axis);
//             if (float.IsNaN(angleDeg) || float.IsInfinity(axis.x))
//             {
//                 angleDeg = 0f;
//                 axis = Vector3.zero;
//             }

//             float angleRad = angleDeg * Mathf.Deg2Rad;

//             // --- Calculer la vitesse angulaire en monde ---
//             Vector3 angularVelocityWorld = axis.normalized * (angleRad / deltaTime);

//             // --- Convertir la vitesse angulaire en local si nécessaire ---
//             Vector3 angularVelocityLocal = PublishedTransform.InverseTransformDirection(angularVelocityWorld);
//             Vector3 angularVelocity = angularVelocityLocal;


//             // --- Calcul vitesse angulaire avec Quaternions ---
//             // Quaternion currentRot = PublishedTransform.rotation;
//             // Quaternion deltaRot = currentRot * Quaternion.Inverse(previousRotation);
//             // deltaRot.ToAngleAxis(out float angleInDegrees, out Vector3 rotationAxis);
//             // // Gérer cas spécial : pas de rotation
//             // if (float.IsInfinity(rotationAxis.x) || float.IsNaN(angleInDegrees))
//             // {
//             //     angleInDegrees = 0f;
//             //     rotationAxis = Vector3.zero;
//             // }
//             // float angleInRadians = angleInDegrees * Mathf.Deg2Rad;
//             // Vector3 angularVelocity = rotationAxis * (angleInRadians / deltaTime);

//             // --- Mémorise état ---
//             previousPosition = currentPos;
//             previousRotation = currentRot;

//             previousRealTime = Time.realtimeSinceStartup;

//             Supervisor.instance.clock.UpdateMHeader(message.header);
//             message.twist.twist.linear = Util.Geometry.GetGeometryVector3(linearVelocity.To<FLU>());
//             message.twist.twist.angular = Util.Geometry.GetGeometryVector3(-angularVelocity.To<FLU>());
//             message.pose.pose.position = Util.Geometry.GetGeometryPoint(PublishedTransform.localPosition.To<FLU>());
//             message.pose.pose.orientation = Util.Geometry.GetGeometryQuaternion(PublishedTransform.rotation.To<FLU>());
//             ros.Publish(topicName, message);
//             // timeElapsed = 0;
//         }
//     }
// }


using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;

namespace ROS_DRL
{
    public class OdometryPublisher : MonoBehaviour
    {
        private EnvController env;
        ROSConnection ros;
        public float publishFrequencyHz = 10;
        private float publishInterval => 1f / publishFrequencyHz;

        public string topicName;
        public Rigidbody robotRigidbody;
        public string FrameId = "base_link";

        private RosMessageTypes.Nav.OdometryMsg message;

        void Start()
        {
            env = Utils.FindScriptInParents<EnvController>(transform);
            if (env != null) topicName = env.prefix + topicName;
            else
            {
                Robot robotFoundScript = Utils.FindScriptInParents<Robot>(transform);
                if (robotFoundScript != null) topicName = robotFoundScript.prefix + topicName;
            }

            ros = ROSConnection.GetOrCreateInstance();
            ros.RegisterPublisher<RosMessageTypes.Nav.OdometryMsg>(topicName);

            InitializeMessage();
            InvokeRepeating("UpdateMessage", 1.0f, publishInterval);
        }

        private void InitializeMessage()
        {
            message = new RosMessageTypes.Nav.OdometryMsg();
            message.pose.covariance = new double[36];  // Identity or leave as zeros if you don't estimate
            message.twist.covariance = new double[36];
            message.child_frame_id = FrameId.TrimStart('/');
        }

        private void UpdateMessage()
        {
            if (robotRigidbody == null)
            {
                Debug.LogWarning("OdometryPublisher: Rigidbody not assigned.");
                return;
            }

            if (!robotRigidbody.gameObject.activeSelf) { return; }

            // --- Lecture des vitesses depuis Rigidbody ---
            Vector3 linearVelocityWorld = robotRigidbody.linearVelocity;
            Vector3 angularVelocityWorld = robotRigidbody.angularVelocity;

            // --- Conversion dans le repère local du robot ---
            Vector3 linearVelocityLocal = robotRigidbody.transform.InverseTransformDirection(linearVelocityWorld);
            Vector3 angularVelocityLocal = robotRigidbody.transform.InverseTransformDirection(angularVelocityWorld);

            // --- Mise à jour du message ROS ---
            Supervisor.instance.clock.UpdateMHeader(message.header);

            message.twist.twist.linear = Util.Geometry.GetGeometryVector3(linearVelocityLocal.To<FLU>());
            message.twist.twist.angular = Util.Geometry.GetGeometryVector3(angularVelocityLocal.To<FLU>());  // Négation: ROS z = +gauche, Unity y = +haut

            message.pose.pose.position = Util.Geometry.GetGeometryPoint(robotRigidbody.transform.localPosition.To<FLU>());
            message.pose.pose.orientation = Util.Geometry.GetGeometryQuaternion(robotRigidbody.transform.rotation.To<FLU>());

            ros.Publish(topicName, message);
        }
    }
}
