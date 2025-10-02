// Copyright (c) 2021, Members of Yale Interactive Machines Group, Yale University,
// Nathan Tsoi
// All rights reserved.
// This source code is licensed under the BSD-style license found in the
// LICENSE file in the root directory of this source tree. 

using System.Collections.Generic;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.Std;

namespace Util
{
    public class Geometry
    {

        private static double[] identityCovariance = new double[36]
        {
            1, 0, 0, 0, 0, 0,
            0, 1, 0, 0, 0, 0,
            0, 0, 1, 0, 0, 0,
            0, 0, 0, 1, 0, 0,
            0, 0, 0, 0, 1, 0,
            0, 0, 0, 0, 0, 1
        };

        public static RosMessageTypes.Geometry.Vector3Msg GetGeometryVector3(Vector3<FLU> vector3)
        {
            RosMessageTypes.Geometry.Vector3Msg geometryVector3 = new RosMessageTypes.Geometry.Vector3Msg();
            geometryVector3.x = vector3.x;
            geometryVector3.y = vector3.y;
            geometryVector3.z = vector3.z;
            return geometryVector3;
        }
        public static RosMessageTypes.Geometry.Vector3Msg GetGeometryVector3(Vector3 vector3)
        {
            RosMessageTypes.Geometry.Vector3Msg geometryVector3 = new RosMessageTypes.Geometry.Vector3Msg();
            geometryVector3.x = vector3.x;
            geometryVector3.y = vector3.y;
            geometryVector3.z = vector3.z;
            return geometryVector3;
        }

        public static RosMessageTypes.Geometry.PointMsg GetGeometryPoint(Vector3<FLU> position)
        {
            RosMessageTypes.Geometry.PointMsg geometryPoint = new RosMessageTypes.Geometry.PointMsg();
            geometryPoint.x = position.x;
            geometryPoint.y = position.y;
            geometryPoint.z = position.z;
            return geometryPoint;
        }
        public static RosMessageTypes.Geometry.PointMsg GetGeometryPoint(Vector3 position)
        {
            RosMessageTypes.Geometry.PointMsg geometryPoint = new RosMessageTypes.Geometry.PointMsg();
            geometryPoint.x = position.x;
            geometryPoint.y = position.y;
            geometryPoint.z = position.z;
            return geometryPoint;
        }

        public static RosMessageTypes.Geometry.QuaternionMsg GetGeometryQuaternion(Quaternion<FLU> quaternion)
        {
            RosMessageTypes.Geometry.QuaternionMsg geometryQuaternion = new RosMessageTypes.Geometry.QuaternionMsg();
            geometryQuaternion.x = quaternion.x;
            geometryQuaternion.y = quaternion.y;
            geometryQuaternion.z = quaternion.z;
            geometryQuaternion.w = quaternion.w;
            return geometryQuaternion;
        }
        public static RosMessageTypes.Geometry.QuaternionMsg GetGeometryQuaternion(Quaternion quaternion)
        {
            RosMessageTypes.Geometry.QuaternionMsg geometryQuaternion = new RosMessageTypes.Geometry.QuaternionMsg();
            geometryQuaternion.x = quaternion.x;
            geometryQuaternion.y = quaternion.y;
            geometryQuaternion.z = quaternion.z;
            geometryQuaternion.w = quaternion.w;
            return geometryQuaternion;
        }


        public static RosMessageTypes.Geometry.PoseMsg GetMPose(Vector3 position, Quaternion rotation)
        {
            return GetMPose(new Pose(position, rotation));
        }
        public static RosMessageTypes.Geometry.PoseMsg GetMPose(GameObject gameObject)
        {
            return GetMPose(new Pose(gameObject.transform.position, gameObject.transform.rotation));
        }

        public static RosMessageTypes.Geometry.PoseMsg GetMPose(Transform transform)
        {
            return GetMPose(new Pose(transform.position, transform.rotation));
        }

        public static RosMessageTypes.Geometry.PoseMsg GetMPose(Pose pose)
        {
            RosMessageTypes.Geometry.PoseMsg mpose = new RosMessageTypes.Geometry.PoseMsg();
            mpose.position = GetGeometryPoint(pose.position.To<FLU>());
            mpose.orientation = GetGeometryQuaternion(pose.rotation.To<FLU>());

            // Debug.Log(pose.position.To<FLU>());
            // Debug.Log(pose);

            return mpose;
        }

        public static RosMessageTypes.Geometry.PoseStampedMsg GetMPoseStamped(Vector3 position, Quaternion rotation)
        {
            return GetMPoseStamped(new Pose(position, rotation));
        }
        public static RosMessageTypes.Geometry.PoseStampedMsg GetMPoseStamped(Vector3 position, Quaternion rotation, string frameId)
        {
            return GetMPoseStamped(new Pose(position, rotation), frameId);
        }

        public static RosMessageTypes.Geometry.PoseStampedMsg GetMPoseStamped(GameObject gameObject)
        {
            return GetMPoseStamped(new Pose(gameObject.transform.position, gameObject.transform.rotation));
        }
        public static RosMessageTypes.Geometry.PoseStampedMsg GetMPoseStamped(GameObject gameObject, string frameId)
        {
            return GetMPoseStamped(new Pose(gameObject.transform.position, gameObject.transform.rotation), frameId);
        }

        public static RosMessageTypes.Geometry.PoseStampedMsg GetMPoseStamped(Transform transform)
        {
            return GetMPoseStamped(new Pose(transform.position, transform.rotation));
        }
        public static RosMessageTypes.Geometry.PoseStampedMsg GetMPoseStamped(Transform transform, string frameId)
        {
            return GetMPoseStamped(new Pose(transform.position, transform.rotation), frameId);
        }

        public static RosMessageTypes.Geometry.PoseStampedMsg GetMPoseStamped(Pose pose)
        {
            return GetMPoseStamped(pose, "");
        }
        public static RosMessageTypes.Geometry.PoseStampedMsg GetMPoseStamped(Pose pose, string frameId)
        {
            RosMessageTypes.Geometry.PoseStampedMsg mpose = new RosMessageTypes.Geometry.PoseStampedMsg();
            mpose.header = GetHeader(frameId);
            mpose.pose.position = GetGeometryPoint(pose.position.To<FLU>());
            mpose.pose.orientation = GetGeometryQuaternion(pose.rotation.To<FLU>());
            return mpose;
        }


        public static RosMessageTypes.Geometry.PoseWithCovarianceStampedMsg GetMPoseWithCovarianceStamped(Vector3 position, Quaternion rotation)
        {
            return GetMPoseWithCovarianceStamped(new Pose(position, rotation));
        }
        public static RosMessageTypes.Geometry.PoseWithCovarianceStampedMsg GetMPoseWithCovarianceStamped(Vector3 position, Quaternion rotation, string frameId)
        {
            return GetMPoseWithCovarianceStamped(new Pose(position, rotation), frameId);
        }
        public static RosMessageTypes.Geometry.PoseWithCovarianceStampedMsg GetMPoseWithCovarianceStamped(GameObject gameObject)
        {
            return GetMPoseWithCovarianceStamped(new Pose(gameObject.transform.position, gameObject.transform.rotation));
        }
        public static RosMessageTypes.Geometry.PoseWithCovarianceStampedMsg GetMPoseWithCovarianceStamped(GameObject gameObject, string frameId)
        {
            return GetMPoseWithCovarianceStamped(new Pose(gameObject.transform.position, gameObject.transform.rotation), frameId);
        }

        public static RosMessageTypes.Geometry.PoseWithCovarianceStampedMsg GetMPoseWithCovarianceStamped(Transform transform)
        {
            return GetMPoseWithCovarianceStamped(new Pose(transform.position, transform.rotation));
        }
        public static RosMessageTypes.Geometry.PoseWithCovarianceStampedMsg GetMPoseWithCovarianceStamped(Transform transform, string frameId)
        {
            return GetMPoseWithCovarianceStamped(new Pose(transform.position, transform.rotation), frameId);
        }

        public static RosMessageTypes.Geometry.PoseWithCovarianceStampedMsg GetMPoseWithCovarianceStamped(Pose pose)
        {
            return GetMPoseWithCovarianceStamped(pose, "");
        }
        public static RosMessageTypes.Geometry.PoseWithCovarianceStampedMsg GetMPoseWithCovarianceStamped(Pose pose, string frameId)
        {
            RosMessageTypes.Geometry.PoseWithCovarianceStampedMsg mpose = new RosMessageTypes.Geometry.PoseWithCovarianceStampedMsg();
            mpose.header = GetHeader(frameId);
            mpose.pose.pose.position = GetGeometryPoint(pose.position.To<FLU>());
            mpose.pose.pose.orientation = GetGeometryQuaternion(pose.rotation.To<FLU>());
            mpose.pose.covariance = identityCovariance;
            return mpose;
        }


        public static RosMessageTypes.Geometry.TwistMsg GetMTwist(Vector3 trajectory)
        {
            RosMessageTypes.Geometry.TwistMsg mtwist = new RosMessageTypes.Geometry.TwistMsg();
            mtwist.linear = new RosMessageTypes.Geometry.Vector3Msg(trajectory.z, -trajectory.x, trajectory.y);
            mtwist.angular = new RosMessageTypes.Geometry.Vector3Msg(0, 0, 0);
            return mtwist;

        }

        public static RosMessageTypes.Geometry.PoseMsg GetMapOriginPose(Vector2 center, float resolution, int width, int height)
        {
            // Calcule le coin inférieur gauche dans le plan XZ Unity
            float originX = center.x - (width * resolution) / 2.0f;
            float originZ = center.y - (height * resolution) / 2.0f;

            // Position dans Unity
            Vector3<FLU> unityOrigin = new Vector3(-originX, 0, originZ).To<FLU>();


            // Orientation (aucune rotation)
            Quaternion rotENU = Quaternion.Euler(0f, 0f, 0f);
            Quaternion<FLU> rotFLU = rotENU.To<FLU>();

            // if (rotFLU.w < 0)
            // {
            //     rotFLU.x = -rotFLU.x;
            //     rotFLU.y = -rotFLU.y;
            //     rotFLU.z = -rotFLU.z;
            //     rotFLU.w = -rotFLU.w;
            // }

            return new RosMessageTypes.Geometry.PoseMsg
            {
                position = new RosMessageTypes.Geometry.PointMsg(unityOrigin.x, unityOrigin.y, unityOrigin.z),
                orientation = new RosMessageTypes.Geometry.QuaternionMsg(rotFLU.x, rotFLU.y, rotFLU.z, rotFLU.w)
            };
        }

        public static float GroundPlaneDist(Vector3 a, Vector3 b)
        {
            Vector3 diff = a - b;
            diff.y = 0;
            return diff.magnitude;
        }

        public static Vector3 Tangent(Vector3 a)
        {
            return new Vector3(-a.z, 0, a.x);
        }

        public static HeaderMsg GetHeader(string frameId)
        {
            return new HeaderMsg
            {
                frame_id = frameId.TrimStart('/'),
                stamp = ROS_DRL.Supervisor.instance.clock.Now()
            };
        }
    }
}
