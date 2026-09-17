// CameraController.cs
using UnityEngine;
using System.Collections.Generic;
using RobotSNAP;
using RobotSNAP.Agents;
using UnityEngine.UIElements;

namespace RobotSNAP.CameraControl
{
    public class CameraController : MonoBehaviour
    {
        /// <summary>
        /// What the left mouse button does in the camera view. The view toolbar picks it. Move is the
        /// combined tool: on top of its left drag it also takes the right button and the wheel.
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
        private List<Transform> _followableTargets = new List<Transform>();
        // Scratch list for the robots of the roster, so a refresh reuses one buffer instead of building one.
        private readonly List<Robot> _rosterRobots = new List<Robot>();
        private int _currentFollowIndex = -1;

        [Header("Orbit View")]
        [Tooltip("The view opens on an orbit angle rather than a straight top-down one: this is the pitch, in degrees.")]
        public float orbitPitch = 38f;
        [Tooltip("Yaw of the default orbit view, in degrees.")]
        public float orbitYaw = 45f;
        [Tooltip("Distance from the pivot the view opens at, in metres.")]
        public float orbitDistance = 14f;

        [Header("Follow Settings")]
        public Vector3 followOffset = new Vector3(0, 5, -10);
        public Vector3 topDownOffset = new Vector3(0, 20, 0);
        public Vector3 firstPersonOffset = new Vector3(0, 1.5f, 0.5f);
        public Vector3 thirdPersonOffset = new Vector3(0, 2, 5);

        [Header("Interaction")]
        [Tooltip("What the left mouse button does in the view: pick an agent, slide, turn or zoom.")]
        [SerializeField] private CameraTool activeTool = CameraTool.Select;

        [Header("Occlusion")]
        [Tooltip("Walls between the camera and the agent it follows step out of the picture.")]
        [SerializeField] private bool revealThroughWalls = true;
        [Tooltip("What counts as an occluder.")]
        [SerializeField] private LayerMask occlusionMask = ~0;
        [Tooltip("Height on the agent the line of sight aims at, in metres.")]
        [SerializeField] private float occlusionEyeHeight = 1f;
        [Tooltip("How long a wall stays out of the picture after it stops blocking the view, in seconds.")]
        [SerializeField] private float occlusionGraceSeconds = 0.2f;

        public CameraTool ActiveTool => activeTool;
        public event System.Action<CameraTool> OnToolChanged;

        /// <summary>
        /// True while the Move tool is picked. That one tool carries the whole navigation: left drag
        /// slides the view, right drag turns it, the wheel zooms it. The three gestures are no longer
        /// split over the move, rotate and zoom buttons. Every mode that reads the right button or the
        /// wheel on its own checks this, so no gesture is ever applied twice.
        /// </summary>
        public bool UnifiedToolActive => activeTool == CameraTool.Move;

        /// <summary>True while an agent is selected. The dashboard reads this for its badges.</summary>
        public bool IsFollowing => _currentFollowTarget != null;

        /// <summary>
        /// True while the mouse is over the camera view. The view tools only react there — the side
        /// panel, the minimap, the sidebar and every popup keep the mouse for themselves.
        ///
        /// The test goes through the UI Toolkit hit test rather than the EventSystem: the whole HUD
        /// is drawn with UI Toolkit, whose panel registers itself with the EventSystem, so
        /// <c>EventSystem.IsPointerOverGameObject()</c> answers "true" everywhere above the game
        /// view — including the empty space between the widgets, which is exactly where the tools
        /// have to work.
        /// </summary>
        public bool PointerOverView { get; private set; }

        /// <summary>
        /// The point the orbit turns around while no agent is selected. The move tool slides it, so the
        /// user keeps a place to look at after releasing an agent.
        /// </summary>
        public Vector3 OrbitPivot { get; set; }

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
        
        /// <summary>Fires when the view was handed to another mode. The HUD reads CurrentMode from it.</summary>
        public event System.Action OnViewChanged;
        
        public enum CameraMode
        {
            Free,
            TopDown,
            FirstPerson,
            ThirdPerson,
            Orbit
        }

        /// <summary>The view the camera is in right now, for the panels that have to show it.</summary>
        public CameraMode CurrentMode => _currentModeEnum;

        private CameraMode _currentModeEnum = CameraMode.Free;
        private ICameraMode _currentMode;
        // Dictionnaire unique : clé = enum, valeur = instance du mode
        private Dictionary<CameraMode, ICameraMode> _modes;
        
        // Variables d’état partagées
        public Vector3 velocity;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private float _currentRotationY;
        private float _currentRotationX;
        private bool _isRotating = false;
        private Vector3 _lastMousePosition;
        private Transform _currentFollowTarget;

        // View-tool input: what the left button does is chosen by the toolbar, so the gesture is
        // tracked here rather than in the modes, which keep their own right-drag and wheel handling.
        private bool _toolDragging;
        private Vector2 _toolDragStart;
        private Vector2 _toolDragLast;
        private bool _toolDragMoved;
        private const float DragThresholdPixels = 4f;

        // Right button of the combined tool, tracked apart from the left drag so both gestures can be
        // held at the same time.
        private bool _viewRotating;
        private Vector2 _rotateDragLast;

        // How far one notch of the wheel pulls the view, in the units of zoomSpeed. A notch is a click
        // rather than a movement, so it has to be worth the same distance on every machine: scaling it
        // by the frame time, the way a drag is scaled, makes it wear off as the frame rate climbs.
        private const float WheelNotchZoom = 0.3f;

        // Ceiling on what one frame of wheel may ask for, in metres: a platform that reports a whole
        // page in a single frame, as some do, must not throw the view across the map in one go.
        private const float WheelMaxStep = 3f;

        private CameraOcclusionSolver _occlusion;

        /// <summary>
        /// Zoom asked for this frame by a gesture, in metres: the wheel and the drag of the zoom tool
        /// both write here, and the modes that zoom by distance consume it during the same frame.
        /// </summary>
        public float PendingZoom { get; set; }
        
        #region Properties for modes
        public Vector3 TargetPosition { get => _targetPosition; set => _targetPosition = value; }
        public Quaternion TargetRotation { get => _targetRotation; set => _targetRotation = value; }
        public float CurrentRotationY { get => _currentRotationY; set => _currentRotationY = value; }
        public float CurrentRotationX { get => _currentRotationX; set => _currentRotationX = value; }
        public bool IsRotating { get => _isRotating; set => _isRotating = value; }
        public Vector3 LastMousePosition { get => _lastMousePosition; set => _lastMousePosition = value; }
        public Transform CurrentFollowTarget { get => _currentFollowTarget; set => _currentFollowTarget = value; }
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            Initialize();
            _occlusion = new CameraOcclusionSolver(mainCamera, occlusionMask, occlusionGraceSeconds, occlusionEyeHeight);
        }
        
        private void LateUpdate()
        {
            if (mainCamera == null) return;
            _currentMode?.Update(this, Time.unscaledDeltaTime);

            // A view with no distance to change (a first person shot sits on the agent) leaves the
            // request unread. Dropping it here keeps it from firing later, when the user switches to a
            // view that would answer it with a jump.
            PendingZoom = 0f;

            // After the camera has moved for the frame, so the line of sight is the one the user sees.
            _occlusion?.Tick(revealThroughWalls ? _currentFollowTarget : null);
        }

        // Nothing may be left out of the picture once the view stops working: the walls of the map
        // have to come back even if the component is switched off mid-run.
        private void OnDisable() => _occlusion?.RestoreAll();
        private void OnDestroy() => _occlusion?.RestoreAll();

        private void Update()
        {
            // No keyboard shortcut here on purpose: the HUD has text fields, and every key this
            // component grabbed was a key the user meant to type. Tab in particular also drives the
            // focus navigation of UI Toolkit, which is how the agent filter used to steal typing.
            PointerOverView = ComputePointerOverView();
            HandleToolInput();
        }

        #region View tools

        /// <summary>
        /// Applies the gestures of the view: the left drag of the picked tool, the wheel, and the right
        /// drag of the combined tool. A gesture that starts over the HUD is ignored, so a panel never
        /// moves the camera.
        ///
        /// The routing lives here rather than in the modes because this component is the only one that
        /// knows which tool is picked, while a mode only knows the pivot and the distance it animates.
        /// One reader is also what keeps a gesture from being counted twice while the Move tool carries
        /// all of them: the gestures are turned into the same channels the dedicated buttons use (a
        /// turn on CurrentRotationY, a zoom on PendingZoom), and the modes leave the right button and
        /// the wheel to the controller while that tool is picked (see UnifiedToolActive).
        /// </summary>
        private void HandleToolInput()
        {
            if (mainCamera == null) return;

            HandleRotateDrag();
            HandleWheelZoom();

            if (Input.GetKeyDown(KeyCode.Mouse0))
            {
                _toolDragging = PointerOverView;
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
        /// The right drag of the combined Move tool: the turn of the rotate button, on the button the
        /// other tools leave free. The select tool needs a free click to pick an agent, and the rotate
        /// and zoom tools already turn and pull the view with the left button.
        /// </summary>
        private void HandleRotateDrag()
        {
            if (!UnifiedToolActive)
            {
                // Picking another tool in the middle of a turn must not leave the view stuck to the
                // mouse: the drag ends where the tool does.
                _viewRotating = false;
                return;
            }

            if (Input.GetKeyDown(rotateKey))
            {
                _viewRotating = PointerOverView;
                _rotateDragLast = Input.mousePosition;
            }

            if (_viewRotating)
            {
                Vector2 mouse = Input.mousePosition;
                Vector2 delta = mouse - _rotateDragLast;
                _rotateDragLast = mouse;
                RotateBy(delta);
            }

            if (Input.GetKeyUp(rotateKey))
                _viewRotating = false;
        }

        /// <summary>
        /// The wheel, wherever the pointer is inside the camera view: it zooms the view whichever tool
        /// is picked, the way the modes used to read it on their own. The views that zoom by a distance
        /// take it through PendingZoom rather than reading the axis themselves, so a notch is worth the
        /// same in each of them and cannot be counted twice. The ones that zoom by something else, an
        /// orthographic size or a forward move, keep their own read.
        /// </summary>
        private void HandleWheelZoom()
        {
            // Hovering the HUD, a panel or a popup must not push the view around while the user is
            // doing something there.
            float scroll = PointerOverView ? Input.GetAxis("Mouse ScrollWheel") : 0f;
            if (scroll == 0f) return;

            PendingZoom -= Mathf.Clamp(scroll * zoomSpeed * WheelNotchZoom, -WheelMaxStep, WheelMaxStep);
        }

        /// <summary>
        /// Slides the view across the map, one visible span of ground per screen: the gesture means
        /// the same thing at every zoom level, and the same thing on every frame rate. Only the free
        /// camera owns its position — while an agent is followed the camera belongs to it, and the
        /// user releases it from the agent bar instead.
        ///
        /// The pivot is not the only thing that moves. Nudging the pivot alone leaves the camera
        /// looking at a spot it has not reached yet, and the orbit's smoothing then turns the shot
        /// while it catches up — the swing the translation tool used to show. The camera is carried
        /// by the same offset instead, so its angle relative to the pivot never changes during a pan.
        /// </summary>
        private void PanBy(Vector2 screenDelta)
        {
            if (mainCamera == null || screenDelta == Vector2.zero) return;

            // Sliding the view by hand is the one gesture that releases the agent: the camera stops
            // being about that agent and becomes a place the user chose to look at.
            if (_currentFollowTarget != null)
                ClearFollowTarget();

            Vector3 shift = GroundShiftFromDrag(screenDelta);

            OrbitPivot += shift;
            mainCamera.transform.position += shift;
        }

        /// <summary>
        /// The ground a drag of that many pixels is worth, in the direction the view faces.
        ///
        /// The scale comes from the ground the camera can see rather than from a fixed constant, so
        /// a pan covers the same share of the screen whether the view is close or far. It is a
        /// distance per pixel and not per second: multiplying a mouse displacement by a frame time
        /// would make the same drag travel twice as far on a machine twice as fast.
        /// </summary>
        private Vector3 GroundShiftFromDrag(Vector2 screenDelta)
        {
            float distance = Vector3.Distance(mainCamera.transform.position, OrbitPivot);
            float pitch = Mathf.Max(5f, Mathf.Abs(_currentRotationX)) * Mathf.Deg2Rad;

            float visibleGround =
                2f * distance * Mathf.Tan(mainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Sin(pitch);
            float perPixel = visibleGround / Mathf.Max(1f, Screen.height);

            Vector3 forward = Vector3.ProjectOnPlane(mainCamera.transform.forward, Vector3.up);
            Vector3 right = Vector3.ProjectOnPlane(mainCamera.transform.right, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;

            // The ground travels with the cursor, so the view travels against it.
            return (-right.normalized * screenDelta.x - forward.normalized * screenDelta.y) * perPixel;
        }

        /// <summary>
        /// Turns the view around the pivot — the selected agent, or wherever the move tool left it.
        ///
        /// Only the turn is offered. Tilting the shot up and down is what made the gesture feel
        /// wrong: the view slid between a corridor eye-level and a map, when the pitch the orbit was
        /// authored with is the one that frames the scene.
        /// </summary>
        private void RotateBy(Vector2 screenDelta)
        {
            _currentRotationY += screenDelta.x * rotateSpeed * Time.unscaledDeltaTime * 0.6f;
        }

        /// <summary>Pulls the camera closer or pushes it away, without touching the wheel path.</summary>
        private void ZoomByDrag(float amount)
        {
            float distance = amount * zoomSpeed * Time.unscaledDeltaTime * 0.35f;
            if (distance == 0f) return;

            PendingZoom -= distance;
        }

        /// <summary>Focuses the agent under the cursor — what the select tool does on a click.</summary>
        private void PickAgentUnderCursor()
        {
            if (mainCamera == null) return;

            if (!TryGetPointerViewportPoint(Input.mousePosition, out Vector2 pointer))
                return;

            Ray ray = mainCamera.ViewportPointToRay(pointer);
            if (Physics.Raycast(ray, out RaycastHit hit, 500f, ~0, QueryTriggerInteraction.Ignore))
            {
                Robot robot = hit.collider.GetComponentInParent<Robot>();
                if (robot != null)
                {
                    SetFollowTarget(robot.RobotTransform);
                    return;
                }

                HumanAgent human = hit.collider.GetComponentInParent<HumanAgent>();
                if (human != null)
                {
                    SetFollowTarget(human.transform);
                    return;
                }
            }

            // The pedestrian avatars carry no collider, so the ray can never reach them. The agent
            // whose body is nearest to the click on screen is the one the user was aiming at.
            Transform nearest = NearestTargetToPointer(pointer, ScreenPickRadius);
            if (nearest != null)
                SetFollowTarget(nearest);
        }

        /// <summary>
        /// The followable agent closest to the pointer, or null when the click landed away from every
        /// agent. Only the agents whose anchor is in front of the camera count.
        ///
        /// <paramref name="viewportPointer"/> and the candidates share the camera's viewport space,
        /// and the radius is turned into that same space, so the distance means what it says: a
        /// screen-space radius measures nothing when the two ends are not in the same space.
        /// </summary>
        private Transform NearestTargetToPointer(Vector2 viewportPointer, float radius)
        {
            if (_followableTargets.Count == 0)
                RefreshFollowableTargets();

            Rect view = ViewElement()?.worldBound ?? default;
            float scale = ViewElement()?.panel?.scaledPixelsPerPoint ?? 1f;
            Vector2 viewSize = new Vector2(view.width * scale, view.height * scale);
            if (viewSize.x <= 0f || viewSize.y <= 0f) return null;

            Transform best = null;
            float bestDistance = radius;

            foreach (Transform candidate in _followableTargets)
            {
                if (candidate == null) continue;

                Vector3 viewport = mainCamera.WorldToViewportPoint(ScreenAnchor(candidate));
                if (viewport.z <= 0f) continue;

                float distance = Vector2.Distance(
                    new Vector2(viewport.x * viewSize.x, viewport.y * viewSize.y),
                    new Vector2(viewportPointer.x * viewSize.x, viewportPointer.y * viewSize.y));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// The pointer in the simulation camera's own viewport space — the space
        /// <c>ViewportPointToRay</c> and <c>WorldToViewportPoint</c> work in.
        ///
        /// The simulation camera draws into a render texture that the view container displays, so a
        /// game view pixel is not a camera pixel: the pointer has to be carried through the
        /// container rectangle before it can be compared with anything the camera knows about.
        /// </summary>
        private bool TryGetPointerViewportPoint(Vector2 screenPoint, out Vector2 viewport)
        {
            viewport = default;

            VisualElement view = ViewElement();
            IPanel panel = view?.panel;
            if (view == null || panel == null) return false;

            Rect bound = view.worldBound;
            if (bound.width <= 0f || bound.height <= 0f) return false;

            float scale = panel.scaledPixelsPerPoint;

            // The panel measures downwards from the top of the view, the pointer upwards from its
            // bottom, so the y axis is flipped once here and the result is already a viewport
            // coordinate — the camera's own space, whichever texture it draws into.
            Vector2 fromViewTop = new Vector2(
                screenPoint.x - bound.x * scale,
                (Screen.height - screenPoint.y) - bound.y * scale);

            viewport = new Vector2(
                fromViewTop.x / (bound.width * scale),
                1f - fromViewTop.y / (bound.height * scale));

            return true;
        }

        /// <summary>A click aims at the middle of the body, not at the feet the transform sits on.</summary>
        private static Vector3 ScreenAnchor(Transform target)
        {
            bool isHuman = target.GetComponentInParent<HumanAgent>() != null;
            return target.position + Vector3.up * (isHuman ? 1f : 0.4f);
        }

        /// <summary>How far from an agent a click still counts as aiming at it, in screen pixels.</summary>
        private const float ScreenPickRadius = 60f;

        // ==========================================
        //          HUD HIT TEST
        // ==========================================

        private UIDocument _hudDocument;
        private VisualElement _viewElement;

        private bool ComputePointerOverView()
        {
            VisualElement picked = PickHudElement(Input.mousePosition);
            if (picked == null) return false;

            VisualElement view = ViewElement();
            if (view == null) return false;

            for (VisualElement element = picked; element != null; element = element.parent)
            {
                if (element == view) return true;
            }

            return false;
        }

        private VisualElement PickHudElement(Vector2 screenPoint)
        {
            IPanel panel = HudDocument()?.rootVisualElement?.panel;
            if (panel == null) return null;

            return panel.Pick(PointerInPanelSpace(panel, screenPoint));
        }

        /// <summary>
        /// The pointer in panel coordinates — the space <c>Pick</c> takes.
        ///
        /// A panel counts downwards from the top of the view while the pointer counts upwards from
        /// its bottom, and the raw pointer is not flipped for us: feeding it straight to the hit
        /// test asks about the mirrored point, which is how the view tools ended up answering a
        /// gesture the user had not made. The flip is done once, here.
        /// </summary>
        private static Vector2 PointerInPanelSpace(IPanel panel, Vector2 screenPoint)
        {
            Vector2 fromTop = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            return RuntimePanelUtils.ScreenToPanel(panel, fromTop);
        }

        /// <summary>The document that holds the simulation view; looked up once, and again if it is rebuilt.</summary>
        private UIDocument HudDocument()
        {
            if (_hudDocument != null && _hudDocument.rootVisualElement != null)
                return _hudDocument;

            foreach (UIDocument document in FindObjectsByType<UIDocument>())
            {
                VisualElement root = document.rootVisualElement;
                if (root != null && root.Q<VisualElement>("CameraContainer") != null)
                {
                    _hudDocument = document;
                    break;
                }
            }

            return _hudDocument;
        }

        /// <summary>The rectangle the view tools own: everything under it is the 3D scene.</summary>
        private VisualElement ViewElement()
        {
            if (_viewElement != null && _viewElement.panel != null)
                return _viewElement;

            _viewElement = HudDocument()?.rootVisualElement?.Q<VisualElement>("CameraContainer");
            return _viewElement;
        }

        #endregion

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

                // The view opens on the isometric orbit rather than on whatever pose the scene camera
                // happens to have: the tools are the only way to move it from here.
                _currentRotationY = orbitYaw;
                _currentRotationX = orbitPitch;
                OrbitPivot = GroundPointInFront(mainCamera.transform, orbitDistance);
            }

            _modes = new Dictionary<CameraMode, ICameraMode>
            {
                { CameraMode.Free, new FreeCameraMode() },
                { CameraMode.TopDown, new TopDownMode() },
                { CameraMode.FirstPerson, new FirstPersonMode() },
                { CameraMode.ThirdPerson, new ThirdPersonMode() },
                { CameraMode.Orbit, new OrbitMode() }
            };

            velocity = Vector3.zero;

            SetCameraMode(CameraMode.Orbit);
        }

        /// <summary>Point on the ground the camera looks at by default, so the orbit opens on the map.</summary>
        private static Vector3 GroundPointInFront(Transform camera, float distance)
        {
            Vector3 forward = Vector3.ProjectOnPlane(camera.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;

            Vector3 point = camera.position + forward.normalized * distance;
            point.y = 0f;
            return point;
        }
        
        #endregion
        
        #region Public Methods

        /// <summary>
        /// Hands the view over to a mode. The view selector of the HUD, the start of a run and the focus
        /// of an agent all end up here, so this stays the one place that knows how a switch is carried
        /// out; OnViewChanged is what tells the HUD which button to light up.
        /// </summary>
        public void SetCameraMode(CameraMode mode)
        {
            // Asking for the view the camera is already in must change nothing: entering the orbit a
            // second time would re-frame its distance under the user.
            if (mode == _currentModeEnum && _currentMode != null)
                return;

            if (!_modes.TryGetValue(mode, out ICameraMode next))
            {
                Debug.LogWarning($"[CameraController] The view has no {mode} mode.");
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
            _currentMode = next;
            _currentMode.Enter(this);
            OnViewChanged?.Invoke();
            Debug.Log($"[CameraController] Mode changed to: {mode}");
        }

        /// <summary>
        /// Rebuilds the list of agents the view can be handed to.
        ///
        /// The robots of the scenario come first, the primary leading and the others in the order the roster
        /// lists them, because the list is read by a person: with several robots around, an order that follows
        /// whatever the scene search returns first would move the subjects around between two refreshes. The
        /// pedestrians, and every robot no roster owns - a hand-placed one, or a test - keep the tag lookup
        /// that used to build the whole list, and each agent lands in the list exactly once.
        /// </summary>
        public void RefreshFollowableTargets()
        {
            _followableTargets.Clear();

            RobotRoster roster = RobotRoster.Current;
            if (roster != null)
            {
                AddFollowableRobot(roster.Primary);

                _rosterRobots.Clear();
                roster.FillRobots(_rosterRobots);
                foreach (Robot robot in _rosterRobots)
                    AddFollowableRobot(robot);
            }

            // Everything the tag lookup adds, and only that: the robots of the roster are already in, so the
            // range sorted below is the part of the list whose order the scene search does not decide.
            int firstTagged = _followableTargets.Count;

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

                    AddFollowable(targetTransform);
                }
            }

            _followableTargets.Sort(firstTagged, _followableTargets.Count - firstTagged,
                Comparer<Transform>.Create(
                    (a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase)));

            OnTargetsUpdated?.Invoke(_followableTargets);
        }

        /// <summary>
        /// Adds a robot of the roster. It is followed through its base_link, the moving link the camera and the
        /// scene picking both work with; the root of the prefab stays where the roster put it.
        /// </summary>
        private void AddFollowableRobot(Robot robot)
        {
            if (robot == null) return;

            AddFollowable(robot.RobotTransform);
        }

        /// <summary>Adds an agent once: two entries on the same transform would read as a duplicate.</summary>
        private void AddFollowable(Transform target)
        {
            if (target == null) return;

            if (!_followableTargets.Contains(target))
                _followableTargets.Add(target);
        }

        /// <summary>Hands the view to that agent. The orbit angle and distance stay where they are.</summary>
        public void SetFollowTarget(Transform target)
        {
            if (target == null) return;
            _currentFollowTarget = target;
            _currentFollowIndex = _followableTargets.IndexOf(target);
            OnFollowTargetChanged?.Invoke(target);
            Debug.Log($"[CameraController] Following target: {target.name}");
        }

        /// <summary>
        /// Selects an agent and makes sure the orbit is the active view — what a click on an agent in
        /// the scene view and a click on a row in the agent list both do.
        /// </summary>
        public void FocusAgent(Transform target)
        {
            if (target == null) return;

            SetFollowTarget(target);
            if (_currentModeEnum != CameraMode.Orbit)
                SetCameraMode(CameraMode.Orbit);
        }

        /// <summary>
        /// Releases the agent. The orbit pivot settles on the spot the agent last stood on, so the
        /// view stays where the user was looking instead of jumping away.
        /// </summary>
        public void ClearFollowTarget()
        {
            if (_currentFollowTarget == null) return;

            Vector3 position = _currentFollowTarget.position;
            OrbitPivot = new Vector3(position.x, 0f, position.z);
            _currentFollowTarget = null;
            _currentFollowIndex = -1;

            OnFollowTargetChanged?.Invoke(null);
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
