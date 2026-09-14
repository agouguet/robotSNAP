using UnityEngine;
using System.Collections.Generic;
using RobotSNAP.Agents.Movement.Controllers;
using RobotSNAP.Agents.Movement.Interfaces;
using RobotSNAP.Core;
using RobotSNAP.Core.Scenario;
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

        // Scenario grid path (see RobotSNAP.Core.Scenario.ScenarioNavigation)
        private readonly List<Vector2> _plannedPath = new List<Vector2>();
        private Vector2 _plannedGoal;
        private bool _scenarioPathInUse;

        /// <summary>Destination move that makes the current path obsolete, in metres.</summary>
        private const float GoalChangeTolerance = 0.5f;

        /// <summary>How far the agent may drift from its path before it is replanned, in metres.</summary>
        private const float PathDeviationTolerance = 0.6f;

        /// <summary>Delay before a plan postponed by the frame budget is asked again, in seconds.</summary>
        private const float DeferredReplanDelay = 0.03f;

        // State
        private Vector2 _currentVelocity;
        private Vector2 _currentPosition;
        private bool _isPlaying = true;
        private MovementControllerType _currentControllerType;
        private float _cruiseSpeedOverride;

        // Neighbors
        private readonly List<Vector2> _neighborPositions = new List<Vector2>();
        private readonly List<Vector2> _neighborVelocities = new List<Vector2>();

        /// <summary>
        /// Smallest radius the neighbour query uses. The social repulsion only matters under a metre or two, but
        /// anticipatory avoidance has to see a conflict coming from further away, so the query is widened.
        /// </summary>
        private const float AnticipationLookAhead = 4.5f;

        // Obstacles
        private readonly List<Vector2> _tempObstacles = new List<Vector2>();

        // Robot to yield to (cached, refreshed periodically)
        private Robot _robot;
        private float _nextRobotLookup;
        private const float RobotLookupInterval = 2f;

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

        /// <summary>
        /// Turns the agent on the spot. The controller only rotates while it is walking, so a freshly spawned
        /// agent used to keep facing whatever direction its prefab was authored with.
        /// </summary>
        public void SnapRotation(Vector2 direction)
        {
            if (direction.sqrMagnitude < 0.0001f)
                return;

            Quaternion target = Quaternion.LookRotation(new Vector3(direction.x, 0f, direction.y), Vector3.up);
            transform.rotation = target;
            if (_rb != null)
                _rb.rotation = target;
        }

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
                // Anticipation needs to see further ahead than the repulsion does: a conflict four metres away
                // is already worth steering for, and this query is what bounds what the controller can see.
               _humanManager.GetNeighbors(
                    _agentId,
                    _currentPosition,
                    Mathf.Max(_config.perceptionRadiusAgent, AnticipationLookAhead),
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
                ObserveRobot(),
                Time.fixedDeltaTime,
                _cruiseSpeedOverride
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

        /// <summary>
        /// Describes the robot for the movement controller so humans yield to it.
        /// The lookup is cached because finding it every physics step would be wasteful.
        /// </summary>
        /// <summary>
        /// Snapshot of the robot for the movement controller. Public because the group also needs it: a member
        /// steps out of its slot for the robot the same way it does for a stranger.
        /// </summary>
        public RobotObservation ObserveRobot()
        {
            if (_robot == null && Time.time >= _nextRobotLookup)
            {
                _nextRobotLookup = Time.time + RobotLookupInterval;
                _robot = FindAnyObjectByType<Robot>();
            }

            if (_robot == null || !_robot.gameObject.activeInHierarchy)
                return RobotObservation.None;

            Vector3 position = _robot.Position;
            Vector3 velocity = _robot.Velocity;
            return new RobotObservation(
                true,
                new Vector2(position.x, position.z),
                new Vector2(velocity.x, velocity.z),
                Mathf.Max(0.25f, _robot.Radius));
        }

        private void UpdateNavMeshPath()
        {
            if (_config == null) return;
            if (Time.time - _lastPathUpdate < _config.pathUpdateInterval) return;
            _lastPathUpdate = Time.time;

            Vector2 destination = _avatar.currentDestination;

            // The scenario's walkable grid is the one the editor drew on: when it is loaded, the agents follow
            // exactly the trajectories the author validated instead of a NavMesh built from the scene.
            if (ScenarioNavigation.IsAvailable)
            {
                // A path that is still valid is not replanned: in a group walking an open corridor, every member
                // would otherwise run a full grid search five times a second for a one-metre leg.
                if (CanKeepScenarioPath(destination))
                {
                    RefreshScenarioPathEnd(destination);
                    return;
                }

                bool planned = ScenarioNavigation.TryPlan(_currentPosition, destination, _plannedPath);

                // The first path of an agent is never postponed: without one it would steer straight through
                // the walls until the frame budget lets it through.
                if (!planned && !_scenarioPathInUse)
                {
                    List<Vector2> unbudgeted = ScenarioNavigation.Plan(_currentPosition, destination);
                    if (unbudgeted != null && unbudgeted.Count >= 2)
                    {
                        _plannedPath.Clear();
                        _plannedPath.AddRange(unbudgeted);
                        planned = true;
                    }
                }

                if (planned)
                {
                    ApplyPlannedPath(_plannedPath);
                    _plannedGoal = destination;
                    _scenarioPathInUse = true;
                    return;
                }

                // The frame's search budget is spent: keep the current path and ask again shortly, instead of
                // making the whole crowd replan in the same frame.
                if (_scenarioPathInUse && _pathCorners is { Length: >= 2 })
                {
                    _lastPathUpdate = Time.time - _config.pathUpdateInterval + DeferredReplanDelay;
                    return;
                }
            }

            _scenarioPathInUse = false;
            Vector3 start3D = new Vector3(_currentPosition.x, 0, _currentPosition.y);
            Vector3 goal3D = new Vector3(destination.x, 0, destination.y);

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

        /// <summary>
        /// True while the path already computed still leads where the agent is going: its destination has not
        /// moved by more than <see cref="GoalChangeTolerance"/>, and the agent has not been pushed off the
        /// polyline by more than <see cref="PathDeviationTolerance"/>.
        /// </summary>
        private bool CanKeepScenarioPath(Vector2 destination)
        {
            if (!_scenarioPathInUse || _pathCorners is not { Length: >= 2 })
                return false;

            if ((destination - _plannedGoal).sqrMagnitude > GoalChangeTolerance * GoalChangeTolerance)
                return false;

            return ScenarioNavigation.IsPathStillValid(_currentPosition, _pathCorners, PathDeviationTolerance);
        }

        /// <summary>
        /// Re-anchors a path that is kept: the current position replaces the stale start and the destination
        /// replaces the last corner, so a slowly moving goal — a formation slot — is followed without paying
        /// for a search.
        /// </summary>
        private void RefreshScenarioPathEnd(Vector2 destination)
        {
            Vector2 target = ScenarioNavigation.TryProjectToWalkable(
                destination,
                ScenarioNavigation.SnapRadius,
                out Vector2 walkable)
                ? walkable
                : destination;

            _pathCorners[0] = new Vector3(_currentPosition.x, 0f, _currentPosition.y);
            _pathCorners[^1] = new Vector3(target.x, 0f, target.y);
            _plannedGoal = destination;
            UpdateCurrentGoalPoint();
        }

        /// <summary>Turns a planned polyline into the corner list the steering target is selected from.</summary>
        private void ApplyPlannedPath(IReadOnlyList<Vector2> planned)
        {
            if (_pathCorners == null || _pathCorners.Length != planned.Count)
                _pathCorners = new Vector3[planned.Count];

            for (int index = 0; index < planned.Count; index++)
                _pathCorners[index] = new Vector3(planned[index].x, 0f, planned[index].y);

            UpdateCurrentGoalPoint();
        }

        private void UpdateCurrentGoalPoint()
        {
            // A scenario plan ends on walkable ground, which is not necessarily the authored point: steering
            // towards the authored goal would push the agent into the wall the planner routed around.
            Vector2 destination = _scenarioPathInUse && _pathCorners is { Length: > 0 }
                ? new Vector2(_pathCorners[^1].x, _pathCorners[^1].z)
                : _avatar.currentDestination;

            if (_pathCorners == null || _pathCorners.Length == 0)
            {
                _currentGoalPoint = destination;
                return;
            }

            float destinationReachedDistance = _config != null ? _config.goalReachedDistance : 0.2f;
            float waypointAdvanceDistance = _config != null
                ? Mathf.Max(_config.nextNavMinDistance, _config.slowDownDistance)
                : 1f;
            _currentGoalPoint = HumanPathTargetSelector.SelectTarget(
                _currentPosition,
                destination,
                _pathCorners,
                waypointAdvanceDistance,
                destinationReachedDistance);
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

        /// <summary>
        /// Imposes a cruise speed for the next frames (0 restores the configured one).
        /// Used by group followers that have to close a gap on their leader.
        /// </summary>
        public void SetCruiseSpeedOverride(float speed) => _cruiseSpeedOverride = Mathf.Max(0f, speed);

        public void SetGoal(Vector2 goal)
        {
            if (_avatar != null)
            {
                _avatar.currentDestination = goal;
                _avatar.hasDestination = true;
            }
            // Never expose the reset value (0,0) as a temporary steering target.
            _currentGoalPoint = goal;
            _lastPathUpdate = -_config.pathUpdateInterval;
        }

        public void Reset()
        {
            _controller?.Reset();
            _cruiseSpeedOverride = 0f;
            _rb.linearVelocity = Vector3.zero;
            _currentVelocity = Vector2.zero;
            _avatar?.SetVelocity(Vector3.zero);
            _pathCorners = new Vector3[0];
            _currentGoalPoint = Vector2.zero;
            _plannedPath.Clear();
            _plannedGoal = Vector2.zero;
            _scenarioPathInUse = false;
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
