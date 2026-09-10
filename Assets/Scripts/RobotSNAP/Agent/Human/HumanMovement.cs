using UnityEngine;
using System.Collections.Generic;
using RobotSNAP.Agents.Movement.Controllers;
using RobotSNAP.Agents.Movement.Interfaces;
using RobotSNAP.Core;
using UnityEngine.AI;

namespace RobotSNAP.Agents
{
    [RequireComponent(typeof(Rigidbody))]
    public class HumanMovement : MonoBehaviour
    {
        [Header("Fallback Settings")]
        [SerializeField] private float _maxSpeed = 2.0f;
        [SerializeField] private float _angularSpeed = 180f;

        [Header("Obstacle SphereCast Settings")]
        [SerializeField] private float _sphereCastRadiusScale = 0.8f;
        [SerializeField] private int _raycastCountPerSide = 6;
        [SerializeField] private float _angleStepDegrees = 30f;
        [SerializeField] private float _verticalOffset = 0.5f;

        // References
        private HumanManager _humanManager;
        private int _agentId;
        private HumanAgent _avatar;
        private IMovementController _controller;
        private HumanConfig _config;
        private Rigidbody _rb;

        // NavMesh
        private NavMeshPath _navMeshPath;
        private Vector3[] _pathCorners;
        private float _lastPathUpdate;
        private Vector2 _currentGoalPoint;

        // State
        private Vector2 _currentVelocity;
        private Vector2 _currentPosition;
        private bool _isPlaying = true;
        private MovementControllerType _currentControllerType;

        // Neighbors
        private readonly List<Vector2> _neighborPositions = new List<Vector2>();
        private readonly List<Vector2> _neighborVelocities = new List<Vector2>();

        // Obstacles
        private readonly List<Vector2> _tempObstacles = new List<Vector2>();

        // Public properties
        public bool IsPlaying => _isPlaying;
        public Vector2 CurrentVelocity => _currentVelocity;
        public Vector2 CurrentPosition => _currentPosition;

        #region Unity Lifecycle

        private void Awake()
        {
            InitializeComponents();
        }

        private void FixedUpdate()
        {
            if (_isPlaying && _avatar != null && _avatar.hasDestination)
                Move();
        }

        #endregion

        #region Initialization

        private void InitializeComponents()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
            _rb.useGravity = false;
            _rb.isKinematic = false;                 // Pour utiliser linearVelocity
            _rb.constraints = RigidbodyConstraints.FreezeRotationX |
                              RigidbodyConstraints.FreezeRotationZ |
                              RigidbodyConstraints.FreezePositionY;
            _rb.linearDamping = 0f;
            _rb.angularDamping = 0f;
            _rb.interpolation = RigidbodyInterpolation.None;

            _navMeshPath = new NavMeshPath();
            _pathCorners = new Vector3[0];
            SetupCollider();
            _currentVelocity = Vector2.zero;
        }

        private void SetupCollider()
        {
            CapsuleCollider collider = GetComponent<CapsuleCollider>();
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
            else
            {
                collider.radius = 0.25f;
                collider.height = 1.7f;
                collider.center = Vector3.up * 0.85f;
            }
        }

        public void Initialize(HumanAgent avatar, HumanConfig config)
        {
            _avatar = avatar;
            _config = config;
            _agentId = avatar.agentId;
            _currentControllerType = config.controllerType;
            InitializeController(_currentControllerType);
        }

        private void OnEnable()
        {
            if (_agentId != 0 && _humanManager != null)
            {
                Vector2 currentPos = new Vector2(transform.position.x, transform.position.z);
                _humanManager.RegisterAgent(_agentId, currentPos);
                _humanManager.UpdateAgent(_agentId, currentPos, _currentVelocity);
            }
        }

        private void OnDisable()
        {
            if (_humanManager != null && _agentId != 0)
                _humanManager.UnregisterAgent(_agentId);
        }

        private void OnDestroy()
        {
            if (_humanManager != null && _agentId != 0)
                _humanManager.UnregisterAgent(_agentId);
        }

        private void InitializeController(MovementControllerType type)
        {
            switch (type)
            {
                case MovementControllerType.SFM:
                    _controller = new SFMController(_config, _avatar);
                    break;
                case MovementControllerType.ONNXPrediction:
                    _controller = new ONNXPredictionController(_config);
                    break;
                case MovementControllerType.Hybrid:
                    _controller = new HybridController(_config);
                    break;
                default:
                    _controller = new SFMController(_config, _avatar);
                    break;
            }
        }

        public void SetHumanManager(HumanManager manager) => _humanManager = manager;

        #endregion

        #region Movement Logic

        private void Move()
        {
            _currentPosition = new Vector2(transform.position.x, transform.position.z);

            UpdateNavMeshPath();

            // Récupération des voisins
            _neighborPositions.Clear();
            _neighborVelocities.Clear();
            if (_humanManager != null)
            {
                _humanManager.GetNeighbors(
                    _agentId,
                    _currentPosition,
                    _config.perceptionRadiusAgent,
                    _neighborPositions,
                    _neighborVelocities
                );
            }

            // Détection des obstacles statiques (SphereCast)
            _tempObstacles.Clear();
            int layerMask = 1 << LayerMask.NameToLayer("Obstacle");
            float sphereRadius = _config.agentRadius * _sphereCastRadiusScale;
            float maxDistance = _config.obstaclePerceptionRadius;
            Vector3 origin = transform.position + Vector3.up * _verticalOffset;
            Vector3 forward = transform.forward;

            void CastAndAdd(Vector3 direction)
            {
                if (Physics.SphereCast(origin, sphereRadius, direction, out RaycastHit hit, maxDistance, layerMask))
                {
                    Vector3 closestPoint = hit.point - hit.normal * sphereRadius;
                    Vector2 obsPos = new Vector2(closestPoint.x, closestPoint.z);
                    float dist = Vector2.Distance(_currentPosition, obsPos);
                    if (dist > 0.01f)
                        _tempObstacles.Add(obsPos);
                }
            }

            CastAndAdd(forward);
            for (int i = 1; i <= _raycastCountPerSide; i++)
            {
                float angle = i * _angleStepDegrees;
                CastAndAdd(Quaternion.AngleAxis(angle, Vector3.up) * forward);
                CastAndAdd(Quaternion.AngleAxis(-angle, Vector3.up) * forward);
            }

            // Calcul de la nouvelle vitesse via le contrôleur
            Vector2 desiredVelocity = _controller.ComputeVelocity(
                _currentPosition,
                _currentVelocity,
                _currentGoalPoint,
                _neighborPositions.ToArray(),
                _neighborVelocities.ToArray(),
                _tempObstacles.ToArray(),
                Time.fixedDeltaTime
            );

            // Application de la vélocité via le Rigidbody
            Vector3 finalVelocity3D = new Vector3(desiredVelocity.x, 0, desiredVelocity.y);
            float maxSpeed = _config != null ? _config.maxSpeed : _maxSpeed;
            if (finalVelocity3D.sqrMagnitude > maxSpeed * maxSpeed)
                finalVelocity3D = finalVelocity3D.normalized * maxSpeed;

            _rb.linearVelocity = finalVelocity3D;

            // Mise à jour de notre vélocité stockée (pour l'animation, etc.)
            _currentVelocity = new Vector2(finalVelocity3D.x, finalVelocity3D.z);

            // Rotation via MoveRotation (pour éviter les conflits)
            UpdateRotation(desiredVelocity);

            // Mise à jour du HumanManager
            _humanManager?.UpdateAgent(_agentId, _currentPosition, _currentVelocity);

            // Transmission de la vélocité réelle à l'agent (pour l'animation)
            _avatar?.SetVelocity(finalVelocity3D);
        }

        #endregion

        #region Navigation

        private void UpdateNavMeshPath()
        {
            if (_config == null) return;
            if (Time.time - _lastPathUpdate < _config.pathUpdateInterval) return;
            _lastPathUpdate = Time.time;

            Vector3 start3D = new Vector3(_currentPosition.x, 0, _currentPosition.y);
            Vector3 goal3D = new Vector3(_avatar.currentDestination.x, 0, _avatar.currentDestination.y);

            if (NavMesh.CalculatePath(start3D, goal3D, NavMesh.AllAreas, _navMeshPath))
            {
                if (_navMeshPath.status == NavMeshPathStatus.PathComplete)
                {
                    _pathCorners = _navMeshPath.corners;
                    UpdateCurrentGoalPoint();
                }
                else
                {
                    _currentGoalPoint = _avatar.currentDestination;
                }
            }
            else
            {
                _currentGoalPoint = _avatar.currentDestination;
            }
        }

        private void UpdateCurrentGoalPoint()
        {
            if (_pathCorners == null || _pathCorners.Length == 0)
            {
                _currentGoalPoint = _avatar.currentDestination;
                return;
            }

            float goalReachedDist = _config != null ? _config.goalReachedDistance : 0.2f;
            foreach (Vector3 p in _pathCorners)
            {
                if (Vector2.Distance(_currentPosition, new Vector2(p.x, p.z)) > goalReachedDist)
                {
                    _currentGoalPoint = new Vector2(p.x, p.z);
                    return;
                }
            }
            _currentGoalPoint = _avatar.currentDestination;
        }

        #endregion

        #region Rotation

        private void UpdateRotation(Vector2 movementDirection)
        {
            if (movementDirection.sqrMagnitude < 0.001f) return;

            float angle = Mathf.Atan2(movementDirection.x, movementDirection.y) * Mathf.Rad2Deg;
            Quaternion targetRotation = Quaternion.Euler(0, angle, 0);
            float angularSpeed = _config != null ? _config.angularSpeed : _angularSpeed;

            // Utiliser MoveRotation pour être cohérent avec la physique
            _rb.MoveRotation(Quaternion.RotateTowards(
                _rb.rotation,
                targetRotation,
                angularSpeed * Time.fixedDeltaTime
            ));
        }

        #endregion

        #region Public API

        public void SetPlaying(bool playing)
        {
            _isPlaying = playing;
            if (!_isPlaying)
            {
                _rb.linearVelocity = Vector3.zero;
                _currentVelocity = Vector2.zero;
                _avatar?.SetVelocity(Vector3.zero);
            }
        }

        public void Stop()
        {
            _rb.linearVelocity = Vector3.zero;
            _currentVelocity = Vector2.zero;
            _avatar?.SetVelocity(Vector3.zero);
        }

        public void SetGoal(Vector2 goal)
        {
            if (_avatar != null)
            {
                _avatar.currentDestination = goal;
                _avatar.hasDestination = true;
            }
            _lastPathUpdate = -_config.pathUpdateInterval;
        }

        public void Reset()
        {
            _controller?.Reset();
            _rb.linearVelocity = Vector3.zero;
            _currentVelocity = Vector2.zero;
            _avatar?.SetVelocity(Vector3.zero);
            _pathCorners = new Vector3[0];
            _currentGoalPoint = Vector2.zero;
            _neighborPositions.Clear();
            _neighborVelocities.Clear();
            _tempObstacles.Clear();
        }

        public void SwitchController(MovementControllerType newType)
        {
            _currentControllerType = newType;
            _controller?.Reset();
            InitializeController(newType);
        }

        public void SetControllerType(int type)
        {
            MovementControllerType controllerType = (MovementControllerType)Mathf.Clamp(type, 0, 2);
            if (_currentControllerType != controllerType)
                SwitchController(controllerType);
        }

        #endregion

        #region Getters

        public IMovementController GetController() => _controller;
        public MovementControllerType GetControllerType() => _currentControllerType;
        public float GetConfidence() => _controller?.GetConfidence() ?? 0f;
        public bool HasGoal => _avatar != null && _avatar.hasDestination;
        public Vector2 GoalPosition2D => _avatar != null ? _avatar.currentDestination : Vector2.zero;

        #endregion

        #region Debug

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;

            if (_navMeshPath != null && _navMeshPath.corners != null)
            {
                Gizmos.color = Color.black;
                Vector3 lastPos = transform.position;
                foreach (Vector3 position in _navMeshPath.corners)
                {
                    float dist = Vector2.Distance(_currentPosition, new Vector2(position.x, position.z));
                    if (dist > _config.goalReachedDistance)
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

            if (_avatar != null)
            {
                Gizmos.color = new Color(0, 1, 0, 0.2f);
                Gizmos.DrawWireSphere(transform.position, _avatar.PersonalSpace);
                Gizmos.color = new Color(0, 0, 1, 0.15f);
                Gizmos.DrawWireSphere(transform.position, _avatar.InteractionRadius);
            }
        }

        #endregion
    }
}