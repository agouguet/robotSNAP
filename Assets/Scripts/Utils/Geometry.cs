// Copyright (c) 2021, Members of Yale Interactive Machines Group, Yale University,
// Nathan Tsoi
// All rights reserved.
// This source code is licensed under the BSD-style license found in the
// LICENSE file in the root directory of this source tree. 

using System.Collections.Generic;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.Std;
using RobotSNAP.Core;
using RobotSNAP.ROS;

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

        #region Vector3 Conversions

        public static RosMessageTypes.Geometry.Vector3Msg GetGeometryVector3(Vector3<FLU> vector3)
        {
            return new RosMessageTypes.Geometry.Vector3Msg
            {
                x = vector3.x,
                y = vector3.y,
                z = vector3.z
            };
        }
        
        public static RosMessageTypes.Geometry.Vector3Msg GetGeometryVector3(Vector3 vector3)
        {
            return new RosMessageTypes.Geometry.Vector3Msg
            {
                x = vector3.x,
                y = vector3.y,
                z = vector3.z
            };
        }

        #endregion

        #region Point Conversions

        public static RosMessageTypes.Geometry.PointMsg GetGeometryPoint(Vector3<FLU> position)
        {
            return new RosMessageTypes.Geometry.PointMsg
            {
                x = position.x,
                y = position.y,
                z = position.z
            };
        }
        
        public static RosMessageTypes.Geometry.PointMsg GetGeometryPoint(Vector3 position)
        {
            return new RosMessageTypes.Geometry.PointMsg
            {
                x = position.x,
                y = position.y,
                z = position.z
            };
        }

        #endregion

        #region Quaternion Conversions

        public static RosMessageTypes.Geometry.QuaternionMsg GetGeometryQuaternion(Quaternion<FLU> quaternion)
        {
            return new RosMessageTypes.Geometry.QuaternionMsg
            {
                x = quaternion.x,
                y = quaternion.y,
                z = quaternion.z,
                w = quaternion.w
            };
        }
        
        public static RosMessageTypes.Geometry.QuaternionMsg GetGeometryQuaternion(Quaternion quaternion)
        {
            return new RosMessageTypes.Geometry.QuaternionMsg
            {
                x = quaternion.x,
                y = quaternion.y,
                z = quaternion.z,
                w = quaternion.w
            };
        }

        #endregion

        #region Pose Conversions

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
            return new RosMessageTypes.Geometry.PoseMsg
            {
                position = GetGeometryPoint(pose.position.To<FLU>()),
                orientation = GetGeometryQuaternion(pose.rotation.To<FLU>())
            };
        }

        #endregion

        #region PoseStamped Conversions

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
            return new RosMessageTypes.Geometry.PoseStampedMsg
            {
                header = GetHeader(frameId),
                pose = new RosMessageTypes.Geometry.PoseMsg
                {
                    position = GetGeometryPoint(pose.position.To<FLU>()),
                    orientation = GetGeometryQuaternion(pose.rotation.To<FLU>())
                }
            };
        }

        #endregion

        #region PoseWithCovarianceStamped Conversions

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
            return new RosMessageTypes.Geometry.PoseWithCovarianceStampedMsg
            {
                header = GetHeader(frameId),
                pose = new RosMessageTypes.Geometry.PoseWithCovarianceMsg
                {
                    pose = new RosMessageTypes.Geometry.PoseMsg
                    {
                        position = GetGeometryPoint(pose.position.To<FLU>()),
                        orientation = GetGeometryQuaternion(pose.rotation.To<FLU>())
                    },
                    covariance = identityCovariance
                }
            };
        }

        #endregion

        #region Twist Conversions

        public static RosMessageTypes.Geometry.TwistMsg GetMTwist(Vector3 trajectory)
        {
            return new RosMessageTypes.Geometry.TwistMsg
            {
                linear = new RosMessageTypes.Geometry.Vector3Msg(trajectory.z, -trajectory.x, trajectory.y),
                angular = new RosMessageTypes.Geometry.Vector3Msg(0, 0, 0)
            };
        }

        #endregion

        #region Map Origin

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

            return new RosMessageTypes.Geometry.PoseMsg
            {
                position = new RosMessageTypes.Geometry.PointMsg(unityOrigin.x, unityOrigin.y, unityOrigin.z),
                orientation = new RosMessageTypes.Geometry.QuaternionMsg(rotFLU.x, rotFLU.y, rotFLU.z, rotFLU.w)
            };
        }

        #endregion

        #region Utility Methods

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
                stamp = ROSTimeUtils.Now()  // Utilise ROSTimeUtils au lieu de Supervisor.Instance.clock.Now()
            };
        }

        #endregion
    }
}