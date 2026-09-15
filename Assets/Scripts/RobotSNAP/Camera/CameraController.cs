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
        /// <summary>
        /// What the left mouse button does in the camera view. The view toolbar picks it.
        /// </summary>
        public enum CameraTool
        {
            Select,
            Move,
            Rotate,
            Zoom
        }

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

        [Header("Default Focus")]
        [Tooltip("Hand the focus to the robot as soon as one exists, until the user picks another target.")]
        public bool focusRobotByDefault = true;

        [Header("Interaction")]
        [Tooltip("What the left mouse button does in the view: pick an agent, slide, turn or zoom.")]
        [SerializeField] private CameraTool activeTool = CameraTool.Select;

        public CameraTool ActiveTool => activeTool;
        public event System.Action<CameraTool> OnToolChanged;

        /// <summary>True while an agent is selected. This is the selection, not the camera binding:
        /// see <see cref="IsFollowingTarget"/> for the state the dashboard badges report.</summary>
        public bool IsFollowing => _currentFollowTarget != null;

        /// <summary>
        /// True when the current view really keeps the camera on the selection. A free or top view
        /// can hold a selected agent that the camera ignores, which is why the selection and the
        /// binding are two distinct states and every panel reads this one for its "Following" label.
        /// </summary>
        public bool IsFollowingTarget => _currentFollowTarget != null && ModeUsesTarget(_currentModeEnum);

        /// <summary>The views that bind the camera to the followed agent.</summary>
        public static bool ModeUsesTarget(CameraMode mode) =>
            mode is CameraMode.FirstPerson or CameraMode.ThirdPerson or CameraMode.Orbit;

        /// <summary>
        /// The scenario spawns the robot well after the first frame, so the default focus cannot
        /// be resolved once in Start. Refreshing the target list is what hands it over; this is how
        /// long that refresh may be retried, and how often.
        /// </summary>
        private const float DefaultFocusTimeout = 60f;
        private const float DefaultFocusRetryInterval = 0.5f;

        /// <summary>Base links of the robots among the followable targets. A base link does not carry
        /// the Robot tag itself, so the default focus cannot be found again from the list alone.</summary>
        private readonly List<Transform> _robotTargets = new List<Transform>();
        private bool _defaultFocusApplied;
        private bool _applyingDefaultFocus;

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

        // View-tool input: what the left button does is chosen by the toolbar, so the gesture is
        // tracked here rather than in the modes, which keep their own right-drag and wheel handling.
        private bool _toolDragging;
        private Vector2 _toolDragStart;
        private Vector2 _toolDragLast;
        private bool _toolDragMoved;
        private const float DragThresholdPixels = 4f;

        /// <summary>Zoom asked for by the zoom tool, in metres. The orbit mode consumes it this frame.</summary>
        public float PendingZoom { get; set; }
        
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

            HandleToolInput();
            UpdateWallVisibility();
        }

        #region View tools

        /// <summary>
        /// Applies the gesture the view toolbar selected: pick an agent, slide the view, turn it or
        /// zoom it. A drag that starts over the HUD is ignored so a panel never moves the camera.
        /// </summary>
        private void HandleToolInput()
        {
            if (mainCamera == null) return;

            if (Input.GetKeyDown(KeyCode.Mouse0))
            {
                _toolDragging = !IsPointerOverHud();
                _toolDragStart = Input.mousePosition;
                _toolDragLast = _toolDragStart;
                _toolDragMoved = false;
            }

            if (!_toolDragging)
                return;

            Vector2 mouse = Input.mousePosition;
            Vector2 delta = mouse - _toolDragLast;
            _toolDragLast = mouse;

            if (!_toolDragMoved && (mouse - _toolDragStart).magnitude > DragThresholdPixels)
                _toolDragMoved = true;

            if (_toolDragMoved)
            {
                switch (activeTool)
                {
                    case CameraTool.Move:
                        PanBy(delta);
                        break;
                    case CameraTool.Rotate:
                        RotateBy(delta);
                        break;
                    case CameraTool.Zoom:
                        ZoomByDrag(-delta.y);
                        break;
                }
            }

            if (Input.GetKeyUp(KeyCode.Mouse0))
            {
                _toolDragging = false;

                // A click that never became a drag means "pick what is under the cursor".
                if (!_toolDragMoved && activeTool == CameraTool.Select)
                    PickAgentUnderCursor();
            }
        }

        /// <summary>
        /// Slides the view. Only the free camera owns its position: while an agent is followed the
        /// camera belongs to it, and the user releases it from the agent bar instead.
        /// </summary>
        private void PanBy(Vector2 screenDelta)
        {
            if (_currentFollowTarget != null) return;
            if (_currentModeEnum != CameraMode.Free) return;

            Transform camera = mainCamera.transform;
            float scale = moveSpeed * Time.unscaledDeltaTime * 0.35f;
            Vector3 shift = (-camera.right * screenDelta.x - camera.up * screenDelta.y) * scale;

            _targetPosition = _targetPosition + shift;
        }

        /// <summary>Turns the view: free camera looks around, a followed agent is circled.</summary>
        private void RotateBy(Vector2 screenDelta)
        {
            float speed = rotateSpeed * Time.unscaledDeltaTime * 0.6f;
            _currentRotationY += screenDelta.x * speed;
            _currentRotationX -= screenDelta.y * speed;

            if (_currentFollowTarget != null)
            {
                _currentRotationX = Mathf.Clamp(_currentRotationX, 10f, 80f);
            }
            else
            {
                _currentRotationX = Mathf.Clamp(_currentRotationX, -90f, 90f);
                _targetRotation = Quaternion.Euler(_currentRotationX, _currentRotationY, 0f);
            }
        }

        /// <summary>Pulls the camera closer or pushes it away, without touching the wheel path.</summary>
        private void ZoomByDrag(float amount)
        {
            float distance = amount * zoomSpeed * Time.unscaledDeltaTime * 0.35f;
            if (distance == 0f) return;

            if (_currentFollowTarget != null)
            {
                PendingZoom -= distance;
                return;
            }

            _targetPosition += mainCamera.transform.forward * distance;
        }

        /// <summary>Focuses the agent under the cursor — what the select tool does on a click.</summary>
        private void PickAgentUnderCursor()
        {
            if (Camera.main == null) return;

            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 500f, ~0, QueryTriggerInteraction.Ignore))
                return;

            Robot robot = hit.collider.GetComponentInParent<Robot>();
            if (robot != null)
            {
                SetFollowTarget(robot.RobotTransform);
                return;
            }

            HumanAgent human = hit.collider.GetComponentInParent<HumanAgent>();
            if (human != null)
                SetFollowTarget(human.transform);
        }

        /// <summary>True while the pointer is over a UI Toolkit panel — the HUD must not drive the camera.</summary>
        private static bool IsPointerOverHud()
        {
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }

        #endregion

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

            if (focusRobotByDefault)
                StartCoroutine(FocusRobotWhenAvailable());
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

            // Hand the current pose over to the next mode. A free camera works from TargetPosition, so
            // without this the view jumps back to wherever the last free view was instead of carrying on
            // from here — the incoherence between the follow modes and the free one.
            if (mainCamera != null)
            {
                _targetPosition = mainCamera.transform.position;
                _targetRotation = mainCamera.transform.rotation;
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
            _robotTargets.Clear();
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
                    if (tag == "Robot" && !_robotTargets.Contains(targetTransform))
                        _robotTargets.Add(targetTransform);
                }
            }
            foreach (LayerMask layer in followableLayers) { /* placeholder */ }
            _followableTargets.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
            OnTargetsUpdated?.Invoke(_followableTargets);

            // The list just changed: this is the moment to know whether the default focus can
            // finally be resolved. Runs after the notification so subscribers see the new list.
            ApplyDefaultFocus();
        }

        /// <summary>
        /// Gives the focus to the robot when nothing is focused yet — the simulation should open on
        /// the robot, without the user picking it in the bar first. Called from every target refresh,
        /// so a robot spawned later is picked up as soon as it appears.
        /// </summary>
        /// <returns>True once a robot holds the focus, false while none is available yet.</returns>
        public bool ApplyDefaultFocus()
        {
            if (_defaultFocusApplied) return _currentFollowTarget != null;
            if (_currentFollowTarget != null)
            {
                // Something is already focused: either the user picked it, or the scene started
                // with a focus. The robot must not take it back, so the rule settles here.
                _defaultFocusApplied = true;
                return true;
            }
            if (!focusRobotByDefault || _applyingDefaultFocus || _robotTargets.Count == 0)
                return false;

            Transform robot = _robotTargets[0];
            if (robot == null) return false;

            _applyingDefaultFocus = true;
            _defaultFocusApplied = true;
            SetFollowTarget(robot);

            // A free-fly camera ignores the focus target, so the view would open on whatever the
            // scene camera was looking at. Only the untouched default view is upgraded; a mode
            // the user picked is never replaced.
            if (_currentModeEnum == CameraMode.Free && mainCamera != null)
                SetCameraMode(CameraMode.Orbit);

            _applyingDefaultFocus = false;
            return true;
        }

        /// <summary>
        /// Retries the default focus while the scene is still empty. The scenario spawns the robot
        /// several frames after the UI appears, and nothing else would refresh the target list in
        /// between. Bounded on purpose: it stops at the first focus, and gives up after
        /// <see cref="DefaultFocusTimeout"/> seconds so a map without an agent never keeps it alive.
        /// </summary>
        private IEnumerator FocusRobotWhenAvailable()
        {
            float deadline = Time.unscaledTime + DefaultFocusTimeout;
            while (_currentFollowTarget == null && Time.unscaledTime < deadline)
            {
                RefreshFollowableTargets();
                yield return new WaitForSecondsRealtime(DefaultFocusRetryInterval);
            }
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

        /// <summary>
        /// Hands the camera to that agent and looks at it right away — what the Focus button and the agent
        /// panel do. Following keeps the position, this one re-frames the view on the agent.
        /// </summary>
        public void FocusAgent(Transform target)
        {
            if (target == null) return;

            SetFollowTarget(target);
            SetCameraMode(CameraMode.Orbit);
        }

        /// <summary>
        /// Releases the camera: it keeps its position and becomes free again. A scenario that spawns a
        /// new robot will not grab the focus back, the user asked for no target.
        /// </summary>
        public void ClearFollowTarget()
        {
            if (_currentFollowTarget == null) return;

            _currentFollowTarget = null;
            _currentFollowIndex = -1;
            _defaultFocusApplied = true;

            // Without an agent, a view that needs one has nothing left to look at, so every mode
            // falls back to the free camera rather than staying attached to a target that is gone.
            if (_currentModeEnum != CameraMode.Free)
                SetCameraMode(CameraMode.Free);

            OnFollowTargetChanged?.Invoke(null);
        }

        /// <summary>
        /// Binds or releases the camera on the current selection without dropping the selection:
        /// turning the follow off hands the camera back to the free view, the agent stays picked in
        /// the bar and in the panel. Every "Follow" button goes through here, so they cannot drift
        /// apart.
        /// </summary>
        /// <returns>True when the camera ends up bound to the selection.</returns>
        public bool ToggleFollow()
        {
            if (_currentFollowTarget == null) return false;

            SetCameraMode(IsFollowingTarget ? CameraMode.Free : CameraMode.Orbit);
            return IsFollowingTarget;
        }

        /// <summary>Selects what the left mouse button does. Fires only on an actual change.</summary>
        public void SetTool(CameraTool tool)
        {
            if (activeTool == tool) return;

            activeTool = tool;
            OnToolChanged?.Invoke(tool);
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
