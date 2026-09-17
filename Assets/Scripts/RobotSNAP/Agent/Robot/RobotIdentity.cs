using System.Collections.Generic;
using UnityEngine;
using RobotSNAP.ROS;

namespace RobotSNAP.Agents
{
    /// <summary>
    /// Which robot of the scenario an instance is: its id, whether it is the one a client reaches without
    /// naming anybody, and the type it drives as.
    ///
    /// It is also the one place a stream of one robot is named, because a topic is the only thing that has
    /// to change when a second robot appears. The rule is short:
    ///
    ///   * robot 1 keeps the names the project has always published - <c>/odom</c>, <c>/scan</c>,
    ///     <c>/cmd_vel</c> - and also answers on <c>/robot_1/...</c>, so a client that addresses robots by
    ///     id drives the first one the same way it drives the others;
    ///   * every other robot publishes and listens only under its own id: <c>/robot_2/scan</c>.
    ///
    /// Nothing else about a robot changes with its rank in the scenario, so the order of the list is not a
    /// hidden setting: it only decides which robot an old client is talking to.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotIdentity : MonoBehaviour
    {
        [SerializeField] private string _id = "robot_1";
        [SerializeField] private bool _isPrimary = true;

        /// <summary>Id of this robot inside the scenario, such as <c>robot_1</c>.</summary>
        public string Id => _id;

        /// <summary>True for the robot a client reaches without naming one.</summary>
        public bool IsPrimary => _isPrimary;

        /// <summary>The type this robot drives as, or null before the roster binds one.</summary>
        public RobotProfile Profile { get; private set; }

        /// <summary>Id of the type, or the default type before a roster bound one.</summary>
        public string TypeId => Profile != null ? Profile.Id : RobotProfiles.DefaultId;

        /// <summary>The segment a namespaced topic is built from.</summary>
        public string Segment => RobotSNAPTopics.Normalize(_id);

        /// <summary>Binds the identity once, from the roster that owns the instance.</summary>
        public void Bind(string id, bool isPrimary, RobotProfile profile)
        {
            _id = string.IsNullOrWhiteSpace(id) ? "robot_1" : id.Trim();
            _isPrimary = isPrimary;
            Profile = profile;
        }

        /// <summary>
        /// The identity of the robot a component belongs to, found up the hierarchy because the sensors and
        /// the publishers live on the links under the root of the prefab.
        /// </summary>
        public static RobotIdentity Of(Component owner)
        {
            return owner == null ? null : owner.GetComponentInParent<RobotIdentity>();
        }

        /// <summary>
        /// Every name a stream of this robot answers on, in the order they are registered: the legacy name
        /// of the first robot first, then the namespaced one. A robot that is not the first has a single
        /// name, so the list costs nothing in a crowded scenario.
        /// </summary>
        public void FillStreamNames(string baseTopic, string envPrefix, List<string> destination)
        {
            destination.Clear();

            string namespaced = RobotSNAPTopics.Full(Segment + "/" + RobotSNAPTopics.Normalize(baseTopic), envPrefix);
            if (!string.IsNullOrEmpty(namespaced))
                destination.Add(namespaced);

            if (!_isPrimary)
                return;

            string legacy = RobotSNAPTopics.Full(baseTopic, envPrefix);
            if (!string.IsNullOrEmpty(legacy) && !destination.Contains(legacy))
                destination.Insert(0, legacy);
        }

        /// <summary>Same names as <see cref="FillStreamNames"/>, for a caller that wants its own array.</summary>
        public string[] StreamNames(string baseTopic, string envPrefix)
        {
            var names = new List<string>(2);
            FillStreamNames(baseTopic, envPrefix, names);
            return names.ToArray();
        }

        /// <summary>
        /// Every stream of this robot at a given prefix, for a component that registered a fixed set of
        /// topics before identities existed: with no identity it is the one legacy name, unchanged.
        /// </summary>
        public static string[] StreamNamesFor(Component owner, string baseTopic, string envPrefix)
        {
            RobotIdentity identity = Of(owner);
            return identity != null
                ? identity.StreamNames(baseTopic, envPrefix)
                : new[] { RobotSNAPTopics.Full(baseTopic, envPrefix) };
        }

        /// <summary>
        /// The frame id of a stream of this robot: the legacy frame for the first robot, and the frame under
        /// the id of the robot for the others, so a client can tell whose <c>base_link</c> it is reading.
        /// </summary>
        public static string FrameIdFor(Component owner, string frameId, string envPrefix)
        {
            RobotIdentity identity = Of(owner);
            return identity != null
                ? identity.FrameId(frameId, envPrefix)
                : RobotSNAPTopics.Full(frameId, envPrefix);
        }

        /// <summary>The single name a frame id is built from: the legacy one for the first robot.</summary>
        public string FrameId(string frameId, string envPrefix)
        {
            string name = _isPrimary
                ? RobotSNAPTopics.Full(frameId, envPrefix)
                : RobotSNAPTopics.Full(Segment + "/" + RobotSNAPTopics.Normalize(frameId), envPrefix);

            return name ?? frameId;
        }

        /// <summary>Name a person reads in a list: "TurtleBot 4 (robot_2)".</summary>
        public string DisplayName => Profile != null ? $"{Profile.DisplayName} ({_id})" : _id;

        public override string ToString() => DisplayName;
    }
}
