using UnityEngine;
using System.Collections.Generic;
using RobotSNAP.Movement.Interfaces;
using RobotSNAP.Movement.Controllers;
using RobotSNAP.Core;

namespace RobotSNAP.Human
{
    [RequireComponent(typeof(Rigidbody))]
    public class HumanMovement : MonoBehaviour
    {
        private HumanAvatar _avatar;
        [SerializeField] private IMovementController _controller;
        private HumanConfig _config;
        private Rigidbody _rb;
        
        // Navigation NavMesh
        private UnityEngine.AI.NavMeshPath _navMeshPath;
        private Vector3[] _pathCorners;
        private float _lastPathUpdate;
        private Vector2 _currentGoalPoint;
        
        // Filter pour NavMesh (important pour spécifier le type d'agent)
        private UnityEngine.AI.NavMeshQueryFilter _navMeshFilter;
        
        // État
        private Vector2 _currentVelocity;
        private Vector2 _currentPosition;
        private bool _isPlaying = true;
        private MovementControllerType _currentControllerType;
        
        // Buffer de trajectoire
        private Vector2[] _trajectoryBuffer;
        private int _bufferIndex;
        private const int TRAJECTORY_BUFFER_SIZE = 8;
        
        // Détection des voisins
        private List<Vector2> _neighborPositions;
        private List<Vector2> _neighborVelocities;
        
        public bool IsPlaying => _isPlaying;
        public Vector2 CurrentVelocity => _currentVelocity;
        public Vector2 CurrentPosition => _currentPosition;
        
        private void Awake()
        {
            InitializeComponents();
            InitializeNavMeshFilter();
        }
        
        private void InitializeComponents()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
            
            _rb.useGravity = true;
            _rb.constraints = RigidbodyConstraints.FreezeRotationX | 
                             RigidbodyConstraints.FreezeRotationZ | 
                             RigidbodyConstraints.FreezePositionY;
            _rb.linearDamping = 2f;
            _rb.angularDamping = 5f;
            
            _navMeshPath = new UnityEngine.AI.NavMeshPath();
            _pathCorners = new Vector3[0];
            _trajectoryBuffer = new Vector2[TRAJECTORY_BUFFER_SIZE];
            _neighborPositions = new List<Vector2>();
            _neighborVelocities = new List<Vector2>();
            
            SetupCollider();
        }
        
        private void InitializeNavMeshFilter()
        {
            _navMeshFilter = new UnityEngine.AI.NavMeshQueryFilter();
            
            // Utiliser l'agent type par défaut (Humanoid)
            // L'ID 0 correspond généralement au premier agent type défini dans les paramètres NavMesh
            _navMeshFilter.agentTypeID = 0;
            
            // Inclure toutes les aires par défaut
            _navMeshFilter.areaMask = UnityEngine.AI.NavMesh.AllAreas;
        }
        
        private void SetupCollider()
        {
            var collider = GetComponent<CapsuleCollider>();
            if (collider == null) collider = gameObject.AddComponent<CapsuleCollider>();
            
            var renderer = GetComponentInChildren<SkinnedMeshRenderer>();
            if (renderer != null)
            {
                var bounds = renderer.bounds;
                float height = bounds.extents.y * 2;
                collider.radius = 0.25f;
                collider.height = height;
                collider.center = Vector3.up * height / 1.95f;
            }
        }
        
        public void Initialize(HumanAvatar avatar, HumanConfig config)
        {
            _avatar = avatar;
            _config = config;
            _currentControllerType = config.controllerType;
            InitializeController(_currentControllerType);
        }
        
        private void InitializeController(MovementControllerType type)
        {
            switch (type)
            {
                case MovementControllerType.LegacySFM:
                    _controller = new LegacySFMController(_config);
                    break;
                case MovementControllerType.ONNXPrediction:
                    _controller = new ONNXPredictionController(_config);
                    break;
                case MovementControllerType.Hybrid:
                    _controller = new HybridController(_config);
                    break;
                default:
                    _controller = new LegacySFMController(_config);
                    break;
            }
        }
        
        private void FixedUpdate()
        {
            if (!_isPlaying || _avatar == null || !_avatar.hasDestination) return;
            
            _currentPosition = _avatar.GetCurrentPosition2D();
            _currentVelocity = new Vector2(_rb.linearVelocity.x, _rb.linearVelocity.z);
            
            UpdateTrajectoryBuffer();
            UpdateNavMeshPath();
            DetectNeighbors();
            
            Vector2 desiredVelocity = _controller.ComputeVelocity(
                _currentPosition,
                _currentVelocity,
                _currentGoalPoint,
                _neighborPositions.ToArray(),
                _neighborVelocities.ToArray(),
                Time.fixedDeltaTime
            );
            
            _rb.linearVelocity = new Vector3(desiredVelocity.x, _rb.linearVelocity.y, desiredVelocity.y);
            _avatar.SetVelocity(_rb.linearVelocity);
            
            UpdateRotation(desiredVelocity);
        }
        
        private void UpdateTrajectoryBuffer()
        {
            _trajectoryBuffer[_bufferIndex] = _currentPosition;
            _bufferIndex = (_bufferIndex + 1) % TRAJECTORY_BUFFER_SIZE;
        }
        
        private void UpdateNavMeshPath()
        {
            if (Time.time - _lastPathUpdate < _config.pathUpdateInterval) return;
            _lastPathUpdate = Time.time;
            
            Vector3 start3D = new Vector3(_currentPosition.x, 0, _currentPosition.y);
            Vector3 goal3D = new Vector3(_avatar.currentDestination.x, 0, _avatar.currentDestination.y);
            
            // CORRECTION : Utiliser le filtre avec l'agent type
            if (UnityEngine.AI.NavMesh.CalculatePath(start3D, goal3D, _navMeshFilter, _navMeshPath))
            {
                if (_navMeshPath.status == UnityEngine.AI.NavMeshPathStatus.PathComplete)
                {
                    _pathCorners = _navMeshPath.corners;
                    UpdateCurrentGoalPoint();
                }
            }
            else
            {
                // Fallback : chemin direct si pas de chemin NavMesh
                _currentGoalPoint = _avatar.currentDestination;
            }
        }
        
        private void UpdateCurrentGoalPoint()
        {
            if (_navMeshPath.corners == null || _navMeshPath.corners.Length == 0)
            {
                _currentGoalPoint = _avatar.currentDestination;
                return;
            }
            
            foreach (Vector3 p in _navMeshPath.corners)
            {
                if (Util.Geometry.GroundPlaneDist(transform.position, p) > _config.goalReachedDistance)
                {
                    _currentGoalPoint = new Vector2(p.x, p.z);
                    return;
                }
            }
            _currentGoalPoint = _avatar.currentDestination;
        }
        
        private void DetectNeighbors()
        {
            _neighborPositions.Clear();
            _neighborVelocities.Clear();
            
            Collider[] colliders = Physics.OverlapSphere(
                new Vector3(_currentPosition.x, 0.5f, _currentPosition.y),
                _config.sfmInteractionRadius
            );
            
            foreach (var collider in colliders)
            {
                HumanAvatar otherAvatar = collider.GetComponentInParent<HumanAvatar>();
                if (otherAvatar != null && otherAvatar != _avatar && otherAvatar.gameObject.activeSelf)
                {
                    Vector2 otherPos = otherAvatar.GetCurrentPosition2D();
                    float dist = Vector2.Distance(_currentPosition, otherPos);
                    
                    if (dist < _config.sfmInteractionRadius && dist > 0.01f)
                    {
                        _neighborPositions.Add(otherPos);
                        _neighborVelocities.Add(new Vector2(
                            otherAvatar.currentVelocity3D.x,
                            otherAvatar.currentVelocity3D.z
                        ));
                    }
                    continue;
                }
                
                Robot robot = collider.GetComponentInParent<Robot>();
                if (robot != null && robot.gameObject.activeSelf)
                {
                    Vector2 robotPos = new Vector2(robot.transform.position.x, robot.transform.position.z);
                    float dist = Vector2.Distance(_currentPosition, robotPos);
                    
                    if (dist < _config.sfmInteractionRadius)
                    {
                        _neighborPositions.Add(robotPos);
                        _neighborVelocities.Add(new Vector2(robot.Velocity.x, robot.Velocity.z));
                    }
                }
            }
        }
        
        private void UpdateRotation(Vector2 movement)
        {
            if (movement.magnitude > 0.1f)
            {
                float angle = Mathf.Atan2(movement.x, movement.y) * Mathf.Rad2Deg;
                Quaternion targetRotation = Quaternion.Euler(0, angle, 0);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    _config.angularSpeed * Time.fixedDeltaTime
                );
            }
        }
        
        public void SetPlaying(bool playing)
        {
            _isPlaying = playing;
            if (!_isPlaying)
            {
                _rb.linearVelocity = Vector3.zero;
                _avatar?.SetVelocity(Vector3.zero);
            }
        }
        
        public void SetGoal(Vector2 goal)
        {
            if (_avatar != null)
            {
                _avatar.currentDestination = goal;
                _avatar.hasDestination = true;
            }
            _lastPathUpdate = -_config.pathUpdateInterval;
            UpdateNavMeshPath();
        }
        
        public void Reset()
        {
            _controller?.Reset();
            System.Array.Clear(_trajectoryBuffer, 0, TRAJECTORY_BUFFER_SIZE);
            _bufferIndex = 0;
            _rb.linearVelocity = Vector3.zero;
            _avatar?.SetVelocity(Vector3.zero);
            _pathCorners = new Vector3[0];
        }
        
        public void SwitchController(MovementControllerType newType)
        {
            _currentControllerType = newType;
            _controller?.Reset();
            InitializeController(newType);
        }
        
        public IMovementController GetController() => _controller;
        public MovementControllerType GetControllerType() => _currentControllerType;
        public float GetConfidence() => _controller?.GetConfidence() ?? 0f;
        
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;
            
            if (_navMeshPath != null && _navMeshPath.corners != null)
            {
                Gizmos.color = Color.black;
                Vector3 lastPos = transform.position;
                foreach (Vector3 position in _navMeshPath.corners)
                {
                    if (Util.Geometry.GroundPlaneDist(transform.position, position) > _config.goalReachedDistance)
                    {
                        Debug.DrawLine(lastPos, position, Color.black);
                        Gizmos.DrawCube(position, new Vector3(0.15f, 0.15f, 0.15f));
                        lastPos = position;
                    }
                }
            }
            
            if (_avatar != null && _avatar.hasDestination)
            {
                Vector3 goal3D = new Vector3(_avatar.currentDestination.x, 0, _avatar.currentDestination.y);
                Gizmos.color = Color.blue;
                Gizmos.DrawCube(goal3D, new Vector3(0.25f, 0.25f, 0.25f));
            }
            
            if (_currentGoalPoint != Vector2.zero)
            {
                Vector3 nextGoal3D = new Vector3(_currentGoalPoint.x, 0, _currentGoalPoint.y);
                Gizmos.color = Color.red;
                Gizmos.DrawCube(nextGoal3D, new Vector3(0.25f, 0.25f, 0.25f));
            }
        }
    }
}