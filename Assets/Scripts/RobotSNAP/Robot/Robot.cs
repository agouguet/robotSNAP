using UnityEngine;
using System;

namespace RobotSNAP
{
    /// <summary>
    /// Core robot controller - No ROS dependencies.
    /// Handles movement, sensors, and state.
    /// </summary>
    public class Robot : MonoBehaviour
    {
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
        
        // Events for external systems (like ROS bridge)
        public event Action<float, float> OnVelocityCommandReceived;
        public event Action<Vector3, Quaternion> OnMovementUpdated;
        
        // Public properties
        public float Radius => radius;
        public float CurrentLinearSpeed => _currentLinearSpeed;
        public float CurrentAngularSpeed => _currentAngularSpeed;
        public Vector3 Position => baseLink != null ? baseLink.transform.position : transform.position;
        public Quaternion Rotation => baseLink != null ? baseLink.transform.rotation : transform.rotation;
        public Vector3 Velocity => _agentRb != null ? _agentRb.linearVelocity : Vector3.zero;
        public Transform RobotTransform => baseLink != null ? baseLink.transform : transform;
        
        // Scenario properties (public getters)
        public Vector3 CurrentGoal => _currentGoal;
        public string CurrentBehavior => _currentBehavior;
        public float CurrentSpeed => _currentSpeed;
        public bool HasGoal => _hasGoal;
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            InitializeRigidbody();
        }
        
        private void FixedUpdate()
        {
            UpdateMovement();
            
            // Scenario-based movement if goal is set
            if (_hasGoal)
            {
                UpdateScenarioMovement();
            }
        }
        
        #endregion
        
        #region Initialization
        
        private void InitializeRigidbody()
        {
            // Get or create Rigidbody
            if (baseLink != null)
            {
                _agentRb = baseLink.GetComponent<Rigidbody>();
                if (_agentRb == null)
                {
                    _agentRb = baseLink.AddComponent<Rigidbody>();
                }
            }
            else
            {
                _agentRb = GetComponent<Rigidbody>();
                if (_agentRb == null)
                {
                    _agentRb = gameObject.AddComponent<Rigidbody>();
                }
                baseLink = gameObject;
            }
            
            // Configure Rigidbody
            if (_agentRb != null)
            {
                _agentRb.useGravity = false;
                _agentRb.mass = 1f;
                _agentRb.linearDamping = 0.5f;
                _agentRb.angularDamping = 0.5f;
            }
        }
        
        #endregion
        
        #region Movement Logic
        
        private void UpdateMovement()
        {
            // Smooth acceleration/deceleration
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
            
            // Apply movement
            if (_agentRb != null)
            {
                // Angular velocity (yaw only)
                Vector3 localAngularVelocity = new Vector3(0, _currentAngularSpeed, 0);
                Vector3 worldAngularVelocity = _agentRb.transform.TransformDirection(localAngularVelocity);
                _agentRb.angularVelocity = worldAngularVelocity;
                
                // Linear velocity
                _agentRb.linearVelocity = _agentRb.transform.forward * _currentLinearSpeed;
            }
            
            // Notify subscribers
            OnMovementUpdated?.Invoke(Position, Rotation);
        }
        
        private void UpdateScenarioMovement()
        {
            // Calculate direction to goal
            Vector3 direction = (_currentGoal - Position).normalized;
            float distance = Vector3.Distance(Position, _currentGoal);
            
            // Slow down when approaching goal
            float targetSpeed = _currentSpeed;
            if (distance < 1.0f)
            {
                targetSpeed = _currentSpeed * (distance / 1.0f);
            }
            
            // Calculate angular speed to face goal
            float angleToGoal = Vector3.SignedAngle(transform.forward, direction, Vector3.up);
            float angularSpeed = Mathf.Clamp(angleToGoal * 2.0f, -maxAngularSpeed, maxAngularSpeed);
            
            // Apply movement
            SetVelocity(targetSpeed, angularSpeed);
            
            // Check if goal reached
            if (distance < 0.2f)
            {
                _hasGoal = false;
                Stop();
            }
        }
        
        #endregion
        
        #region Public API - Command Interface
        
        /// <summary>
        /// Set the target velocity for the robot
        /// </summary>
        /// <param name="linearSpeed">Forward speed (m/s)</param>
        /// <param name="angularSpeed">Angular speed (rad/s) - positive = left turn</param>
        public void SetVelocity(float linearSpeed, float angularSpeed)
        {
            // Clamp values
            _targetLinearSpeed = Mathf.Clamp(linearSpeed, -maxLinearSpeed, maxLinearSpeed);
            _targetAngularSpeed = Mathf.Clamp(angularSpeed, -maxAngularSpeed, maxAngularSpeed);
            
            OnVelocityCommandReceived?.Invoke(_targetLinearSpeed, _targetAngularSpeed);
        }
        
        /// <summary>
        /// Stop the robot immediately
        /// </summary>
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
        
        /// <summary>
        /// Reset the robot state
        /// </summary>
        public void Reset()
        {
            Stop();
            _hasGoal = false;
            _currentGoal = Vector3.zero;
            // detector?.Restart();
        }
        
        /// <summary>
        /// Set position and rotation
        /// </summary>
        public void SetPose(Vector3 position, Quaternion rotation)
        {
            transform.position = position;
            transform.rotation = rotation;
            
            if (_agentRb != null)
            {
                _agentRb.linearVelocity = Vector3.zero;
                _agentRb.angularVelocity = Vector3.zero;
            }
        }
        
        /// <summary>
        /// Set the robot's goal position (for scenario-based navigation)
        /// </summary>
        public void SetGoal(Vector3 goal)
        {
            _currentGoal = goal;
            _hasGoal = true;
        }
        
        /// <summary>
        /// Set the robot's behavior (normal, cautious, assertive, socially_aware)
        /// </summary>
        public void SetBehavior(string behavior)
        {
            _currentBehavior = behavior;
            
            // Adjust movement parameters based on behavior
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
                default: // normal
                    maxLinearSpeed = 1.2f;
                    acceleration = 2.0f;
                    break;
            }
        }
        
        /// <summary>
        /// Set the robot's speed
        /// </summary>
        public void SetSpeed(float speed)
        {
            _currentSpeed = Mathf.Clamp(speed, 0.5f, 3.0f);
        }
        
        #endregion
        
        #region Sensors
        
        /// <summary>
        /// Set laser scanner resolution
        /// </summary>
        public void SetLaserSample(int laserSample)
        {
            var laser = GetComponentInChildren<RaycastLaserScanner>();
            if (laser != null)
            {
                laser.samples = laserSample;
                laser.Init();
            }
        }
        
        /// <summary>
        /// Get the laser scanner component
        /// </summary>
        public RaycastLaserScanner GetLaserScanner()
        {
            return GetComponentInChildren<RaycastLaserScanner>();
        }
        
        /// <summary>
        /// Get the laser scan publisher (if attached)
        /// </summary>
        public LaserScanPublisher GetLaserPublisher()
        {
            return GetComponentInChildren<LaserScanPublisher>();
        }
        
        /// <summary>
        /// Get the agent detector component
        /// </summary>
        public AgentDetector GetAgentDetector()
        {
            return detector;
        }
        
        #endregion
        
        #region Editor Utilities
        
        [ContextMenu("Stop")]
        private void EditorStop()
        {
            Stop();
        }
        
        [ContextMenu("Reset")]
        private void EditorReset()
        {
            Reset();
        }
        
        [ContextMenu("Set Test Goal")]
        private void EditorSetTestGoal()
        {
            SetGoal(new Vector3(5, 0, 5));
        }
        
        private void OnDrawGizmosSelected()
        {
            // Draw robot radius
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, radius);
            
            // Draw goal if set
            if (_hasGoal && Application.isPlaying)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, _currentGoal);
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