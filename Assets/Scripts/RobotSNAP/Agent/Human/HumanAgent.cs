using UnityEngine;
using System.Collections.Generic;
using RobotSNAP.Core;
using RobotSNAP.Agents.Movement.Interfaces;

namespace RobotSNAP.Agents
{
    public class HumanAgent : BaseAgent, IHumanController
    {
        [Header("Human Specific")]
        public HumanConfig humanConfig;
        public int agentId = 0;
        public bool isStatic = false;
        public float perceptionRadius = 5f;

        [Header("Avatar")]
        public GameObject[] avatars;
        private static List<GameObject> _avatarsList;
        private GameObject _avatarObject;

        [Header("Current State")]
        public Vector2 currentDestination;
        public Vector3 currentVelocity3D;
        public bool hasDestination = false;

        // Composants
        public HumanManager humanManager;
        private HumanMovement _movement;
        private Animator _animator;
        private Supervisor _supervisor;
        private bool _wasPlaying = true;
        private bool _wasPaused;

        // Ordered route: the agent walks from the first point to the last one, then applies its end behavior.
        private readonly HumanRouteWalker _walker = new();
        private bool _followingRoute;

        // What happens once the last point of the route is reached.
        public HumanEndBehavior endBehavior = HumanEndBehavior.Stay;

        // Group walking: the group owns the route and everybody holds a slot around its reference point.
        private HumanGroup _group;
        private Vector2 _formationOffset;
        private HumanPoolManager _poolManager;

        // Per-agent copy of the shared HumanConfig, created only when a scenario overrides speed or controller.
        private HumanConfig _scenarioConfig;

        // Guards a route against agents wedged against geometry.
        private float _stalledTime;
        private const float StallSpeedThreshold = 0.08f;
        private const float StallSecondsBeforeAdvance = 10f;

        // Formation yielding: a member about to be walked through steps aside, then re-forms.
        private const float YieldDistance = 0.7f;
        private const float YieldHoldSeconds = 0.8f;
        private const float YieldLookAheadRadius = 4f;
        private readonly List<Vector2> _yieldNeighbourPositions = new();
        private readonly List<Vector2> _yieldNeighbourVelocities = new();
        private readonly List<int> _yieldNeighbourIds = new();
        private Vector2 _yieldOffset;
        private float _yieldUntil;

        // Propriétés héritées de BaseAgent
        public override Vector3 Position => transform.position;
        public override Quaternion Rotation => transform.rotation;
        public override Vector3 Forward => transform.forward;
        public override Vector3 Velocity => currentVelocity3D;

        // Propriétés pour IHumanController
        public bool HasDestination => hasDestination;
        public Vector3 CurrentGoal => _currentGoal;
        public float CurrentSpeed => _currentSpeed;
        public string CurrentBehavior => _currentBehavior;
        public HumanEndBehavior EndBehavior => endBehavior;
        public IReadOnlyList<Vector3> RoutePoints => _walker.Points;
        /// <summary>True while the agent walks as part of a group. Nobody in the group is a leader.</summary>
        public bool IsGroupMember => _group != null;

        /// <summary>True while the member has stepped out of its slot to let somebody pass.</summary>
        public bool IsYielding => Time.time < _yieldUntil;

        /// <summary>Lateral offset added to its slot while the member yields; zero otherwise.</summary>
        public Vector2 YieldOffset => IsYielding ? _yieldOffset : Vector2.zero;

        /// <summary>
        /// Speed the movement controller regulates in open space. Group following needs it to aim ahead
        /// of a formation slot by the exact distance the controller uses to ramp its speed up.
        /// </summary>
        public float CruiseSpeed => humanConfig != null ? Mathf.Max(0.1f, humanConfig.desiredSpeed) : 1f;

        /// <summary>Distance before its goal at which the controller starts slowing this agent down.</summary>
        public float SlowDownDistance => humanConfig != null ? Mathf.Max(0.2f, humanConfig.slowDownDistance) : 1.5f;

        /// <summary>Physical ceiling of the movement controller, used to bound a catch-up speed.</summary>
        public float MaxSpeed => humanConfig != null ? Mathf.Max(CruiseSpeed, humanConfig.maxSpeed) : 1.5f;

        // ==================== Unity Lifecycle ====================
        private void Awake()
        {
            _supervisor = Supervisor.Instance;
            InitializeComponents();
            InitializeAvatar();
            InitializeAnimator();
        }

        private void Start()
        {
            _movement?.Initialize(this, humanConfig);
        }

        private void Update()
        {
            bool isPaused = (_supervisor ??= Supervisor.Instance) != null && _supervisor.IsPaused;

            // Déléguer la pause au mouvement
            if (_movement != null)
                _movement.SetPlaying(!isPaused);

            if (isPaused)
            {
                if (_animator != null) _animator.speed = 0f;
                return;
            }
            else
            {
                if (_animator != null && _animator.speed == 0f)
                    _animator.speed = 1f;
            }

            // Any member may advance the group; the group keeps that to once per frame.
            UpdateFormationYield();
            _group?.UpdateGroup();

            AdvanceRouteIfReached();

            // Mise à jour de l'animation
            UpdateAnimation();
        }

        // FixedUpdate est vide car tout le mouvement est géré par HumanMovement
        private void FixedUpdate()
        {
            // Ne rien faire ici
        }

        #region Initialization

        private void InitializeComponents()
        {
            _movement = GetComponent<HumanMovement>();
            if (_movement == null)
                _movement = gameObject.AddComponent<HumanMovement>();

            // Le Rigidbody est géré par HumanMovement, on ne le touche pas ici
        }

        private void InitializeAvatar()
        {
            if (_avatarsList == null || _avatarsList.Count == 0)
                _avatarsList = new List<GameObject>(avatars);

            if (_avatarObject == null && _avatarsList.Count > 0)
            {
                int randomIndex = Random.Range(0, _avatarsList.Count);
                GameObject avatarPrefab = _avatarsList[randomIndex];
                _avatarsList.RemoveAt(randomIndex);
                if (avatarPrefab != null)
                    _avatarObject = Instantiate(avatarPrefab, transform.position, transform.rotation, transform);
            }
        }

        private void InitializeAnimator()
        {
            _animator = GetComponentInChildren<Animator>();
            if (_animator == null) return;

            if (humanConfig != null && humanConfig.animationController != null)
                _animator.runtimeAnimatorController = humanConfig.animationController;

            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        #endregion

        #region Animation

        private void UpdateAnimation()
        {
            if (_animator == null || !_animator.enabled || humanConfig == null) return;

            Vector3 localVelocity = transform.InverseTransformDirection(currentVelocity3D);
            float forward = localVelocity.z / humanConfig.animationSmoothing;
            float strafe = localVelocity.x / humanConfig.animationSmoothing;
            bool isIdle = currentVelocity3D.magnitude < humanConfig.idleSpeedThreshold;

            _animator.SetFloat("Forward", forward);
            _animator.SetFloat("Strafe", strafe);
            _animator.SetBool("Idling", isIdle);
            _animator.speed = Mathf.Clamp(currentVelocity3D.magnitude, 0.5f, 2f);
        }

        #endregion

        #region IHumanController Implementation

        public void Initialize() { }

        public override void SetGoal(Vector3 goal)
        {
            _walker.Clear();
            _followingRoute = false;
            SetRouteGoal(goal);
        }

        private void SetRouteGoal(Vector3 goal)
        {
            _currentGoal = goal;
            _hasGoal = true;
            currentDestination = new Vector2(goal.x, goal.z);
            hasDestination = true;
            _movement?.SetGoal(currentDestination);
        }

        /// <summary>
        /// Replaces the route with the given ordered points. The agent walks through every point,
        /// from the first to the last one, then applies <see cref="endBehavior"/>.
        /// </summary>
        public void SetGoals(IEnumerable<Vector3> goals)
        {
            _walker.EndBehavior = endBehavior;
            _walker.SetRoute(goals);
            _stalledTime = 0f;
            // A group member never walks a route of its own: the group owns the route.
            _followingRoute = _walker.HasCurrent && !IsGroupMember;
            if (_followingRoute)
                SetRouteGoal(_walker.Current);
            else if (_walker.Points.Count == 0)
                ClearGoal();
        }

        private void AdvanceToNextGoal()
        {
            _stalledTime = 0f;
            if (_walker.AdvanceNext())
            {
                CompleteRoute();
                return;
            }

            SetRouteGoal(_walker.Current);
        }

        private void AdvanceRouteIfReached()
        {
            if (!_followingRoute || !hasDestination)
                return;

            float arrivalRadius = ArrivalRadius;
            float distance = Vector2.Distance(Position2D, currentDestination);
            if (distance <= arrivalRadius)
            {
                AdvanceToNextGoal();
                return;
            }

            // An agent wedged against geometry must not freeze the whole route.
            if (currentVelocity3D.magnitude <= StallSpeedThreshold)
            {
                _stalledTime += Time.deltaTime;
                if (_stalledTime >= StallSecondsBeforeAdvance && distance > arrivalRadius * 2f)
                {
                    Debug.LogWarning($"[HumanAgent] {AgentName} stalled before " +
                                     $"({currentDestination.x:0.##}, {currentDestination.y:0.##}); " +
                                     "skipping to the next route point.");
                    AdvanceToNextGoal();
                }
            }
            else
            {
                _stalledTime = 0f;
            }
        }

        /// <summary>Arrival tolerance: the agent radius matters more than the raw goal distance.</summary>
        private float ArrivalRadius =>
            Mathf.Max(humanConfig != null ? humanConfig.goalReachedDistance : 0.2f, radius * 1.6f);

        /// <summary>Called when the walker really ran out of points (a looping route never does).</summary>
        private void CompleteRoute()
        {
            if (endBehavior == HumanEndBehavior.Disappear)
            {
                _group?.ApplyEndBehavior(HumanEndBehavior.Disappear);
                Disappear();
                return;
            }

            _followingRoute = false;
            hasDestination = false;
            _hasGoal = false;
            Stop();
        }

        public void SetPlay(bool isPlaying) => _movement?.SetPlaying(isPlaying);

        // ==================== End behavior, groups and lifecycle ====================

        public void SetEndBehavior(HumanEndBehavior behavior)
        {
            endBehavior = behavior;
            _walker.EndBehavior = behavior;
        }

        public void SetPoolManager(HumanPoolManager poolManager) => _poolManager = poolManager;

        /// <summary>
        /// Applies the movement settings the scenario authored for this agent: its walking speed and, when the
        /// entry names one, the controller its group should use.
        ///
        /// The controllers read their parameters from <see cref="HumanConfig"/>, not from the fields of
        /// <see cref="BaseAgent"/>, so writing only the base field left the Speed value of the editor without
        /// any effect on a walking human. The asset is copied per agent the first time it is overridden, so a
        /// scenario can never change the shared asset of everybody else.
        /// </summary>
        public void ApplyScenarioMovement(float speed, string controller = null)
        {
            float requestedSpeed = Mathf.Clamp(speed, 0.5f, 5f);
            SetSpeed(requestedSpeed);

            bool wantsController = HumanMovementControllerParser.TryParse(controller, out MovementControllerType parsed);
            bool speedDiffers = humanConfig != null && !Mathf.Approximately(humanConfig.desiredSpeed, requestedSpeed);
            bool controllerDiffers = humanConfig != null && wantsController && humanConfig.controllerType != parsed;
            if (!speedDiffers && !controllerDiffers)
                return;

            HumanConfig effective = TakeScenarioConfig();
            if (effective == null)
                return;

            effective.desiredSpeed = requestedSpeed;
            effective.maxSpeed = Mathf.Max(effective.maxSpeed, requestedSpeed);
            if (wantsController)
                effective.controllerType = parsed;

            _movement?.Initialize(this, effective);
        }

        /// <summary>The per-agent copy of the shared configuration, created on first override.</summary>
        private HumanConfig TakeScenarioConfig()
        {
            if (humanConfig == null)
                return null;
            if (_scenarioConfig != null)
                return _scenarioConfig;

            _scenarioConfig = Instantiate(humanConfig);
            _scenarioConfig.name = $"{humanConfig.name} (scenario)";
            humanConfig = _scenarioConfig;
            return _scenarioConfig;
        }

        private void OnDestroy()
        {
            if (_scenarioConfig != null)
                Destroy(_scenarioConfig);
        }

        /// <summary>
        /// Joins a walking group. Nobody leads: the group carries the route and every member, this one
        /// included, holds a slot around its reference point. The layout itself is applied by
        /// <see cref="HumanGroup.PlaceMembersAtSpawn"/> once the whole group exists.
        /// </summary>
        public void JoinGroup(HumanGroup group, Vector2 formationOffset)
        {
            _group = group;
            _formationOffset = formationOffset;

            // The route belongs to the group now, so this agent stops walking one of its own. A group without
            // a shared route is left alone: its members keep the route they were configured with.
            if (group != null && group.IsRouteActive)
            {
                _walker.Clear();
                _followingRoute = false;
                _stalledTime = 0f;
            }

            SetGroupDestination(Position2D);
        }

        public void SetFormationOffset(Vector2 offset) => _formationOffset = offset;

        /// <summary>Slot this member holds in its group's frame, relative to the group reference.</summary>
        public Vector2 FormationOffset => _formationOffset;

        /// <summary>
        /// Turns the agent on the spot, used at spawn so it looks at its first objective instead of keeping
        /// the direction its prefab was authored with.
        /// </summary>
        public void FaceTowards(Vector2 direction)
        {
            if (direction.sqrMagnitude < 0.0001f)
                return;

            if (_movement != null)
            {
                _movement.SnapRotation(direction);
                return;
            }

            transform.rotation = Quaternion.LookRotation(new Vector3(direction.x, 0f, direction.y), Vector3.up);
        }

        /// <summary>
        /// Decides whether this member should leave its slot. Keeping formation is a preference, not a
        /// constraint: when somebody outside the group is about to walk through it, the member steps aside for
        /// a moment and re-forms afterwards. That is what real pedestrians in a group do, and it stops a rigid
        /// slot from turning a face-to-face encounter into a standstill for the whole group.
        /// </summary>
        private void UpdateFormationYield()
        {
            if (_group == null || humanManager == null)
                return;

            Vector2 heading = currentDestination - Position2D;
            if (heading.sqrMagnitude < 0.0001f)
                heading = new Vector2(transform.forward.x, transform.forward.z);
            if (heading.sqrMagnitude < 0.0001f)
                return;
            heading.Normalize();

            float radius = humanConfig != null ? humanConfig.agentRadius : 0.25f;
            humanManager.GetNeighbors(
                agentId,
                Position2D,
                YieldLookAheadRadius,
                _yieldNeighbourPositions,
                _yieldNeighbourVelocities,
                _yieldNeighbourIds);

            HumanAvoidance.Prediction worst = HumanAvoidance.Prediction.None;
            for (int index = 0; index < _yieldNeighbourIds.Count; index++)
            {
                // Companions hold their own slots: only strangers push a member out of formation.
                if (_group.ContainsAgent(_yieldNeighbourIds[index]))
                    continue;

                HumanAvoidance.Prediction prediction = HumanAvoidance.Predict(
                    Position2D,
                    Velocity2D,
                    radius,
                    _yieldNeighbourPositions[index],
                    _yieldNeighbourVelocities[index],
                    radius,
                    heading);
                if (prediction.IsConflict && prediction.Urgency > worst.Urgency)
                    worst = prediction;
            }

            // The robot is not one of the neighbours the manager tracks, but it is the one obstacle a member
            // must never stand in front of: it gets the same yield decision, with its own radius.
            RobotObservation robot = _movement != null ? _movement.ObserveRobot() : RobotObservation.None;
            if (robot.IsVisible)
            {
                HumanAvoidance.Prediction prediction = HumanAvoidance.Predict(
                    Position2D,
                    Velocity2D,
                    radius,
                    robot.Position,
                    robot.Velocity,
                    robot.Radius,
                    heading);
                if (prediction.IsConflict && prediction.Urgency > worst.Urgency)
                    worst = prediction;
            }

            if (!worst.IsConflict)
                return;

            _yieldOffset = HumanAvoidance.RightOf(heading) * (worst.Side * YieldDistance * worst.Urgency);
            _yieldUntil = Time.time + YieldHoldSeconds;
        }

        public void LeaveGroup()
        {
            _group = null;
            _formationOffset = Vector2.zero;
            _movement?.SetCruiseSpeedOverride(0f);
        }

        /// <summary>Steering target imposed by the group leader; does not touch the route.</summary>
        public void SetGroupDestination(Vector2 destination)
        {
            currentDestination = destination;
            hasDestination = true;
            _hasGoal = true;
            _currentGoal = new Vector3(destination.x, 0f, destination.y);
            _movement?.SetGoal(destination);
        }

        /// <summary>
        /// Cruise speed imposed by the group for this frame, so a follower can close the gap on its
        /// leader instead of being capped at exactly the leader's speed.
        /// </summary>
        public void SetGroupCruiseSpeed(float speed) => _movement?.SetCruiseSpeedOverride(speed);

        /// <summary>End behavior propagated by the group leader to its followers.</summary>
        public void SetGroupEndBehavior(HumanEndBehavior behavior)
        {
            endBehavior = behavior;
            if (behavior == HumanEndBehavior.Disappear)
                Disappear();
        }

        /// <summary>Removes the agent from the simulation once its route is complete.</summary>
        public void Disappear()
        {
            _followingRoute = false;
            hasDestination = false;
            _hasGoal = false;
            _stalledTime = 0f;
            Stop();

            _group?.Remove(this);
            LeaveGroup();

            if (_poolManager != null)
            {
                _poolManager.ReturnHuman(gameObject);
                return;
            }

            gameObject.SetActive(false);
        }

        public override void Reset()
        {
            _group?.Remove(this);
            LeaveGroup();
            _walker.Clear();
            _followingRoute = false;
            _stalledTime = 0f;
            hasDestination = false;
            _hasGoal = false;
            currentVelocity3D = Vector3.zero;
            _movement?.Reset();
            if (_animator != null) _animator.Rebind();
        }

        public void FullReset()
        {
            Reset();
            if (_avatarObject != null) _avatarObject.SetActive(true);
        }

        public void SetColor(Color color)
        {
            var renderer = GetComponentInChildren<Renderer>();
            if (renderer != null) renderer.material.color = color;
        }

        public Vector3 GetPosition() => Position;
        public Vector3 GetVelocity() => currentVelocity3D;
        public Vector3 GetCurrentPosition3D() => Position;
        public Vector2 GetCurrentPosition2D() => Position2D;
        public void SetVelocity(Vector3 velocity) => currentVelocity3D = velocity;

        #endregion

        #region Overrides of BaseAgent methods

        public override void SetSpeed(float speed)
        {
            base.SetSpeed(speed);
        }

        public override void SetBehavior(string behavior)
        {
            base.SetBehavior(behavior);
        }

        public override void SetInteractionRadius(float radius)
        {
            base.SetInteractionRadius(radius);
        }

        public override void SetPersonalSpace(float space)
        {
            base.SetPersonalSpace(space);
        }

        public override void SetAssertiveness(float value)
        {
            base.SetAssertiveness(value);
        }

        public override void SetReactionTime(float time)
        {
            base.SetReactionTime(time);
        }

        public override void Stop()
        {
            currentVelocity3D = Vector3.zero;
            _movement?.Stop();
        }

        public override void ClearGoal()
        {
            _walker.Clear();
            _followingRoute = false;
            _stalledTime = 0f;
            hasDestination = false;
            _hasGoal = false;
            _currentGoal = Vector3.zero;
            _movement?.SetGoal(Vector2.zero);
        }

        public override void SetActive(bool active)
        {
            gameObject.SetActive(active);
            if (!active) Stop();
        }

        #endregion

        #region Public Methods

        public void SetHumanManager(HumanManager manager)
        {
            humanManager = manager;
            _movement?.SetHumanManager(manager);
        }

        public void SetDestination(Vector2 destination) => SetGoal(destination);
        public void SetAgentId(int id) => agentId = id;
        public void SetAgentName(string name) => agentName = name;
        public HumanMovement GetMovement() => _movement;
        public Animator GetAnimator() => _animator;
        public Rigidbody GetRigidbody() => null; // Plus utilisé, on retourne null

        #endregion

        #region Debug

        private void OnDrawGizmosSelected()
        {
            if (hasDestination)
            {
                Gizmos.color = Color.yellow;
                Vector3 dest3D = new Vector3(currentDestination.x, 0.1f, currentDestination.y);
                Gizmos.DrawLine(transform.position, dest3D);
                Gizmos.DrawWireSphere(dest3D, 0.2f);
            }
            Gizmos.color = new Color(0, 1, 0, 0.2f);
            Gizmos.DrawWireSphere(transform.position, personalSpace);
            Gizmos.color = new Color(0, 0, 1, 0.15f);
            Gizmos.DrawWireSphere(transform.position, interactionRadius);
        }

        [ContextMenu("Reset Human")]
        private void EditorReset() => Reset();

        [ContextMenu("Full Reset Human")]
        private void EditorFullReset() => FullReset();

        #endregion

        public override string ToString()
        {
            return $"{AgentName} (Behavior: {_currentBehavior}, Speed: {_currentSpeed}, Goal: {(hasDestination ? _currentGoal.ToString() : "none")})";
        }
    }
}
