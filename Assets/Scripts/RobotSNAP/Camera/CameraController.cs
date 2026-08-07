// CameraController.cs
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using RobotSNAP;
using RobotSNAP.Agents;

namespace RobotSNAP.CameraControl
{
    // Structure regroupant les informations d'un mode
    public class CameraModeEntry
    {
        public ICameraMode ModeInstance { get; set; }
        public string DisplayName { get; set; }
        public bool RequiresFocus { get; set; }

        public CameraModeEntry(ICameraMode modeInstance, string displayName, bool requiresFocus)
        {
            ModeInstance = modeInstance;
            DisplayName = displayName;
            RequiresFocus = requiresFocus;
        }
    }

    public class CameraController : MonoBehaviour
    {
        [Header("Camera References")]
        public Camera mainCamera;
        public Transform cameraTarget;
        
        [Header("Camera Settings")]
        public float moveSpeed = 10f;
        public float rotateSpeed = 100f;
        public float zoomSpeed = 50f;
        public float smoothTime = 0.3f;
        public float minDistance = 2f;
        public float maxDistance = 50f;
        public float minHeight = 1f;
        public float maxHeight = 30f;

        [Header("Collision")]
        public LayerMask obstacleMask = -1;
        public float collisionRadius = 0.2f;
        
        [Header("Follow Mode Settings")]
        public List<string> followableTags = new List<string> { "Robot", "Human", "Agent" };
        public List<LayerMask> followableLayers = new List<LayerMask>();
        private List<Transform> _followableTargets = new List<Transform>();
        private int _currentFollowIndex = -1;

        [Header("Follow Settings")]
        public Vector3 followOffset = new Vector3(0, 5, -10);
        public Vector3 topDownOffset = new Vector3(0, 20, 0);
        public Vector3 firstPersonOffset = new Vector3(0, 1.5f, 0.5f);
        public Vector3 thirdPersonOffset = new Vector3(0, 2, 5);
        public Vector3 orbitOffset = new Vector3(0, 5, -10);

        public System.Action<List<Transform>> OnTargetsUpdated;
        public System.Action<Transform> OnFollowTargetChanged;
        
        [Header("Input")]
        public bool enableKeyboardControl = true;
        public KeyCode forwardKey = KeyCode.W;
        public KeyCode backwardKey = KeyCode.S;
        public KeyCode leftKey = KeyCode.A;
        public KeyCode rightKey = KeyCode.D;
        public KeyCode upKey = KeyCode.Q;
        public KeyCode downKey = KeyCode.E;
        public KeyCode rotateKey = KeyCode.Mouse1;
        public KeyCode fastMoveKey = KeyCode.LeftShift;
        
        [Header("Split View")]
        public List<Camera> splitViewCameras = new List<Camera>();
        public GameObject splitViewContainer;
        
        public event System.Action OnViewChanged;
        
        public enum CameraMode
        {
            Free,
            TopDown,
            FirstPerson,
            ThirdPerson,
            Orbit,
            MultiTarget,
            Cinematic
        }
        
        private CameraMode _currentModeEnum = CameraMode.Free;
        private ICameraMode _currentMode;
        // Dictionnaire unique : clé = enum, valeur = entrée (instance + métadonnées)
        private Dictionary<CameraMode, CameraModeEntry> _modeEntries;
        public IEnumerable<KeyValuePair<CameraMode, CameraModeEntry>> ModeEntries => _modeEntries;
        
        // Variables d’état partagées
        public Vector3 velocity;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private float _currentDistance;
        private float _currentHeight;
        private float _currentRotationY;
        private float _currentRotationX;
        private bool _isRotating = false;
        private Vector3 _lastMousePosition;
        private Transform _currentFollowTarget;
        private Coroutine _cinematicCoroutine;
        private bool _isSplitView = false;
        
        #region Properties for modes
        public Vector3 TargetPosition { get => _targetPosition; set => _targetPosition = value; }
        public Quaternion TargetRotation { get => _targetRotation; set => _targetRotation = value; }
        public float CurrentDistance { get => _currentDistance; set => _currentDistance = value; }
        public float CurrentHeight { get => _currentHeight; set => _currentHeight = value; }
        public float CurrentRotationY { get => _currentRotationY; set => _currentRotationY = value; }
        public float CurrentRotationX { get => _currentRotationX; set => _currentRotationX = value; }
        public bool IsRotating { get => _isRotating; set => _isRotating = value; }
        public Vector3 LastMousePosition { get => _lastMousePosition; set => _lastMousePosition = value; }
        public Transform CurrentFollowTarget { get => _currentFollowTarget; set => _currentFollowTarget = value; }
        public bool IsSplitView { get => _isSplitView; set => _isSplitView = value; }
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            Initialize();
        }
        
        private void LateUpdate()
        {
            if (mainCamera == null) return;
            _currentMode?.Update(this, Time.unscaledDeltaTime);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                int direction = Input.GetKey(KeyCode.LeftShift) ? -1 : 1;
                CycleFollowTarget(direction);
            }
            UpdateWallVisibility();
        }

        private void UpdateWallVisibility()
        {
            // Récupère tous les colliders proches de la caméra
            // Collider[] hitColliders = Physics.OverlapSphere(mainCamera.transform.position, 5f, obstacleMask);
            // foreach (var col in hitColliders)
            // {
            //     // Active un indicateur sur le mur (ex: un enfant avec un renderer)
            //     var wallIndicator = col.GetComponentInChildren<WallIndicator>();
            //     if (wallIndicator != null)
            //         wallIndicator.Show();
            // }
            // Désactiver les indicateurs trop loin ? (à gérer avec un système de pooling ou de durée)
        }
        
        #endregion
        
        #region Initialization
        
        private void Initialize()
        {
            if (mainCamera == null)
                mainCamera = Camera.main;
            
            if (mainCamera != null)
            {
                _targetPosition = mainCamera.transform.position;
                _targetRotation = mainCamera.transform.rotation;
                _currentDistance = Vector3.Distance(mainCamera.transform.position, Vector3.zero);
                
                Vector3 euler = mainCamera.transform.eulerAngles;
                _currentRotationY = euler.y;
                _currentRotationX = euler.x;
            }
            
            if (splitViewContainer != null) splitViewContainer.SetActive(false);
            
            // Création du dictionnaire unique
            _modeEntries = new Dictionary<CameraMode, CameraModeEntry>
            {
                { CameraMode.Free, new CameraModeEntry(new FreeCameraMode(), "Free", false) },
                { CameraMode.TopDown, new CameraModeEntry(new TopDownMode(), "Top", false) },
                { CameraMode.FirstPerson, new CameraModeEntry(new FirstPersonMode(), "First", true) },
                { CameraMode.ThirdPerson, new CameraModeEntry(new ThirdPersonMode(), "Third", true) },
                { CameraMode.Orbit, new CameraModeEntry(new OrbitMode(), "Orbit", false) },
                // { CameraMode.MultiTarget, new CameraModeEntry(new MultiTargetMode(), "Multi", false) },
                // { CameraMode.Cinematic, new CameraModeEntry(new CinematicMode(), "Cinematic", false) }
            };

            velocity = Vector3.zero;
            
            SetCameraMode(CameraMode.Free);
        }
        
        #endregion
        
        #region Public Methods
        
        public void SetCameraMode(int mode) => SetCameraMode((CameraMode)mode);
        
        public void SetCameraMode(string modeName)
        {
            if (System.Enum.TryParse(modeName, out CameraMode mode))
                SetCameraMode(mode);
        }
        
        public void SetCameraMode(CameraMode mode)
        {
            if (!_modeEntries.TryGetValue(mode, out var entry))
            {
                Debug.LogWarning($"Mode {mode} not found.");
                return;
            }
            if (_currentMode != null) _currentMode.Exit(this);
            _currentModeEnum = mode;
            _currentMode = entry.ModeInstance;
            _currentMode.Enter(this);
            OnViewChanged?.Invoke();
            Debug.Log($"[CameraController] Mode changed to: {mode}");
        }
        
        public CameraMode GetCurrentMode() => _currentModeEnum;
        public string GetCurrentModeName() => _currentModeEnum.ToString();
        
        public string GetCurrentDisplayName()
        {
            if (_modeEntries.TryGetValue(_currentModeEnum, out var entry))
                return entry.DisplayName;
            return _currentModeEnum.ToString();
        }
        
        public bool GetModeRequiresFocus(CameraMode mode)
        {
            return _modeEntries.TryGetValue(mode, out var entry) && entry.RequiresFocus;
        }
        
        public void ResetToDefaultView()
        {
            SetCameraMode(CameraMode.Free);
            OnViewChanged?.Invoke();
        }
        
        public void SetSplitView(bool enable, int mode)
        {
            _isSplitView = enable;
            if (splitViewContainer != null) splitViewContainer.SetActive(enable);
            if (enable)
                SetupSplitView(mode);
            else
            {
                foreach (var cam in splitViewCameras)
                    if (cam != null && cam != mainCamera) cam.gameObject.SetActive(false);
                mainCamera.rect = new Rect(0, 0, 1, 1);
            }
            OnViewChanged?.Invoke();
        }
        
        private void SetupSplitView(int mode) { /* Garder le code existant inchangé */ }
        private void SetupCameraForRect(int index, Rect rect) { /* Garder le code existant inchangé */ }
        
        public void FocusOnAgent(int agentId)
        {
            GameObject[] agents = GameObject.FindGameObjectsWithTag("Human");
            foreach (var agent in agents)
            {
                var humanComponent = agent.GetComponent<HumanAgent>();
                if (humanComponent != null && humanComponent.agentId == agentId)
                {
                    SetCameraMode(CameraMode.Orbit);
                    _currentFollowTarget = agent.transform;
                    _currentRotationY = agent.transform.eulerAngles.y + 180f;
                    _currentRotationX = 30f;
                    OnViewChanged?.Invoke();
                    return;
                }
            }
            Debug.LogWarning($"[CameraController] Agent with ID {agentId} not found");
        }
        
        public void CycleToNextView()
        {
            int nextMode = ((int)_currentModeEnum + 1) % System.Enum.GetValues(typeof(CameraMode)).Length;
            SetCameraMode(nextMode);
        }
        
        public void SetCameraTransform(Vector3 position, Quaternion rotation)
        {
            SetCameraMode(CameraMode.Free);
            _targetPosition = position;
            _targetRotation = rotation;
            Vector3 euler = rotation.eulerAngles;
            _currentRotationY = euler.y;
            _currentRotationX = euler.x;
            OnViewChanged?.Invoke();
        }
        
        public void StartCinematicSequence(Transform[] waypoints, float duration)
        {
            if (_cinematicCoroutine != null) StopCoroutine(_cinematicCoroutine);
            _cinematicCoroutine = StartCoroutine(CinematicSequence(waypoints, duration));
        }
        
        private IEnumerator CinematicSequence(Transform[] waypoints, float duration)
        {
            SetCameraMode(CameraMode.Cinematic);
            var cinematicMode = _currentMode as CinematicMode;
            if (cinematicMode != null)
            {
                yield return cinematicMode.PlaySequence(this, waypoints, duration);
            }
            SetCameraMode(CameraMode.Free);
        }
        
        public void RefreshFollowableTargets()
        {
            _followableTargets.Clear();
            foreach (string tag in followableTags)
            {
                GameObject[] objects = GameObject.FindGameObjectsWithTag(tag);
                foreach (GameObject obj in objects)
                {
                    Transform targetTransform = obj.transform;
                    if (tag == "Robot")
                    {
                        Transform baseLink = obj.GetComponent<Robot>()?.RobotTransform;
                        if (baseLink != null)
                            targetTransform = baseLink;
                        else
                            Debug.LogWarning($"Robot {obj.name} has no movable child – using parent.");
                    }
                    if (!_followableTargets.Contains(targetTransform))
                        _followableTargets.Add(targetTransform);
                }
            }
            foreach (LayerMask layer in followableLayers) { /* placeholder */ }
            _followableTargets.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
            OnTargetsUpdated?.Invoke(_followableTargets);
        }
        
        public void SetFollowTarget(Transform target)
        {
            if (target == null) return;
            _currentFollowTarget = target;
            _currentFollowIndex = _followableTargets.IndexOf(target);
            _currentRotationY = target.eulerAngles.y + 180f;
            _currentRotationX = 25f;
            OnFollowTargetChanged?.Invoke(target);
            Debug.Log($"[CameraController] Following target: {target.name}");
        }
        
        public void CycleFollowTarget(int direction)
        {
            if (_followableTargets.Count == 0)
            {
                RefreshFollowableTargets();
                if (_followableTargets.Count == 0) return;
            }
            int newIndex = _currentFollowIndex + direction;
            if (newIndex >= _followableTargets.Count) newIndex = 0;
            else if (newIndex < 0) newIndex = _followableTargets.Count - 1;
            SetFollowTarget(_followableTargets[newIndex]);
        }
        
        public List<Transform> GetFollowableTargets()
        {
            if (_followableTargets.Count == 0) RefreshFollowableTargets();
            return new List<Transform>(_followableTargets);
        }
        
        public Transform GetCurrentFollowTarget() => _currentFollowTarget;
        public bool IsSplitViewActive() => _isSplitView;

        /// <summary>
        /// Ajuste une position désirée pour éviter les obstacles en effectuant un raycast depuis un point d'origine.
        /// </summary>
        /// <param name="desiredPos">Position souhaitée de la caméra.</param>
        /// <param name="origin">Point de départ du raycast (généralement la cible suivie).</param>
        /// <param name="radius">Rayon de la sphère de collision (évite les coins).</param>
        /// <param name="adjustedPos">Position ajustée (la plus proche possible de desiredPos sans traverser).</param>
        /// <returns>True si un obstacle a été rencontré, false sinon.</returns>
        public bool AdjustPositionForCollision(Vector3 desiredPos, Vector3 origin, float radius, out Vector3 adjustedPos)
        {
            adjustedPos = desiredPos;
            Vector3 direction = (desiredPos - origin).normalized;
            float distance = Vector3.Distance(origin, desiredPos);

            // Lance un spherecast depuis l'origine vers la position désirée.
            if (Physics.SphereCast(origin, radius, direction, out RaycastHit hit, distance, obstacleMask))
            {
                // On place la caméra juste avant l'obstacle, en retirant un petit offset pour éviter le z-fighting.
                float safeDistance = Mathf.Max(0.1f, hit.distance - radius);
                adjustedPos = origin + direction * safeDistance;
                return true;
            }
            return false;
        }
        
        #endregion
    }
    
    // public class HumanAgent : MonoBehaviour
    // {
    //     public int agentId;
    // }
}