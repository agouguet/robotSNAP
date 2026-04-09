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
        private GameObject _avatarPrefab;
        private GameObject _avatarObject;
        
        [Header("Current State")]
        public Vector2 currentDestination;
        public Vector3 currentVelocity3D;
        public bool hasDestination = false;
        
        public bool HasDestination => hasDestination;
        
        private HumanMovement _movement;
        private Animator _animator;
        private bool _wasPlaying = true;
        
        #region Unity Lifecycle
        
        void Awake()
        {
            _movement = GetComponent<HumanMovement>();
            if (_movement == null)
            {
                _movement = gameObject.AddComponent<HumanMovement>();
            }
            
            InitializeAvatar();
            InitializeAnimator();
            _movement.Initialize(this, humanConfig);
        }
        
        void Update()
        {
            HandlePlayPause();
            UpdateAnimation();
        }
        
        #endregion
        
        #region Initialization
        
        private void InitializeAvatar()
        {
            if (_avatarsList == null || _avatarsList.Count == 0)
            {
                _avatarsList = new List<GameObject>(avatars);
            }
            
            if (_avatarPrefab == null && _avatarsList.Count > 0)
            {
                int randomIndex = Random.Range(0, _avatarsList.Count);
                _avatarPrefab = _avatarsList[randomIndex];
                _avatarsList.RemoveAt(randomIndex);
            }
            
            if (_avatarPrefab != null)
            {
                _avatarObject = Instantiate(_avatarPrefab, transform.position, transform.rotation);
                _avatarObject.transform.parent = transform;
            }
        }
        
        private void InitializeAnimator()
        {
            _animator = GetComponentInChildren<Animator>();
            
            if (_animator == null) return;
            
            if (humanConfig.animationController != null)
            {
                _animator.runtimeAnimatorController = humanConfig.animationController;
            }
            
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
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
            if (_animator == null || !_animator.enabled) return;
            
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
        
        public void Initialize() {}
        
        public void SetGoal(Vector3 goal)
        {
            currentDestination = new Vector2(goal.x, goal.z);
            hasDestination = true;
            _movement?.SetGoal(currentDestination);
        }
        
        public void SetPlay(bool isPlaying)
        {
            _movement?.SetPlaying(isPlaying);
        }
        
        public void Reset()
        {
            hasDestination = false;
            currentVelocity3D = Vector3.zero;
            _movement?.Reset();
            
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
            
            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
        
        public Vector3 GetPosition()
        {
            return transform.position;
        }
        
        public Vector3 GetVelocity()
        {
            return currentVelocity3D;
        }
        
        public Vector3 GetCurrentPosition3D()
        {
            return transform.position;
        }
        
        public Vector2 GetCurrentPosition2D()
        {
            return new Vector2(transform.position.x, transform.position.z);
        }
        
        public void SetVelocity(Vector3 velocity)
        {
            currentVelocity3D = velocity;
        }
        
        #endregion
        
        #region Public Methods
        
        public void SetDestination(Vector2 destination)
        {
            currentDestination = destination;
            hasDestination = true;
            _movement?.SetGoal(destination);
        }
        
        public HumanMovement GetMovement()
        {
            return _movement;
        }
        
        public Animator GetAnimator()
        {
            return _animator;
        }
        
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
        }
        
        #endregion
    }
}