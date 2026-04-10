using UnityEngine;
using System.Collections.Generic;
using RobotSNAP.Core;

namespace RobotSNAP.Human
{
    public class HumanAvatar : MonoBehaviour, IHumanController
    {
        [Header("Agent Properties")]
        public HumanConfig humanConfig;
        public string agentName;
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
        
        // Propriétés internes pour le système de scénarios
        private float _movementSpeed = 1.2f;
        private string _behavior = "normal";
        private Vector3 _currentGoal;
        private float _interactionRadius = 1.5f;
        private float _personalSpace = 0.8f;
        private float _assertiveness = 0.5f;
        private float _reactionTime = 0.3f;
        
        // Composants
        private HumanMovement _movement;
        private Animator _animator;
        private bool _wasPlaying = true;
        private Rigidbody _rb;
        
        // Propriétés publiques
        public bool HasDestination => hasDestination;
        public Vector3 CurrentGoal => _currentGoal;
        public float CurrentSpeed => _movementSpeed;
        public string CurrentBehavior => _behavior;
        public float InteractionRadius => _interactionRadius;
        public float PersonalSpace => _personalSpace;
        public float Assertiveness => _assertiveness;
        
        #region Unity Lifecycle
        
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
            HandlePlayPause();
            UpdateAnimation();
        }
        
        private void FixedUpdate()
        {
            UpdateMovement();
        }
        
        #endregion
        
        #region Initialization
        
        private void InitializeComponents()
        {
            // Movement component
            _movement = GetComponent<HumanMovement>();
            if (_movement == null)
            {
                _movement = gameObject.AddComponent<HumanMovement>();
            }
            
            // Rigidbody
            _rb = GetComponent<Rigidbody>();
            if (_rb == null)
            {
                _rb = gameObject.AddComponent<Rigidbody>();
                _rb.useGravity = false;
                _rb.mass = 1f;
                _rb.linearDamping = 0.5f;
            }
        }
        
        private void InitializeAvatar()
        {
            if (_avatarsList == null || _avatarsList.Count == 0)
            {
                _avatarsList = new List<GameObject>(avatars);
            }
            
            if (_avatarObject == null && _avatarsList.Count > 0)
            {
                int randomIndex = Random.Range(0, _avatarsList.Count);
                GameObject avatarPrefab = _avatarsList[randomIndex];
                _avatarsList.RemoveAt(randomIndex);
                
                if (avatarPrefab != null)
                {
                    _avatarObject = Instantiate(avatarPrefab, transform.position, transform.rotation, transform);
                }
            }
        }
        
        private void InitializeAnimator()
        {
            _animator = GetComponentInChildren<Animator>();
            if (_animator == null) return;
            
            if (humanConfig != null && humanConfig.animationController != null)
            {
                _animator.runtimeAnimatorController = humanConfig.animationController;
            }
            
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        
        private void UpdateMovement()
        {
            if (_movement == null || !_movement.IsPlaying) return;
            
            // Appliquer la vélocité via Rigidbody
            if (_rb != null)
            {
                _rb.linearVelocity = currentVelocity3D;
            }
            else
            {
                transform.position += currentVelocity3D * Time.fixedDeltaTime;
            }
            
            // Rotation vers la direction du mouvement
            if (currentVelocity3D.magnitude > 0.1f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(currentVelocity3D.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.fixedDeltaTime * 10f);
            }
        }
        
        #endregion
        
        #region Animation
        
        private void HandlePlayPause()
        {
            if (_movement == null) return;
            
            bool isPlaying = _movement.IsPlaying;
            
            if (isPlaying != _wasPlaying && _animator != null)
            {
                _animator.enabled = isPlaying;
                if (isPlaying)
                {
                    _animator.Rebind();
                }
                _wasPlaying = isPlaying;
            }
        }
        
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
        
        public void Initialize() 
        {
            // Déjà initialisé dans Awake/Start
        }
        
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
        
        public void SetPlay(bool isPlaying)
        {
            _movement?.SetPlaying(isPlaying);
        }
        
        public void Reset()
        {
            hasDestination = false;
            currentVelocity3D = Vector3.zero;
            _currentGoal = Vector3.zero;
            _movement?.Reset();
            
            if (_rb != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            
            if (_animator != null)
            {
                _animator.Rebind();
                _animator.Update(0f);
                _animator.speed = 1f;
            }
        }
        
        public void FullReset()
        {
            Reset();
            
            if (_avatarObject != null)
            {
                _avatarObject.SetActive(true);
            }
        }
        
        public void SetInteractionRadius(float radius)
        {
            _interactionRadius = Mathf.Max(0.5f, radius);
            _movement?.SetInteractionRadius(_interactionRadius);
        }
        
        public void SetPersonalSpace(float space)
        {
            _personalSpace = Mathf.Clamp(space, 0.3f, 2f);
            _movement?.SetPersonalSpace(_personalSpace);
        }
        
        public void SetAssertiveness(float value)
        {
            _assertiveness = Mathf.Clamp(value, 0f, 1f);
            _movement?.SetAssertiveness(_assertiveness);
        }
        
        public void SetReactionTime(float time)
        {
            _reactionTime = Mathf.Clamp(time, 0.1f, 1f);
            _movement?.SetReactionTime(_reactionTime);
        }
        
        public void SetSpeed(float speed)
        {
            _movementSpeed = Mathf.Clamp(speed, 0.5f, 3f);
            _movement?.SetSpeed(_movementSpeed);
        }
        
        public void SetBehavior(string behavior)
        {
            _behavior = behavior;
            _movement?.SetBehavior(behavior);
        }
        
        public void SetColor(Color color)
        {
            var renderer = GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = color;
            }
        }
        
        public Vector3 GetPosition() => transform.position;
        
        public Vector3 GetVelocity() => currentVelocity3D;
        
        public Vector3 GetCurrentPosition3D() => transform.position;
        
        public Vector2 GetCurrentPosition2D() => new Vector2(transform.position.x, transform.position.z);
        
        public void SetVelocity(Vector3 velocity)
        {
            currentVelocity3D = velocity;
        }
        
        #endregion
        
        #region Public Methods
        
        public void SetDestination(Vector2 destination)
        {
            SetGoal(destination);
        }
        
        public HumanMovement GetMovement() => _movement;
        
        public Animator GetAnimator() => _animator;
        
        public Rigidbody GetRigidbody() => _rb;
        
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
            
            // Visualiser l'espace personnel
            Gizmos.color = new Color(0, 1, 0, 0.2f);
            Gizmos.DrawWireSphere(transform.position, _personalSpace);
            
            // Visualiser le rayon d'interaction
            Gizmos.color = new Color(0, 0, 1, 0.15f);
            Gizmos.DrawWireSphere(transform.position, _interactionRadius);
        }
        
        [ContextMenu("Reset Human")]
        private void EditorReset()
        {
            Reset();
        }
        
        [ContextMenu("Full Reset Human")]
        private void EditorFullReset()
        {
            FullReset();
        }
        
        #endregion
        
        public override string ToString()
        {
            return $"{agentName ?? gameObject.name} (Behavior: {_behavior}, Speed: {_movementSpeed}, Goal: {(hasDestination ? _currentGoal.ToString() : "none")})";
        }
    }
}