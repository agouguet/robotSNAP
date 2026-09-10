using UnityEngine;
using System.Collections.Generic;
using RobotSNAP.Core;

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
        private bool _wasPlaying = true;
        private bool _wasPaused;

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

        // ==================== Unity Lifecycle ====================
        private void Awake()
        {
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
            bool isPaused = Supervisor.Instance != null && Supervisor.Instance.IsPaused;

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

        public void SetGoal(Vector3 goal)
        {
            _currentGoal = goal;
            currentDestination = new Vector2(goal.x, goal.z);
            hasDestination = true;
            _movement?.SetGoal(currentDestination);
        }

        public void SetGoal(Vector2 goal)
        {
            currentDestination = goal;
            _currentGoal = new Vector3(goal.x, 0, goal.y);
            hasDestination = true;
            _movement?.SetGoal(goal);
        }

        public void SetPlay(bool isPlaying) => _movement?.SetPlaying(isPlaying);

        public override void Reset()
        {
            hasDestination = false;
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

        public void ClearGoal()
        {
            hasDestination = false;
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