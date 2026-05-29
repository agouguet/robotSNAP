// CameraController.cs
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using RobotSNAP;

namespace RobotSNAP.CameraControl
{
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
        public LayerMask obstacleMask = -1; // tous les layers par défaut, à assigner dans l'inspecteur
        public float collisionRadius = 0.2f;   // rayon pour un SphereCast (plus précis)
        
        [Header("Follow Mode Settings")]
        public List<string> followableTags = new List<string> { "Robot", "Human", "Agent" };
        public List<LayerMask> followableLayers = new List<LayerMask>();
        private List<Transform> _followableTargets = new List<Transform>();
        private int _currentFollowIndex = -1;

        [Header("Follow Settings")]
        public Vector3 followOffset = new Vector3(0, 5, -10);
        public Vector3 topDownOffset = new Vector3(0, 20, 0);
        public Vector3 firstPersonOffset = new Vector3(0, 1.5f, 0.5f);
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
            Orbit,
            MultiTarget,
            Cinematic
        }
        
        private CameraMode _currentModeEnum = CameraMode.Free;
        private ICameraMode _currentMode;
        private Dictionary<CameraMode, ICameraMode> _modes;
        
        // Variables d’état partagées (utilisées par certains modes)
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
        
        #region Properties for modes (accès aux états internes)
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
        
        private void Start()
        {
            Initialize();
        }
        
        private void LateUpdate()
        {
            if (mainCamera == null) return;
            _currentMode?.Update(this, Time.deltaTime);
        }

        private void Update()
        {
            // Cycle through follow targets with Tab (uniquement en mode Follow)
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                int direction = Input.GetKey(KeyCode.LeftShift) ? -1 : 1;
                CycleFollowTarget(direction);
            }
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
            
            // Instanciation des modes
            _modes = new Dictionary<CameraMode, ICameraMode>
            {
                { CameraMode.Free, new FreeCameraMode() },
                { CameraMode.TopDown, new TopDownMode() },
                { CameraMode.FirstPerson, new FirstPersonMode() },
                { CameraMode.Orbit, new OrbitMode() },
                { CameraMode.MultiTarget, new MultiTargetMode() },
                { CameraMode.Cinematic, new CinematicMode() }
            };

            velocity = Vector3.zero;
            
            // Mode par défaut
            SetCameraMode(CameraMode.Free);
        }
        
        #endregion
        
        #region Public Methods (API inchangée)
        
        public void SetCameraMode(int mode) => SetCameraMode((CameraMode)mode);
        
        public void SetCameraMode(string modeName)
        {
            if (System.Enum.TryParse(modeName, out CameraMode mode))
                SetCameraMode(mode);
        }
        
        public void SetCameraMode(CameraMode mode)
        {
            if (_currentMode != null) _currentMode.Exit(this);
            _currentModeEnum = mode;
            _currentMode = _modes[mode];
            _currentMode.Enter(this);
            OnViewChanged?.Invoke();
            Debug.Log($"[CameraController] Mode changed to: {mode}");
        }
        
        public CameraMode GetCurrentMode() => _currentModeEnum;
        public string GetCurrentModeName() {return _currentModeEnum.ToString();}
        
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

                    // Si c'est un robot, on cherche l'enfant "base_link" (ou tout autre nom)
                    if (tag == "Robot")
                    {
                        Transform baseLink = obj.GetComponent<Robot>()?.RobotTransform;
                        if (baseLink != null)
                            targetTransform = baseLink;
                        else
                            Debug.LogWarning($"Robot {obj.name} has no 'base_link' child – using parent.");
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
        
        #endregion
    }
    
    // Mock class for HumanAgent (compatibilité)
    public class HumanAgent : MonoBehaviour
    {
        public int agentId;
    }
}