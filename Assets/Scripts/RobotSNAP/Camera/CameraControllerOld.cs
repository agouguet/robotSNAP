using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using RobotSNAP.Core;
using RobotSNAP.Agents;

namespace RobotSNAP.UI
{
    public class CameraController : MonoBehaviour
    {
        [Header("Camera References")]
        public Camera mainCamera;
        public Transform cameraTarget;
        public Transform robotTarget;
        
        [Header("Camera Settings")]
        public float moveSpeed = 10f;
        public float rotateSpeed = 100f;
        public float zoomSpeed = 50f;
        public float smoothTime = 0.3f;
        public float minDistance = 2f;
        public float maxDistance = 50f;
        public float minHeight = 1f;
        public float maxHeight = 30f;
        
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

        // Événement pour notifier l'UI des cibles disponibles
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
            FollowRobot,
            TopDown,
            FirstPerson,
            OrbitRobot,
            MultiTarget,
            Cinematic
        }
        
        private CameraMode _currentMode = CameraMode.Free;
        private Vector3 _velocity = Vector3.zero;
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
        private Supervisor _supervisor;
        
        #region Unity Lifecycle
        
        private void Start()
        {
            Initialize();
        }
        
        private void LateUpdate()
        {
            if (mainCamera == null) return;
            
            HandleKeyboardInput();
            UpdateCameraPosition();
        }

        private void Update()
        {
            // Cycle through follow targets with Tab
            if (_currentMode == CameraMode.FollowRobot && Input.GetKeyDown(KeyCode.Tab))
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
            
            _supervisor = Supervisor.Instance;
            
            if (_supervisor != null)
            {
                FindRobotTarget();
            }
            
            // Initialize split view container
            if (splitViewContainer != null)
                splitViewContainer.SetActive(false);
        }
        
        private void FindRobotTarget()
        {
            // Try to find robot in scene
            GameObject robot = GameObject.FindGameObjectWithTag("Robot");
            if (robot == null)
                robot = GameObject.Find("Robot");
            
            if (robot != null)
                robotTarget = robot.transform;
        }
        
        #endregion
        
        #region Input Handling
        
        private void HandleKeyboardInput()
        {
            if (!enableKeyboardControl || _currentMode != CameraMode.Free) return;
            
            float speed = moveSpeed;
            if (Input.GetKey(fastMoveKey))
                speed *= 3f;
            
            Vector3 move = Vector3.zero;
            
            if (Input.GetKey(forwardKey)) move += mainCamera.transform.forward;
            if (Input.GetKey(backwardKey)) move -= mainCamera.transform.forward;
            if (Input.GetKey(rightKey)) move += mainCamera.transform.right;
            if (Input.GetKey(leftKey)) move -= mainCamera.transform.right;
            if (Input.GetKey(upKey)) move += Vector3.up;
            if (Input.GetKey(downKey)) move -= Vector3.up;
            
            if (move != Vector3.zero)
            {
                _targetPosition += move.normalized * speed * Time.deltaTime;
            }
            
            // Mouse rotation
            if (Input.GetKeyDown(rotateKey))
            {
                _isRotating = true;
                _lastMousePosition = Input.mousePosition;
            }
            
            if (Input.GetKeyUp(rotateKey))
            {
                _isRotating = false;
            }
            
            if (_isRotating)
            {
                Vector3 delta = Input.mousePosition - _lastMousePosition;
                _lastMousePosition = Input.mousePosition;
                
                _currentRotationY += delta.x * rotateSpeed * Time.deltaTime;
                _currentRotationX -= delta.y * rotateSpeed * Time.deltaTime;
                _currentRotationX = Mathf.Clamp(_currentRotationX, -90f, 90f);
                
                _targetRotation = Quaternion.Euler(_currentRotationX, _currentRotationY, 0);
            }
            
            // Zoom with scroll wheel
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0)
            {
                if (mainCamera.orthographic)
                {
                    mainCamera.orthographicSize -= scroll * zoomSpeed * Time.deltaTime;
                    mainCamera.orthographicSize = Mathf.Clamp(mainCamera.orthographicSize, 1f, 50f);
                }
                else
                {
                    Vector3 forward = mainCamera.transform.forward;
                    _targetPosition += forward * scroll * zoomSpeed;
                }
            }
        }
        
        #endregion
        
        #region Camera Update
        
        private void UpdateCameraPosition()
        {
            switch (_currentMode)
            {
                case CameraMode.Free:
                    UpdateFreeCamera();
                    break;
                    
                case CameraMode.FollowRobot:
                    UpdateFollowCamera();
                    break;
                    
                case CameraMode.TopDown:
                    UpdateTopDownCamera();
                    break;
                    
                case CameraMode.FirstPerson:
                    UpdateFirstPersonCamera();
                    break;
                    
                case CameraMode.OrbitRobot:
                    UpdateOrbitCamera();
                    break;
                    
                case CameraMode.Cinematic:
                    // Handled by coroutine
                    break;
                    
                default:
                    UpdateFreeCamera();
                    break;
            }
        }
        
        private void UpdateFreeCamera()
        {
            mainCamera.transform.position = Vector3.SmoothDamp(
                mainCamera.transform.position,
                _targetPosition,
                ref _velocity,
                smoothTime
            );
            
            if (!_isRotating)
            {
                mainCamera.transform.rotation = Quaternion.Slerp(
                    mainCamera.transform.rotation,
                    _targetRotation,
                    smoothTime * 2
                );
            }
            else
            {
                mainCamera.transform.rotation = _targetRotation;
            }
        }
        
        private void UpdateFollowCamera()
        {
            if (_currentFollowTarget == null)
            {
                RefreshFollowableTargets();
                if (_followableTargets.Count > 0)
                    _currentFollowTarget = _followableTargets[0];
                else
                    return;
            }
            
            Vector3 targetPos = _currentFollowTarget.position + followOffset;
            
            mainCamera.transform.position = Vector3.SmoothDamp(
                mainCamera.transform.position,
                targetPos,
                ref _velocity,
                smoothTime
            );
            
            mainCamera.transform.LookAt(_currentFollowTarget);
        }
        
        private void UpdateTopDownCamera()
        {
            if (_currentFollowTarget == null)
            {
                if (robotTarget != null)
                    _currentFollowTarget = robotTarget;
                else
                    return;
            }
            
            Vector3 targetPos = _currentFollowTarget.position + topDownOffset;
            
            mainCamera.transform.position = Vector3.Lerp(
                mainCamera.transform.position,
                targetPos,
                smoothTime * 2
            );
            
            mainCamera.transform.rotation = Quaternion.Slerp(
                mainCamera.transform.rotation,
                Quaternion.Euler(90, 0, 0),
                smoothTime
            );
        }
        
        private void UpdateFirstPersonCamera()
        {
            if (_currentFollowTarget == null)
            {
                if (robotTarget != null)
                    _currentFollowTarget = robotTarget;
                else
                    return;
            }
            
            Vector3 targetPos = _currentFollowTarget.position + 
                _currentFollowTarget.rotation * firstPersonOffset;
            
            mainCamera.transform.position = targetPos;
            mainCamera.transform.rotation = _currentFollowTarget.rotation;
        }
        
        private void UpdateOrbitCamera()
        {
            if (_currentFollowTarget == null)
            {
                if (robotTarget != null)
                    _currentFollowTarget = robotTarget;
                else
                    return;
            }
            
            // Handle orbit input
            if (Input.GetKey(rotateKey))
            {
                Vector3 delta = Input.mousePosition - _lastMousePosition;
                _lastMousePosition = Input.mousePosition;
                
                _currentRotationY += delta.x * rotateSpeed * Time.deltaTime;
                _currentRotationX -= delta.y * rotateSpeed * Time.deltaTime;
                _currentRotationX = Mathf.Clamp(_currentRotationX, 10f, 80f);
            }
            
            _lastMousePosition = Input.mousePosition;
            
            // Calculate orbit position
            Quaternion rotation = Quaternion.Euler(_currentRotationX, _currentRotationY, 0);
            Vector3 offset = rotation * orbitOffset;
            Vector3 targetPos = _currentFollowTarget.position + offset;
            
            mainCamera.transform.position = Vector3.SmoothDamp(
                mainCamera.transform.position,
                targetPos,
                ref _velocity,
                smoothTime
            );
            
            mainCamera.transform.LookAt(_currentFollowTarget);
        }
        
        #endregion
        
        #region Public Methods
        
        public void SetCameraMode(int mode)
        {
            SetCameraMode((CameraMode)mode);
        }
        
        public void SetCameraMode(string modeName)
        {
            if (System.Enum.TryParse(modeName, out CameraMode mode))
            {
                SetCameraMode(mode);
            }
        }
        
        public void SetCameraMode(CameraMode mode)
        {
            _currentMode = mode;
            
            if (_cinematicCoroutine != null)
            {
                StopCoroutine(_cinematicCoroutine);
                _cinematicCoroutine = null;
            }
            
            switch (mode)
            {
                case CameraMode.FollowRobot:
                    RefreshFollowableTargets();
                    if (_followableTargets.Count > 0)
                    {
                        SetFollowTarget(_followableTargets[0]);
                    }
                    break;
                    
                case CameraMode.TopDown:
                case CameraMode.FirstPerson:
                case CameraMode.OrbitRobot:
                    _currentFollowTarget = robotTarget;
                    if (robotTarget != null)
                    {
                        _currentRotationY = robotTarget.eulerAngles.y;
                        _currentRotationX = 30f;
                    }
                    break;
                    
                case CameraMode.Free:
                    _targetPosition = mainCamera.transform.position;
                    _targetRotation = mainCamera.transform.rotation;
                    break;
            }
            
            OnViewChanged?.Invoke();
            Debug.Log($"[CameraController] Mode changed to: {mode}");
        }
        
        public void ResetToDefaultView()
        {
            _currentMode = CameraMode.Free;
            
            if (robotTarget != null)
            {
                _targetPosition = robotTarget.position + new Vector3(-10, 8, -10);
                Vector3 direction = (robotTarget.position - _targetPosition).normalized;
                _targetRotation = Quaternion.LookRotation(direction);
                
                Vector3 euler = _targetRotation.eulerAngles;
                _currentRotationY = euler.y;
                _currentRotationX = euler.x;
            }
            else
            {
                _targetPosition = new Vector3(0, 10, -15);
                _targetRotation = Quaternion.Euler(25, 0, 0);
                _currentRotationY = 0;
                _currentRotationX = 25;
            }
            
            OnViewChanged?.Invoke();
        }
        
        public void SetSplitView(bool enable, int mode)
        {
            _isSplitView = enable;
            
            if (splitViewContainer != null)
                splitViewContainer.SetActive(enable);
            
            if (enable)
            {
                SetupSplitView(mode);
            }
            else
            {
                // Return to single camera
                if (splitViewCameras != null)
                {
                    foreach (var cam in splitViewCameras)
                    {
                        if (cam != null && cam != mainCamera)
                            cam.gameObject.SetActive(false);
                    }
                }
                mainCamera.rect = new Rect(0, 0, 1, 1);
            }
            
            OnViewChanged?.Invoke();
        }
        
        private void SetupSplitView(int mode)
        {
            // Disable all split cameras first
            foreach (var cam in splitViewCameras)
            {
                if (cam != null)
                    cam.gameObject.SetActive(false);
            }
            
            switch (mode)
            {
                case 0: // 2-View Horizontal
                    mainCamera.rect = new Rect(0, 0, 0.5f, 1f);
                    SetupCameraForRect(0, new Rect(0.5f, 0, 0.5f, 1f));
                    break;
                    
                case 1: // 2-View Vertical
                    mainCamera.rect = new Rect(0, 0.5f, 1f, 0.5f);
                    SetupCameraForRect(0, new Rect(0, 0, 1f, 0.5f));
                    break;
                    
                case 2: // 3-View
                    mainCamera.rect = new Rect(0, 0.5f, 0.5f, 0.5f);
                    SetupCameraForRect(0, new Rect(0.5f, 0.5f, 0.5f, 0.5f));
                    SetupCameraForRect(1, new Rect(0, 0, 1f, 0.5f));
                    break;
                    
                case 3: // 4-View
                    mainCamera.rect = new Rect(0, 0.5f, 0.5f, 0.5f);
                    SetupCameraForRect(0, new Rect(0.5f, 0.5f, 0.5f, 0.5f));
                    SetupCameraForRect(1, new Rect(0, 0, 0.5f, 0.5f));
                    SetupCameraForRect(2, new Rect(0.5f, 0, 0.5f, 0.5f));
                    break;
            }
        }
        
        private void SetupCameraForRect(int index, Rect rect)
        {
            if (index < splitViewCameras.Count && splitViewCameras[index] != null)
            {
                var cam = splitViewCameras[index];
                cam.gameObject.SetActive(true);
                cam.rect = rect;
                
                // Set different views
                switch (index)
                {
                    case 0:
                        cam.transform.position = robotTarget != null ? 
                            robotTarget.position + new Vector3(0, 15, 0) : new Vector3(0, 15, 0);
                        cam.transform.rotation = Quaternion.Euler(90, 0, 0);
                        break;
                    case 1:
                        cam.transform.position = robotTarget != null ? 
                            robotTarget.position + new Vector3(-15, 5, 0) : new Vector3(-15, 5, 0);
                        Transform lookTarget = robotTarget != null ? robotTarget : null;
                        if (lookTarget != null)
                            cam.transform.LookAt(lookTarget);
                        else
                            cam.transform.LookAt(Vector3.zero);
                        break;
                }
            }
        }
        
        public void FocusOnRobot()
        {
            if (robotTarget == null)
            {
                FindRobotTarget();
                if (robotTarget == null) return;
            }
            
            _currentMode = CameraMode.OrbitRobot;
            _currentFollowTarget = robotTarget;
            _currentRotationY = robotTarget.eulerAngles.y + 180f;
            _currentRotationX = 30f;
            
            OnViewChanged?.Invoke();
        }
        
        public void FocusOnAgent(int agentId)
        {
            // Find agent by ID
            GameObject[] agents = GameObject.FindGameObjectsWithTag("Human");
            foreach (var agent in agents)
            {
                var humanComponent = agent.GetComponent<HumanAgent>();
                if (humanComponent != null && humanComponent.agentId == agentId)
                {
                    _currentMode = CameraMode.OrbitRobot;
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
            int nextMode = ((int)_currentMode + 1) % System.Enum.GetValues(typeof(CameraMode)).Length;
            SetCameraMode(nextMode);
        }
        
        public void SetCameraTransform(Vector3 position, Quaternion rotation)
        {
            _currentMode = CameraMode.Free;
            _targetPosition = position;
            _targetRotation = rotation;
            
            Vector3 euler = rotation.eulerAngles;
            _currentRotationY = euler.y;
            _currentRotationX = euler.x;
            
            OnViewChanged?.Invoke();
        }
        
        public void StartCinematicSequence(Transform[] waypoints, float duration)
        {
            if (_cinematicCoroutine != null)
                StopCoroutine(_cinematicCoroutine);
            
            _cinematicCoroutine = StartCoroutine(CinematicSequence(waypoints, duration));
        }
        
        private IEnumerator CinematicSequence(Transform[] waypoints, float duration)
        {
            _currentMode = CameraMode.Cinematic;
            
            foreach (var waypoint in waypoints)
            {
                Vector3 startPos = mainCamera.transform.position;
                Quaternion startRot = mainCamera.transform.rotation;
                float elapsed = 0;
                
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / duration;
                    t = Mathf.SmoothStep(0, 1, t);
                    
                    mainCamera.transform.position = Vector3.Lerp(startPos, waypoint.position, t);
                    mainCamera.transform.rotation = Quaternion.Slerp(startRot, waypoint.rotation, t);
                    
                    yield return null;
                }
            }
            
            _cinematicCoroutine = null;
            _currentMode = CameraMode.Free;
            OnViewChanged?.Invoke();
        }

        public void RefreshFollowableTargets()
        {
            _followableTargets.Clear();
            
            // Recherche par tag
            foreach (string tag in followableTags)
            {
                GameObject[] objects = GameObject.FindGameObjectsWithTag(tag);
                foreach (GameObject obj in objects)
                {
                    if (!_followableTargets.Contains(obj.transform))
                        _followableTargets.Add(obj.transform);
                }
            }
            
            // Recherche par layer (optionnel)
            foreach (LayerMask layer in followableLayers)
            {
                // Implémentation selon vos besoins
            }
            
            // Trier par nom pour une meilleure présentation
            _followableTargets.Sort((a, b) => 
                string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
            
            OnTargetsUpdated?.Invoke(_followableTargets);
        }

        public void SetFollowTarget(Transform target)
        {
            if (target == null) return;
            
            _currentFollowTarget = target;
            _currentFollowIndex = _followableTargets.IndexOf(target);
            
            // Ajuster la rotation initiale
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
            if (newIndex >= _followableTargets.Count)
                newIndex = 0;
            else if (newIndex < 0)
                newIndex = _followableTargets.Count - 1;
            
            SetFollowTarget(_followableTargets[newIndex]);
        }

        public List<Transform> GetFollowableTargets()
        {
            if (_followableTargets.Count == 0)
                RefreshFollowableTargets();
            
            return new List<Transform>(_followableTargets);
        }

        public Transform GetCurrentFollowTarget()
        {
            return _currentFollowTarget;
        }
        
        public CameraMode GetCurrentMode()
        {
            return _currentMode;
        }
        
        public bool IsSplitView()
        {
            return _isSplitView;
        }
        
        #endregion
    }
    
    // Mock class for HumanAgent - implement according to your project
    // public class HumanAgent : MonoBehaviour
    // {
    //     public int agentId;
    // }
}