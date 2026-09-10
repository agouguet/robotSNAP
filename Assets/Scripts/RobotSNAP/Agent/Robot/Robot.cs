using UnityEngine;
using System;
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

        public event Action<float, float> OnVelocityCommandReceived;
        public event Action<Vector3, Quaternion> OnMovementUpdated;

        // Propriétés héritées de BaseAgent (implémentation)
        public override Vector3 Position => baseLink != null ? baseLink.transform.position : transform.position;
        public override Quaternion Rotation => baseLink != null ? baseLink.transform.rotation : transform.rotation;
        public override Vector3 Forward => Rotation * Vector3.forward;
        public override Vector3 Velocity => _baseLinkArticulation != null ? _baseLinkArticulation.linearVelocity : Vector3.zero;
        public override float AngularSpeed => _baseLinkArticulation != null ? _baseLinkArticulation.angularVelocity.y : 0f;

        public Transform RobotTransform => baseLink != null ? baseLink.transform : transform;

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
                _hasGoal = false;
                Stop();
            }
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
