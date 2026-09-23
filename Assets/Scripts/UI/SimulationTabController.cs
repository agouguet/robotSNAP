using System.Collections.Generic;
using RobotSNAP;
using RobotSNAP.CameraControl;
using RobotSNAP.Core;
using RobotSNAP.Core.Scenario;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Wires the simulation tab: the play controls of the top bar, the minimap camera, and the widgets
/// of the HUD. The widgets take the camera controller as their single source of truth for the
/// selection, and the camera itself is driven by the view toolbar of the overlay - its tools and
/// its three camera views - and by clicks in the scene.
/// </summary>
public class SimulationTabController : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private ScenarioManager scenarioManager;
    [SerializeField] private ScenarioSelectionController scenarioSelectionController;
    [SerializeField] private CameraController cameraController;

    [Header("Minimap")]
    [SerializeField] private Camera minimapCamera;
    [SerializeField] private RenderTexture minimapRenderTexture;

    private Supervisor _supervisor;
    private ScenarioLoader _scenarioLoader;
    private bool _isInitialized;
    private bool _subscribed;

    private VisualElement _root;
    private Button _loadScenarioButton;
    private Button _startStopButton;
    private Button _pauseResumeButton;
    private VisualElement _cameraOverlay;

    private SimulationMinimap _minimap;
    private SimulationAgentPanel _agentPanel;
    private SimulationViewToolbar _viewToolbar;
    private SimulationVisualizationPanel _visualizationPanel;

    private readonly Dictionary<CameraController.CameraMode, Button> _viewModeButtons = new();

    private Transform _minimapTarget;
    private SimulationState _currentState = SimulationState.Idle;

    /// <summary>
    /// Seconds between two refreshes of the widgets of the overlay. The minimap is a map of dots and the
    /// agent panel a list: both are read at a glance, so twenty updates a second look identical to one per
    /// frame and leave the crowd the frame budget instead.
    /// </summary>
    private const float WidgetTickInterval = 0.05f;

    private float _nextWidgetTick;

    private void OnEnable()
    {
        MainViewController.OnViewLoaded += OnViewLoaded;
        TryInitialize();
        Subscribe();
    }

    private void OnDisable()
    {
        MainViewController.OnViewLoaded -= OnViewLoaded;
        Unsubscribe();
    }

    private void OnViewLoaded(string viewName)
    {
        if (viewName != "Simulator") return;

        TryInitialize();
        Subscribe();
    }

    private void TryInitialize()
    {
        if (_isInitialized) return;

        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null) return;

        _root = uiDocument.rootVisualElement;
        if (_root == null) return;

        _loadScenarioButton = _root.Q<Button>("LoadScenarioButton");
        _startStopButton = _root.Q<Button>("StartStopButton");
        _pauseResumeButton = _root.Q<Button>("PauseResumeButton");
        _cameraOverlay = _root.Q<VisualElement>("CameraOverlay");

        if (_startStopButton == null || _pauseResumeButton == null)
        {
            Debug.LogWarning("[SimulationTabController] The simulation view is incomplete; it stays inert.");
            return;
        }

        _supervisor = Supervisor.Instance;

        if (scenarioManager == null)
            scenarioManager = FindAnyObjectByType<ScenarioManager>();
        if (scenarioManager == null)
            Debug.LogError("[SimulationTabController] ScenarioManager missing.");

        if (scenarioSelectionController == null)
            scenarioSelectionController = GetComponent<ScenarioSelectionController>();
        if (scenarioSelectionController == null)
            scenarioSelectionController = FindAnyObjectByType<ScenarioSelectionController>();

        if (cameraController == null)
            cameraController = FindAnyObjectByType<CameraController>();
        if (cameraController == null)
            Debug.LogWarning("[SimulationTabController] CameraController missing.");

        _scenarioLoader = FindAnyObjectByType<ScenarioLoader>();

        BuildHud();
        WirePlayControls();
        WireViewSelector();
        UpdateUI();

        _isInitialized = true;
        MainViewController.OnViewLoaded -= OnViewLoaded;
    }

    /// <summary>
    /// Builds the widgets that own the HUD panels. They are plain classes reading the same visual
    /// tree, which keeps every panel independent of the others.
    /// </summary>
    private void BuildHud()
    {
        if (cameraController != null)
        {
            _minimap = new SimulationMinimap(_root, minimapCamera);
            _agentPanel = new SimulationAgentPanel(_root, cameraController);
            _viewToolbar = new SimulationViewToolbar(_root, cameraController);
        }

        // The debug switches need no camera: they are built whenever the overlay is, so an environment whose
        // view has no controller yet still gets them.
        _visualizationPanel = new SimulationVisualizationPanel(_root, _minimap);

        if (minimapCamera != null && minimapRenderTexture != null)
        {
            minimapCamera.targetTexture = minimapRenderTexture;
            minimapCamera.orthographic = true;
            minimapCamera.orthographicSize = 15f;
            minimapCamera.transform.rotation = Quaternion.Euler(90f, 180f, 0f);
        }
    }

    private void WirePlayControls()
    {
        if (_loadScenarioButton != null)
            _loadScenarioButton.clicked += OnLoadScenarioClicked;

        _startStopButton.clicked += OnStartStopClicked;
        _pauseResumeButton.clicked += OnPauseResumeClicked;
    }

    /// <summary>
    /// Wires the three camera views of the rail: the buttons that hand the camera to another view.
    /// The camera controller stays the only one that knows how a switch is carried out, so the
    /// buttons only ask it for a view and read it back for their highlight.
    /// </summary>
    private void WireViewSelector()
    {
        if (cameraController == null) return;

        _viewModeButtons[CameraController.CameraMode.Orbit] = _root.Q<Button>("ViewIsometricButton");
        _viewModeButtons[CameraController.CameraMode.FirstPerson] = _root.Q<Button>("ViewFirstPersonButton");
        _viewModeButtons[CameraController.CameraMode.ThirdPerson] = _root.Q<Button>("ViewThirdPersonButton");

        foreach (KeyValuePair<CameraController.CameraMode, Button> pair in _viewModeButtons)
        {
            if (pair.Value == null)
            {
                Debug.LogWarning($"[SimulationTabController] The view selector has no button for {pair.Key}.");
                continue;
            }

            CameraController.CameraMode mode = pair.Key;
            pair.Value.clicked += () => cameraController.SetCameraMode(mode);
        }
    }

    /// <summary>
    /// Highlights the view the camera is in. The view changes without a click on these buttons (a run
    /// hands it back to the robot, and a click on an agent focuses it), so the highlight is read from
    /// the camera rather than from the button the user pressed.
    /// </summary>
    private void RefreshViewSelector()
    {
        if (cameraController == null) return;

        foreach (KeyValuePair<CameraController.CameraMode, Button> pair in _viewModeButtons)
        {
            if (pair.Value == null) continue;

            pair.Value.EnableInClassList("is-active", cameraController.CurrentMode == pair.Key);
        }
    }

    private void Subscribe()
    {
        if (!_isInitialized || _subscribed) return;
        _subscribed = true;

        EventBus.Instance.Subscribe<SimulationStateChangedEvent>(OnStateChanged);

        if (cameraController != null)
        {
            cameraController.OnFollowTargetChanged += OnFollowTargetChanged;
            cameraController.OnViewChanged += RefreshViewSelector;
            _minimapTarget = cameraController.GetCurrentFollowTarget();
            _minimap?.SetFocus(_minimapTarget);
            RefreshViewSelector();
        }

        if (scenarioManager != null)
            scenarioManager.OnScenarioApplied += OnScenarioApplied;

        if (scenarioSelectionController != null)
            scenarioSelectionController.OnNewScenarioRequested += OnNewScenarioRequested;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;

        EventBus.Instance.Unsubscribe<SimulationStateChangedEvent>(OnStateChanged);

        if (cameraController != null)
        {
            cameraController.OnFollowTargetChanged -= OnFollowTargetChanged;
            cameraController.OnViewChanged -= RefreshViewSelector;
        }

        if (scenarioManager != null)
            scenarioManager.OnScenarioApplied -= OnScenarioApplied;

        if (scenarioSelectionController != null)
            scenarioSelectionController.OnNewScenarioRequested -= OnNewScenarioRequested;
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextWidgetTick) return;
        _nextWidgetTick = Time.unscaledTime + WidgetTickInterval;

        UpdateMinimap();

        _minimap?.Tick();
        _agentPanel?.Tick();
        _visualizationPanel?.Tick();
    }

    // ==========================================
    //          MINIMAP
    // ==========================================

    /// <summary>
    /// The render-texture minimap follows the agent, so the view stays on the crowd; the occupancy
    /// map, when the scenario has one, shows the whole map instead.
    /// </summary>
    private void UpdateMinimap()
    {
        if (minimapCamera == null) return;

        Vector3 position;
        if (_minimapTarget != null)
        {
            position = _minimapTarget.position;
        }
        else
        {
            Camera view = Camera.main;
            position = view != null ? view.transform.position : Vector3.zero;
        }

        position.y = 30f;
        minimapCamera.transform.position = position;
    }

    private void OnScenarioApplied(ScenarioData scenario)
    {
        // The scenario spawned or cleared agents: refreshing the target list is what tells the agent
        // panel and the minimap that the cast changed. This used to live in the camera bar.
        cameraController?.RefreshFollowableTargets();

        _minimap?.SetFocus(_minimapTarget);

        if (_minimap == null) return;

        if (_scenarioLoader == null)
            _scenarioLoader = FindAnyObjectByType<ScenarioLoader>();

        MapAsset asset = _scenarioLoader != null ? _scenarioLoader.LoadMap(scenario?.Info?.MapImage) : null;
        bool hasGrid = asset != null && asset.Kind == MapAssetKind.Image && asset.Texture != null;

        // Nothing is selected yet, so the orbit opens somewhere useful instead of on whatever the
        // scene camera happened to face. The robot is preferred over the centre of the map: the
        // centre of a corridor can sit inside a wall, and a pivot inside geometry collapses the
        // orbit onto the pivot. Selecting an agent moves the pivot onto it.
        if (cameraController != null)
        {
            RobotSNAP.Agents.Robot robot = FindScenarioRobot();
            Vector3 pivot = robot != null
                ? new Vector3(robot.Position.x, 0f, robot.Position.z)
                : hasGrid
                    ? new Vector3(asset.Bounds.center.x, 0f, asset.Bounds.center.z)
                    : cameraController.OrbitPivot;

            cameraController.OrbitPivot = pivot;

            // A freshly applied scenario replaces the whole cast, so whatever was selected before is
            // gone: the robot takes the view and the selection, and the panel opens on it rather than
            // on "No agent".
            if (robot != null)
                cameraController.FocusAgent(robot.RobotTransform);
        }

        // A prefab or an additive scene has no occupancy image: the minimap then shows the top view
        // of the running camera, which is the only map available.
        _minimap.SetEnvironment(hasGrid ? asset.Texture : null, hasGrid ? asset.Bounds : default);
    }

    private void OnFollowTargetChanged(Transform newTarget)
    {
        _minimapTarget = newTarget;
        _minimap?.SetFocus(newTarget);
    }

    // ==========================================
    //          PLAY CONTROLS
    // ==========================================

    private void OnLoadScenarioClicked()
    {
        if (scenarioSelectionController != null)
            scenarioSelectionController.OpenPopup();
        else
            Debug.LogWarning("[SimulationTabController] ScenarioSelectionController missing.");
    }

    private void OnStartStopClicked()
    {
        switch (_currentState)
        {
            case SimulationState.Idle:
            case SimulationState.Ready:
                EventBus.Instance.Publish(new StartSimulationCommand());
                break;
            case SimulationState.Running:
            case SimulationState.Paused:
                EventBus.Instance.Publish(new StopSimulationCommand());
                break;
        }
    }

    private void OnPauseResumeClicked()
    {
        if (_currentState == SimulationState.Running)
            EventBus.Instance.Publish(new PauseSimulationCommand());
        else if (_currentState == SimulationState.Paused)
            EventBus.Instance.Publish(new ResumeSimulationCommand());
    }

    private void OnStateChanged(SimulationStateChangedEvent evt)
    {
        _currentState = evt.NewState;
        UpdateUI();

        // A run is about the robot: starting one hands it the view and the selection, the way every
        // simulation tool opens on its subject. Only when nothing is selected, so a user who went
        // and picked a pedestrian keeps that choice.
        if (_currentState == SimulationState.Running)
            FocusRobot();
    }

    /// <summary>
    /// The popup asked for a new scenario. The creation workflow lives in the Scenarios tab, so the
    /// shell switches there — loading it first, which is what builds the tab's widgets — and then
    /// opens the first step of a blank scenario.
    /// </summary>
    private void OnNewScenarioRequested()
    {
        SidebarController sidebar = FindAnyObjectByType<SidebarController>();
        sidebar?.ShowView("Scenarios");

        FindAnyObjectByType<ScenariosTabController>()?.OpenNewScenario();
    }

    /// <summary>
    /// Points the camera at the robot and selects it, unless the user has already chosen an agent.
    /// The robot is the subject of the run and the only agent the scenario guarantees.
    /// </summary>
    private void FocusRobot()
    {
        if (cameraController == null || cameraController.IsFollowing) return;

        RobotSNAP.Agents.Robot robot = FindScenarioRobot();
        if (robot != null)
            cameraController.FocusAgent(robot.RobotTransform);
    }

    /// <summary>
    /// The robot of the scenario, which is the one the view and the selection open on.
    ///
    /// That is the primary robot of the roster, and not whichever instance a scene search happens to
    /// return first: with several robots around, an arbitrary pick would open the tool on a robot the
    /// scenario never meant as the subject of a run. A scene with no roster - a hand-placed robot, or a
    /// test - keeps the old lookup.
    /// </summary>
    private static RobotSNAP.Agents.Robot FindScenarioRobot()
    {
        RobotSNAP.Agents.RobotRoster roster = RobotSNAP.Agents.RobotRoster.Current;
        RobotSNAP.Agents.Robot primary = roster != null ? roster.Primary : null;

        return primary != null ? primary : UnityEngine.Object.FindAnyObjectByType<RobotSNAP.Agents.Robot>();
    }

    private void UpdateUI()
    {
        Label startLabel = _startStopButton?.Q<Label>("StartStopLabel");
        VisualElement startIcon = _startStopButton?.Q<VisualElement>("StartStopIcon");

        switch (_currentState)
        {
            case SimulationState.Idle:
            case SimulationState.Ready:
                if (startLabel != null) startLabel.text = "Start";
                if (startIcon != null) SetIcon(startIcon, "play");
                _pauseResumeButton.SetEnabled(false);
                break;

            case SimulationState.Running:
                if (startLabel != null) startLabel.text = "Stop";
                if (startIcon != null) SetIcon(startIcon, "stop");
                _pauseResumeButton.SetEnabled(true);
                SetPauseResumeText("Pause", "pause");
                break;

            case SimulationState.Paused:
                if (startLabel != null) startLabel.text = "Stop";
                if (startIcon != null) SetIcon(startIcon, "stop");
                _pauseResumeButton.SetEnabled(true);
                SetPauseResumeText("Resume", "play");
                break;
        }

        bool isRunningOrPaused = _currentState is SimulationState.Running or SimulationState.Paused;
        _startStopButton.EnableInClassList("running", isRunningOrPaused);

        _cameraOverlay?.EnableInClassList("is-hidden", _currentState != SimulationState.Paused);
    }

    private void SetPauseResumeText(string text, string iconName)
    {
        Label label = _pauseResumeButton?.Q<Label>("PauseResumeLabel");
        VisualElement icon = _pauseResumeButton?.Q<VisualElement>("PauseResumeIcon");

        if (label != null) label.text = text;
        if (icon != null) SetIcon(icon, iconName);
    }

    private void SetIcon(VisualElement iconElement, string iconName)
    {
        Texture2D texture = Resources.Load<Texture2D>($"Icons/{iconName}");
        if (texture != null)
            iconElement.style.backgroundImage = new StyleBackground(texture);
        else
            Debug.LogWarning($"[SimulationTabController] Missing icon: Icons/{iconName}");
    }
}
