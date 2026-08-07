using UnityEngine;
using System.Collections.Generic;
using RobotSNAP.Agents.Movement.Controllers;
using RobotSNAP.Agents.Movement.Interfaces;
using RobotSNAP.Core;
using UnityEngine.AI;

namespace RobotSNAP.Agents
{
    /// <summary>
    /// Contrôleur de mouvement pour un agent humain utilisant un IMovementController (SFM, ONNX, Hybride).
    /// Gère le chemin NavMesh, la détection des voisins (sans doublon) et l'application du mouvement.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class HumanMovement : MonoBehaviour
    {
        [Header("Fallback Settings (si config manquante)")]
        [SerializeField] private float _maxSpeed = 2.0f;
        [SerializeField] private float _angularSpeed = 180f;

        // Références principales
        private HumanManager _humanManager;
        private int _agentId;
        private HumanAgent _avatar;
        private IMovementController _controller;
        private HumanConfig _config;
        private Rigidbody _rb;

        // Navigation NavMesh
        private NavMeshPath _navMeshPath;
        private Vector3[] _pathCorners;
        private float _lastPathUpdate;
        private Vector2 _currentGoalPoint; // prochain waypoint

        // État local
        private Vector2 _currentVelocity;
        private Vector2 _currentPosition;
        private bool _isPlaying = true;
        private MovementControllerType _currentControllerType;

        // Détection des voisins (positions/vitesses)
        private readonly List<Vector2> _neighborPositions = new List<Vector2>();
        private readonly List<Vector2> _neighborVelocities = new List<Vector2>();

        // Cache pour éviter les allocations
        private Collider[] _overlapCache = new Collider[50];
        private HashSet<int> _processedAgents = new HashSet<int>(); // réutilisé

        // Propriétés publiques
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
            {
                Move();
            }
        }

        #endregion

        #region Initialization

        private void InitializeComponents()
        {
            // Rigidbody : on contrôle entièrement la vitesse
            _rb = GetComponent<Rigidbody>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
            _rb.useGravity = false;
            _rb.constraints = RigidbodyConstraints.FreezeRotationX |
                              RigidbodyConstraints.FreezeRotationZ |
                              RigidbodyConstraints.FreezePositionY;
            _rb.linearDamping = 0f;
            _rb.angularDamping = 0f;

            _navMeshPath = new NavMeshPath();
            _pathCorners = new Vector3[0];
            SetupCollider();
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
                // valeurs par défaut pour un humain standard
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
                Vector2 currentVel = new Vector2(_rb.linearVelocity.x, _rb.linearVelocity.z);
                _humanManager.RegisterAgent(_agentId, currentPos);
                _humanManager.UpdateAgent(_agentId, currentPos, currentVel);
            }
        }
   

        private void OnDisable()
        {
            if (_humanManager != null && _agentId != 0)
            {
                _humanManager.UnregisterAgent(_agentId);
            }
        }

        private void OnDestroy()
        {
            // Sécurité supplémentaire (même si OnDisable est appelé avant, ça ne fait pas de mal)
            if (_humanManager != null && _agentId != 0)
            {
                _humanManager.UnregisterAgent(_agentId);
            }
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

        #endregion

        #region Movement Logic

        private void Move()
        {
            _currentPosition = new Vector2(transform.position.x, transform.position.z);
            _currentVelocity = new Vector2(_rb.linearVelocity.x, _rb.linearVelocity.z);

            // 1. Mettre à jour les données dans le manager
            _humanManager?.UpdateAgent(_agentId, _currentPosition, _currentVelocity);

            // 2. Mise à jour du chemin NavMesh
            UpdateNavMeshPath();

            // 3. Récupérer les voisins via le manager (plus de OverlapSphere)
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

            // 4. Calcul de la vélocité par le contrôleur (inchangé)
            Vector2 desiredVelocity = _controller.ComputeVelocity(
                _currentPosition,
                _currentVelocity,
                _currentGoalPoint,
                _neighborPositions.ToArray(),
                _neighborVelocities.ToArray(),
                Time.fixedDeltaTime
            );

            // Application directe (le contrôleur gère déjà les limites)
            Vector3 finalVelocity3D = new Vector3(desiredVelocity.x, 0, desiredVelocity.y);

            // Limite de sécurité (si le contrôleur dépasse)
            float maxSpeed = _config != null ? _config.maxSpeed : _maxSpeed;
            if (finalVelocity3D.sqrMagnitude > maxSpeed * maxSpeed)
                finalVelocity3D = finalVelocity3D.normalized * maxSpeed;

            _rb.linearVelocity = finalVelocity3D;

            // Mise à jour de la vélocité dans l'avatar
            _avatar?.SetVelocity(_rb.linearVelocity);

            // Rotation vers la direction du mouvement
            UpdateRotation(desiredVelocity);
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
                    // Si le chemin est incomplet, on prend la destination directe
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

        #region Neighbor Detection (optimisée sans doublon)

        private void DetectNeighbors()
        {
            // Vide les listes et le HashSet de la frame précédente
            _neighborPositions.Clear();
            _neighborVelocities.Clear();
            _processedAgents.Clear();

            if (_config == null) return;

            float radius = _config.perceptionRadiusAgent;
            Vector3 center = new Vector3(_currentPosition.x, 0.5f, _currentPosition.y);

            int count = Physics.OverlapSphereNonAlloc(center, radius, _overlapCache);

            for (int i = 0; i < count; i++)
            {
                Collider col = _overlapCache[i];
                if (col.transform == transform) continue; // ignorer soi-même

                // --- Détection d'un HumanAgent ---
                HumanAgent otherAvatar = col.GetComponentInParent<HumanAgent>();
                if (otherAvatar != null && otherAvatar != _avatar && otherAvatar.gameObject.activeSelf)
                {
                    int id = otherAvatar.gameObject.GetInstanceID();
                    if (_processedAgents.Contains(id)) continue;
                    _processedAgents.Add(id);

                    Vector2 otherPos = otherAvatar.GetCurrentPosition2D();
                    float dist = Vector2.Distance(_currentPosition, otherPos);
                    if (dist < radius && dist > 0.01f)
                    {
                        _neighborPositions.Add(otherPos);
                        _neighborVelocities.Add(new Vector2(
                            otherAvatar.currentVelocity3D.x,
                            otherAvatar.currentVelocity3D.z
                        ));
                    }
                    continue;
                }

                // --- Détection d'un Robot ---
                Robot robot = col.GetComponentInParent<Robot>();
                if (robot != null && robot.gameObject.activeSelf)
                {
                    int id = robot.gameObject.GetInstanceID();
                    if (_processedAgents.Contains(id)) continue;
                    _processedAgents.Add(id);

                    Vector3 robotPos = robot.Position;
                    float dist = Vector2.Distance(_currentPosition, new Vector2(robotPos.x, robotPos.z));
                    if (dist < radius)
                    {
                        _neighborPositions.Add(new Vector2(robotPos.x, robotPos.z));
                        _neighborVelocities.Add(new Vector2(robot.Velocity.x, robot.Velocity.z));
                    }
                    // float dist = Vector2.Distance(_currentPosition, robotPos);
                    // if (dist < radius)
                    // {
                    //     _neighborPositions.Add(robotPos);
                    //     _neighborVelocities.Add(new Vector2(robot.Velocity.x, robot.Velocity.z));
                    // }
                }
            }
        }

        #endregion

        #region Rotation

        private void UpdateRotation(Vector2 movementDirection)
        {
            if (movementDirection.sqrMagnitude < 0.001f) return;

            float angle = Mathf.Atan2(movementDirection.x, movementDirection.y) * Mathf.Rad2Deg;
            Quaternion targetRotation = Quaternion.Euler(0, angle, 0);
            float angularSpeed = _config != null ? _config.angularSpeed : _angularSpeed;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                angularSpeed * Time.fixedDeltaTime
            );
        }

        #endregion

        #region Public API

        public void SetHumanManager(HumanManager manager) { _humanManager = manager; }

        public void SetPlaying(bool playing)
        {
            _isPlaying = playing;
            if (!_isPlaying)
            {
                _rb.linearVelocity = Vector3.zero;
                _avatar?.SetVelocity(Vector3.zero);
            }
        }

        public void Stop()
        {
            _rb.linearVelocity = Vector3.zero;
            _currentVelocity = Vector2.zero;
            if (_avatar != null) _avatar.SetVelocity(Vector3.zero);
        }

        public void SetGoal(Vector2 goal)
        {
            if (_avatar != null)
            {
                _avatar.currentDestination = goal;
                _avatar.hasDestination = true;
            }
            // Force une mise à jour du chemin au prochain FixedUpdate
            _lastPathUpdate = -_config.pathUpdateInterval;
        }

        public void Reset()
        {
            _controller?.Reset();
            _rb.linearVelocity = Vector3.zero;
            _avatar?.SetVelocity(Vector3.zero);
            _pathCorners = new Vector3[0];
            _currentGoalPoint = Vector2.zero;
            _neighborPositions.Clear();
            _neighborVelocities.Clear();
            _processedAgents.Clear();
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