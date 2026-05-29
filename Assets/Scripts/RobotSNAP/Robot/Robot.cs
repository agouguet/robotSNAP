using UnityEngine;
using System;

namespace RobotSNAP
{
    /// <summary>
    /// Core robot controller - No ROS dependencies.
    /// Handles movement, sensors, and state.
    /// The parent GameObject (Robot) remains at (0,0,0) – only the baseLink moves.
    /// </summary>
    public class Robot : MonoBehaviour
    {
        [Header("Identification")]
        [SerializeField] public string robotName;

        [Header("Movement")]
        [SerializeField] private float maxLinearSpeed = 1.0f;
        [SerializeField] private float maxAngularSpeed = 2.0f;
        [SerializeField] private float acceleration = 2.0f;
        [SerializeField] private float deceleration = 3.0f;
        
        [Header("Attributes")]
        [SerializeField] private float radius = 0.16f;
        [SerializeField] private GameObject baseLink;

        [Header("Components")]
        [SerializeField] private AgentDetector detector;
        
        [Header("Constraints")]
        [Tooltip("Keep the parent GameObject (this transform) at world origin (0,0,0). Only baseLink moves.")]
        [SerializeField] private bool keepParentAtOrigin = true;
        
        // Movement state
        private float _targetLinearSpeed;
        private float _targetAngularSpeed;
        private float _currentLinearSpeed;
        private float _currentAngularSpeed;
        private Rigidbody _agentRb;
        
        // Scenario properties
        private Vector3 _currentGoal;
        private string _currentBehavior = "normal";
        private float _currentSpeed = 1.2f;
        private bool _hasGoal = false;
        
        // Events
        public event Action<float, float> OnVelocityCommandReceived;
        public event Action<Vector3, Quaternion> OnMovementUpdated;
        
        // Public properties – all return baseLink state when possible
        public float Radius => radius;
        public float CurrentLinearSpeed => _currentLinearSpeed;
        public float CurrentAngularSpeed => _currentAngularSpeed;
        public Vector3 Position => baseLink != null ? baseLink.transform.position : transform.position;
        public Quaternion Rotation => baseLink != null ? baseLink.transform.rotation : transform.rotation;
        public Vector3 Velocity => _agentRb != null ? _agentRb.linearVelocity : Vector3.zero;
        public Transform RobotTransform => baseLink != null ? baseLink.transform : transform;
        
        // Scenario properties
        public Vector3 CurrentGoal => _currentGoal;
        public string CurrentBehavior => _currentBehavior;
        public float CurrentSpeed => _currentSpeed;
        public bool HasGoal => _hasGoal;
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            InitializeRigidbody();
            EnforceParentOrigin();
        }
        
        private void Start()
        {
            EnforceParentOrigin();
        }
        
        private void FixedUpdate()
        {
            UpdateMovement();
            if (_hasGoal)
                UpdateScenarioMovement();
        }
        
        #endregion
        
        #region Initialization & Constraint
        
        private void InitializeRigidbody()
        {
            if (baseLink != null)
            {
                _agentRb = baseLink.GetComponent<Rigidbody>();
                if (_agentRb == null)
                    _agentRb = baseLink.AddComponent<Rigidbody>();
            }
            else
            {
                Debug.LogWarning("[Robot] baseLink not assigned. Falling back to this GameObject.");
                _agentRb = GetComponent<Rigidbody>();
                if (_agentRb == null)
                    _agentRb = gameObject.AddComponent<Rigidbody>();
                baseLink = gameObject;
            }
            
            if (_agentRb != null)
            {
                _agentRb.useGravity = false;
                _agentRb.mass = 1f;
                _agentRb.linearDamping = 0.5f;
                _agentRb.angularDamping = 0.5f;
            }
        }
        
        /// <summary>
        /// Force the parent transform to world origin if keepParentAtOrigin is true.
        /// </summary>
        private void EnforceParentOrigin()
        {
            if (keepParentAtOrigin)
            {
                transform.position = Vector3.zero;
                transform.rotation = Quaternion.identity;
            }
        }
        
        #endregion
        
        #region Movement Logic
        
        private void UpdateMovement()
        {
            _currentLinearSpeed = Mathf.MoveTowards(
                _currentLinearSpeed, 
                _targetLinearSpeed, 
                (_targetLinearSpeed > _currentLinearSpeed ? acceleration : deceleration) * Time.fixedDeltaTime
            );
            
            _currentAngularSpeed = Mathf.MoveTowards(
                _currentAngularSpeed, 
                _targetAngularSpeed, 
                (_targetAngularSpeed > _currentAngularSpeed ? acceleration : deceleration) * Time.fixedDeltaTime
            );
            
            if (_agentRb != null)
            {
                Vector3 localAngularVelocity = new Vector3(0, _currentAngularSpeed, 0);
                Vector3 worldAngularVelocity = _agentRb.transform.TransformDirection(localAngularVelocity);
                _agentRb.angularVelocity = worldAngularVelocity;
                _agentRb.linearVelocity = _agentRb.transform.forward * _currentLinearSpeed;
            }
            
            OnMovementUpdated?.Invoke(Position, Rotation);
        }
        
        private void UpdateScenarioMovement()
        {
            Vector3 direction = (_currentGoal - Position).normalized;
            float distance = Vector3.Distance(Position, _currentGoal);
            
            float targetSpeed = _currentSpeed;
            if (distance < 1.0f)
                targetSpeed = _currentSpeed * (distance / 1.0f);
            
            float angleToGoal = Vector3.SignedAngle(RobotTransform.forward, direction, Vector3.up);
            float angularSpeed = Mathf.Clamp(angleToGoal * 2.0f, -maxAngularSpeed, maxAngularSpeed);
            
            SetVelocity(targetSpeed, angularSpeed);
            
            if (distance < 0.2f)
            {
                _hasGoal = false;
                Stop();
            }
        }
        
        #endregion
        
        #region Public API - Movement Control
        
        public void SetVelocity(float linearSpeed, float angularSpeed)
        {
            _targetLinearSpeed = Mathf.Clamp(linearSpeed, -maxLinearSpeed, maxLinearSpeed);
            _targetAngularSpeed = Mathf.Clamp(angularSpeed, -maxAngularSpeed, maxAngularSpeed);
            OnVelocityCommandReceived?.Invoke(_targetLinearSpeed, _targetAngularSpeed);
        }
        
        public void Stop()
        {
            _targetLinearSpeed = 0f;
            _targetAngularSpeed = 0f;
            _currentLinearSpeed = 0f;
            _currentAngularSpeed = 0f;
            if (_agentRb != null)
            {
                _agentRb.linearVelocity = Vector3.zero;
                _agentRb.angularVelocity = Vector3.zero;
            }
        }
        
        public void Reset()
        {
            Stop();
            _hasGoal = false;
            _currentGoal = Vector3.zero;
            EnforceParentOrigin();
        }
        
        #endregion
        
        #region Public API - Teleportation (baseLink only)
        
        /// <summary>
        /// Set the baseLink position and rotation. The parent transform stays at origin.
        /// </summary>
        public void SetBaseLinkPose(Vector3 position, Quaternion rotation)
        {
            if (baseLink == null)
            {
                Debug.LogError("[Robot] baseLink is null, cannot set pose.");
                return;
            }
            baseLink.transform.position = position;
            baseLink.transform.rotation = rotation;
            ResetVelocity();
            EnforceParentOrigin();
        }
        
        /// <summary>
        /// Set only the baseLink position.
        /// </summary>
        public void SetBaseLinkPosition(Vector3 position)
        {
            if (baseLink == null) return;
            baseLink.transform.position = position;
            ResetVelocity();
            EnforceParentOrigin();
        }
        
        /// <summary>
        /// Set only the baseLink rotation.
        /// </summary>
        public void SetBaseLinkRotation(Quaternion rotation)
        {
            if (baseLink == null) return;
            baseLink.transform.rotation = rotation;
            ResetVelocity();
            EnforceParentOrigin();
        }
        
        /// <summary>
        /// Legacy method for compatibility – works on baseLink.
        /// </summary>
        public void SetPose(Vector3 position, Quaternion rotation)
        {
            SetBaseLinkPose(position, rotation);
        }
        
        private void ResetVelocity()
        {
            if (_agentRb != null)
            {
                _agentRb.linearVelocity = Vector3.zero;
                _agentRb.angularVelocity = Vector3.zero;
            }
            _currentLinearSpeed = 0;
            _currentAngularSpeed = 0;
            _targetLinearSpeed = 0;
            _targetAngularSpeed = 0;
        }
        
        #endregion
        
        #region Public API - Goal & Behavior
        
        public void SetGoal(Vector3 goal)
        {
            _currentGoal = goal;
            _hasGoal = true;
        }
        
        public void SetBehavior(string behavior)
        {
            _currentBehavior = behavior;
            switch (behavior.ToLower())
            {
                case "cautious":
                    maxLinearSpeed = 0.8f;
                    acceleration = 1.5f;
                    break;
                case "assertive":
                    maxLinearSpeed = 1.5f;
                    acceleration = 3.0f;
                    break;
                case "socially_aware":
                    maxLinearSpeed = 1.2f;
                    acceleration = 2.0f;
                    break;
                default:
                    maxLinearSpeed = 1.2f;
                    acceleration = 2.0f;
                    break;
            }
        }
        
        public void SetSpeed(float speed)
        {
            _currentSpeed = Mathf.Clamp(speed, 0.5f, 3.0f);
        }
        
        #endregion
        
        #region Sensors
        
        public void SetLaserSample(int laserSample)
        {
            var laser = GetComponentInChildren<RaycastLaserScanner>();
            if (laser != null)
            {
                laser.samples = laserSample;
                laser.Init();
            }
        }
        
        public RaycastLaserScanner GetLaserScanner()
        {
            return GetComponentInChildren<RaycastLaserScanner>();
        }
        
        public LaserScanPublisher GetLaserPublisher()
        {
            return GetComponentInChildren<LaserScanPublisher>();
        }
        
        public AgentDetector GetAgentDetector()
        {
            return detector;
        }
        
        #endregion
        
        #region Editor Utilities
        
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
        
        #endregion
        
        public override string ToString()
        {
            return $"{gameObject.name} (Behavior: {_currentBehavior}, Speed: {_currentSpeed}, Goal: {(_hasGoal ? _currentGoal.ToString() : "none")})";
        }
    }
}