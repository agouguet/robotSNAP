using UnityEngine;
using System;
using System.Collections.Generic;
using RobotSNAP.Core;

namespace RobotSNAP.Agents
{
    public class Robot : BaseAgent
    {
        [Header("Robot Specific")]
        [SerializeField] private float maxLinearSpeed = 1.0f;
        [SerializeField] private float maxAngularSpeed = 2.0f;
        [SerializeField] private GameObject baseLink;
        [SerializeField] private AgentDetector detector;
        [SerializeField] private ArticulationWheelController wheelController;
        [SerializeField] private bool keepParentAtOrigin = true;

        private float _targetLinearSpeed;
        private float _targetAngularSpeed;
        private ArticulationBody _baseLinkArticulation;
        private Supervisor _supervisor;
        private readonly Queue<Vector3> _routeGoals = new();
        private bool _followingRoute;

        // Geometry of the wheels as the prefab authored them, kept so a second call to ApplyProfile scales
        // from the prefab and not from the previous type.
        private float _baseWheelTrackLength;
        private float _baseWheelRadius;
        private bool _wheelGeometryCaptured;

        public event Action<float, float> OnVelocityCommandReceived;
        public event Action<Vector3, Quaternion> OnMovementUpdated;

        // Propriétés héritées de BaseAgent (implémentation)
        public override Vector3 Position => baseLink != null ? baseLink.transform.position : transform.position;
        public override Quaternion Rotation => baseLink != null ? baseLink.transform.rotation : transform.rotation;
        public override Vector3 Forward => Rotation * Vector3.forward;
        public override Vector3 Velocity => _baseLinkArticulation != null ? _baseLinkArticulation.linearVelocity : Vector3.zero;
        public override float AngularSpeed => _baseLinkArticulation != null ? _baseLinkArticulation.angularVelocity.y : 0f;

        public Transform RobotTransform => baseLink != null ? baseLink.transform : transform;

        /// <summary>The type this robot drives as, or the default one before a roster applied one.</summary>
        public RobotProfile Profile { get; private set; }

        /// <summary>The body picture of this robot's type, or null when it drives as the prefab it came from.</summary>
        public GameObject Visual { get; private set; }

        // ==================== Unity Lifecycle ====================
        private void Awake()
        {
            _supervisor = Supervisor.Instance;
            EnsureComponents();
            EnforceParentOrigin();
        }

        private void Start() => EnforceParentOrigin();

        private void FixedUpdate()
        {
            if (wheelController != null && (_supervisor ??= Supervisor.Instance) != null && !_supervisor.IsPaused)
                wheelController.SetRobotVelocity(_targetLinearSpeed, _targetAngularSpeed);

            if (_hasGoal)
                UpdateScenarioMovement();

            SyncVisual();
            OnMovementUpdated?.Invoke(Position, Rotation);
        }

        // ==================== Initialization ====================
        private void EnsureComponents()
        {
            // Find baseLink if not assigned
            if (baseLink == null)
            {
                var child = GetComponentInChildren<ArticulationBody>();
                if (child != null)
                    baseLink = child.gameObject;
                else
                    baseLink = gameObject;
            }

            // Get ArticulationBody on baseLink
            _baseLinkArticulation = baseLink.GetComponent<ArticulationBody>();
            if (_baseLinkArticulation == null)
                Debug.LogWarning($"[Robot] baseLink {baseLink.name} has no ArticulationBody. Velocity will be zero.");

            // Find wheel controller if not assigned
            if (wheelController == null)
            {
                wheelController = GetComponent<ArticulationWheelController>();
                if (wheelController == null)
                    wheelController = GetComponentInChildren<ArticulationWheelController>();
                if (wheelController == null)
                    Debug.LogWarning($"[Robot] No ArticulationWheelController found on {name}");
            }

            if (detector == null)
            {
                detector = GetComponentInChildren<AgentDetector>();
                if (detector == null)
                    Debug.LogWarning($"[Robot] No AgentDetector found on {name}");
            }
        }

        private void EnforceParentOrigin()
        {
            if (keepParentAtOrigin)
            {
                Debug.LogWarning($"[Robot] Enforcing parent origin for {name}. Parent will be reset to (0,0,0).");
                transform.position = Vector3.zero;
                transform.rotation = Quaternion.identity;
            }
        }

        // ==================== Robot Type ====================

        /// <summary>
        /// Makes this instance drive as <paramref name="profile"/>: its footprint, its mass, the speeds it
        /// may be commanded, its body size, and the shape of its lidar.
        ///
        /// It is meant to run once, on an instance the roster has just created and before the components of
        /// the prefab had their first frame - the scanner rebuilds its rays here, and the publisher of the
        /// scan reads the rate at its own start.
        ///
        /// The footprint is what the rest of the application reads: the crowd's social force model gives a
        /// Husky a wider berth than a TurtleBot, and the spawn check uses the same number to decide whether
        /// two robots may be placed side by side.
        /// </summary>
        public void ApplyProfile(RobotProfile profile)
        {
            if (profile == null)
                return;

            Profile = profile;

            maxLinearSpeed = Mathf.Max(0.01f, profile.MaxLinearSpeed);
            maxAngularSpeed = Mathf.Max(0.01f, profile.MaxAngularSpeed);
            SetRadius(profile.Radius);
            SetMass(profile.Mass);

            ApplyBodyScale(profile.BodyScale);
            ApplyWheelGeometry();
            ApplyLidar(profile);
            ApplyBodyColor(profile.BodyColor);
        }

        /// <summary>
        /// Gives this robot the body of its type.
        ///
        /// The body is a picture and nothing else: the articulation, the wheels, the sensors and the streams
        /// stay the ones the prefab was built and proven with, so a scenario that drives a Husky drives
        /// exactly as well as one that drives the base - it simply looks like a Husky. The picture is dropped
        /// on the ground the robot stands on rather than on the base itself, whose origin sits above the
        /// wheels, so a body authored from y = 0 upwards lands with its wheels on the floor.
        /// </summary>
        public void ApplyVisual(GameObject prefab)
        {
            if (prefab == null || Visual != null)
                return;

            Visual = Instantiate(prefab, RobotTransform);
            Visual.name = "Body";
            SyncVisual();

            // The picture replaces the one of the prefab instead of doubling it: the two would otherwise
            // stand in the same place, and the crowd would see both.
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.transform.IsChildOf(Visual.transform))
                    continue;

                renderer.enabled = false;
            }
        }

        /// <summary>
        /// Keeps the body under the base it belongs to: it follows where the robot drives and which way it
        /// faces, and it stays on the plane the robot stands on.
        ///
        /// The body is not simply parented and left alone because the origin of a mobile base sits above its
        /// wheels, and the base moves up and down as the physics settles it: a body pinned to that origin
        /// would float or sink by however much the articulation happened to have risen when it was built.
        /// The ground of this project is the plane y = 0, which is also where the root of every robot is
        /// pinned.
        /// </summary>
        private void SyncVisual()
        {
            if (Visual == null)
                return;

            Transform reference = RobotTransform;
            if (reference == null)
                return;

            Vector3 position = reference.position;
            Visual.transform.SetPositionAndRotation(
                new Vector3(position.x, 0f, position.z),
                Quaternion.Euler(0f, reference.eulerAngles.y, 0f));
        }

        private void ApplyBodyScale(float scale)
        {
            // A robot that was given the body of its type already looks like that type: resizing the picture
            // underneath it would move a wheel off its own rim.
            if (Visual != null)
                return;

            // Only the picture is resized, never the body. An articulation that is scaled keeps the joints it
            // was built with, its collision volume stops matching the space the planner reserved for it, and a
            // base teleported to the ground ends up buried in it. The bodies of every type therefore stay the
            // ones the prefab was authored and proven with, and the type is what a person sees.
            if (!Mathf.Approximately(scale, 1f) && scale > 0f)
            {
                var nodes = new List<Transform>();
                CollectVisualRoots(nodes);
                foreach (Transform node in nodes)
                    ScaleVisual(node, scale);
            }

            if (_baseLinkArticulation != null)
                _baseLinkArticulation.mass = Profile != null ? Profile.Mass : _baseLinkArticulation.mass;
        }

        /// <summary>
        /// The outermost nodes of the robot that carry a picture, so a body is scaled once rather than once
        /// per mesh under it, and a node that also carries a collider is left alone: that one is not a
        /// picture, it is the shape the physics reacts to.
        /// </summary>
        private void CollectVisualRoots(List<Transform> destination)
        {
            destination.Clear();

            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled)
                    continue;

                Transform node = renderer.transform;
                if (node == null || node == transform || node.GetComponent<Collider>() != null)
                    continue;

                bool nested = false;
                for (Transform parent = node.parent; parent != null && parent != transform; parent = parent.parent)
                {
                    if (HasRenderer(parent.gameObject))
                    {
                        nested = true;
                        break;
                    }
                }

                if (!nested && !destination.Contains(node))
                    destination.Add(node);
            }
        }

        private static bool HasRenderer(GameObject candidate)
        {
            foreach (Renderer renderer in candidate.GetComponents<Renderer>())
            {
                if (renderer != null && renderer.enabled)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Resizes one picture around the ground it stands on: a larger robot grows upwards rather than
        /// sinking into the floor, so whatever the type its wheels keep touching the same plane.
        /// </summary>
        private static void ScaleVisual(Transform node, float scale)
        {
            if (node == null)
                return;

            float bottomBefore = LowestPoint(node);
            node.localScale *= scale;
            float bottomAfter = LowestPoint(node);

            if (float.IsInfinity(bottomBefore) || float.IsInfinity(bottomAfter))
                return;

            node.position += Vector3.up * (bottomBefore - bottomAfter);
        }

        private static float LowestPoint(Transform node)
        {
            float lowest = float.PositiveInfinity;
            foreach (Renderer renderer in node.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled)
                    continue;

                lowest = Mathf.Min(lowest, renderer.bounds.min.y);
            }

            return lowest;
        }

        private void ApplyWheelGeometry()
        {
            if (wheelController == null)
                return;

            if (!_wheelGeometryCaptured)
            {
                _baseWheelTrackLength = wheelController.wheelTrackLength;
                _baseWheelRadius = wheelController.wheelRadius;
                _wheelGeometryCaptured = true;
            }

            // The wheels keep the size the prefab gave them, because only the picture of the robot is
            // resized: the metres the controller converts into wheel rotations stay true to the geometry the
            // articulation actually drives, whatever the type looks like.
            wheelController.wheelTrackLength = _baseWheelTrackLength;
            wheelController.wheelRadius = _baseWheelRadius;
        }

        private void ApplyLidar(RobotProfile profile)
        {
            RaycastLaserScanner scanner = GetLaserScanner();
            if (scanner != null)
            {
                scanner.samples = profile.LidarRays;
                scanner.angle_min = profile.LidarAngleMin;
                scanner.angle_max = profile.LidarAngleMax;
                scanner.range_max = profile.LidarRange;

                // The height of the laser plane is what decides whether a wall, a table or a pedestrian
                // blocks the beam, so the profile gives the height above the ground and the offset the
                // scanner carries is what remains once its mount has been taken into account.
                float mountHeight = scanner.transform.position.y - Position.y;
                scanner.laserHeight = Mathf.Max(0f, profile.LidarHeight - mountHeight);

                // Rebuilds the rays and the buffers the scan writes into.
                scanner.Init();
            }

            LaserScanPublisher publisher = GetLaserPublisher();
            if (publisher != null)
                publisher.SetPublishFrequency(profile.LidarFrequencyHz);
        }

        private void ApplyBodyColor(Color color)
        {
            // A clear colour means "leave the materials of the prefab alone", which is what the legacy
            // default asks for so an existing scenario keeps the exact look it was authored against.
            if (color.a <= 0.01f)
                return;

            var block = new MaterialPropertyBlock();
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;

                // A body of its own already carries the colours of its type, trim and sensor included: tinting
                // it here would flatten the whole robot into one flat plate of the profile colour.
                if (Visual != null && renderer.transform.IsChildOf(Visual.transform))
                    continue;

                renderer.GetPropertyBlock(block);
                // Both names are written because the project renders through HDRP and the tests run on the
                // built-in pipeline: whichever material is in use finds its own property.
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                renderer.SetPropertyBlock(block);
            }
        }

        // ==================== Scenario Movement ====================
        private void UpdateScenarioMovement()
        {
            Vector3 direction = (_currentGoal - Position).normalized;
            float distance = Vector3.Distance(Position, _currentGoal);

            float targetSpeed = _currentSpeed;
            if (distance < 1.0f)
                targetSpeed = _currentSpeed * (distance / 1.0f);

            float angleToGoal = Vector3.SignedAngle(Forward, direction, Vector3.up);
            float angularSpeed = Mathf.Clamp(angleToGoal * 2.0f, -maxAngularSpeed, maxAngularSpeed);

            SetVelocity(targetSpeed, angularSpeed);

            if (distance < 0.2f)
            {
                if (_followingRoute && _routeGoals.Count > 0)
                {
                    SetNextRouteGoal();
                }
                else
                {
                    _followingRoute = false;
                    _hasGoal = false;
                    Stop();
                }
            }
        }

        public override void SetGoal(Vector3 goal)
        {
            _routeGoals.Clear();
            _followingRoute = false;
            base.SetGoal(goal);
        }

        public void SetGoals(IEnumerable<Vector3> goals)
        {
            _routeGoals.Clear();
            if (goals != null)
            {
                foreach (Vector3 goal in goals)
                    _routeGoals.Enqueue(goal);
            }

            _followingRoute = _routeGoals.Count > 0;
            if (_followingRoute)
                SetNextRouteGoal();
            else
                ClearGoal();
        }

        public override void ClearGoal()
        {
            _routeGoals.Clear();
            _followingRoute = false;
            base.ClearGoal();
        }

        private void SetNextRouteGoal()
        {
            if (_routeGoals.Count == 0)
                return;
            base.SetGoal(_routeGoals.Dequeue());
        }

        // ==================== Public API - Movement Control ====================
        public void SetVelocity(float linearSpeed, float angularSpeed)
        {
            _targetLinearSpeed = Mathf.Clamp(linearSpeed, -maxLinearSpeed, maxLinearSpeed);
            _targetAngularSpeed = Mathf.Clamp(angularSpeed, -maxAngularSpeed, maxAngularSpeed);
            OnVelocityCommandReceived?.Invoke(_targetLinearSpeed, _targetAngularSpeed);
        }

        public override void Stop()
        {
            _targetLinearSpeed = 0f;
            _targetAngularSpeed = 0f;
            if (wheelController != null)
                wheelController.SetRobotVelocity(0f, 0f);
        }

        public override void Reset()
        {
            Stop();
            _routeGoals.Clear();
            _followingRoute = false;
            ClearGoal();
            EnforceParentOrigin();
            if (wheelController != null)
                wheelController.ResetDrives();
        }

        // ==================== Public API - Teleportation ====================
        public void SetBaseLinkPose(Vector3 position, Quaternion rotation)
        {
            if (baseLink == null)
            {
                Debug.LogError("[Robot] baseLink is null, cannot set pose.");
                return;
            }
            ArticulationBody ab = baseLink.GetComponent<ArticulationBody>();
            if (ab == null)
            {
                Debug.LogError("[Robot] baseLink has no ArticulationBody, cannot set pose.");
                return;
            }
            ab.TeleportRoot(position, rotation);
            // A teleported articulation is settled where it was put, and a drive command on a body that is
            // asleep is ignored: the robot of a scenario would sit at its start with its wheels commanded.
            ab.WakeUp();
            Stop();
            EnforceParentOrigin();
        }

        public void SetBaseLinkPosition(Vector3 position)
        {
            if (baseLink == null) return;
            Debug.Log($"[Robot] Teleporting baseLink to {position}");
            ArticulationBody ab = baseLink.GetComponent<ArticulationBody>();
            if (ab == null)
            {
                Debug.LogError("[Robot] baseLink has no ArticulationBody, cannot set position.");
                return;
            }
            ab.TeleportRoot(position, baseLink.transform.rotation);
            ab.WakeUp();
            Stop();
            EnforceParentOrigin();
        }

        public void SetBaseLinkRotation(Quaternion rotation)
        {
            if (baseLink == null) return;
            ArticulationBody ab = baseLink.GetComponent<ArticulationBody>();
            if (ab == null)
            {
                Debug.LogError("[Robot] baseLink has no ArticulationBody, cannot set rotation.");
                return;
            }
            ab.TeleportRoot(baseLink.transform.position, rotation);
            ab.WakeUp();
            Stop();
            EnforceParentOrigin();
        }

        public void SetPose(Vector3 position, Quaternion rotation) => SetBaseLinkPose(position, rotation);

        // ==================== IAgent Methods Overrides ====================
        public override void SetBehavior(string behavior)
        {
            base.SetBehavior(behavior); // met à jour _currentBehavior
            switch (behavior.ToLower())
            {
                case "cautious":
                    maxLinearSpeed = 0.8f;
                    break;
                case "assertive":
                    maxLinearSpeed = 1.5f;
                    break;
                case "socially_aware":
                    maxLinearSpeed = 1.2f;
                    break;
                default:
                    maxLinearSpeed = 1.2f;
                    break;
            }
        }

        // ==================== Sensors ====================
        public void SetLaserSample(int laserSample)
        {
            var laser = GetComponentInChildren<RaycastLaserScanner>();
            if (laser != null)
            {
                laser.samples = laserSample;
                laser.Init();
            }
        }

        public RaycastLaserScanner GetLaserScanner() => GetComponentInChildren<RaycastLaserScanner>();
        public LaserScanPublisher GetLaserPublisher() => GetComponentInChildren<LaserScanPublisher>();
        public AgentDetector GetAgentDetector() => detector;

        // ==================== Editor Utilities ====================
        [ContextMenu("Stop")]
        private void EditorStop() => Stop();

        [ContextMenu("Reset")]
        private void EditorReset() => Reset();

        [ContextMenu("Set Test Goal")]
        private void EditorSetTestGoal() => SetGoal(new Vector3(5, 0, 5));

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(Position, radius);
            if (_hasGoal && Application.isPlaying)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(Position, _currentGoal);
                Gizmos.DrawWireSphere(_currentGoal, 0.2f);
            }
        }

        public override string ToString()
        {
            return $"{gameObject.name} (Behavior: {_currentBehavior}, Speed: {_currentSpeed}, Goal: {(_hasGoal ? _currentGoal.ToString() : "none")})";
        }
    }
}
