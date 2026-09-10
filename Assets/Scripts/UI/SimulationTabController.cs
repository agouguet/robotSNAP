using UnityEngine;
using UnityEngine.UIElements;
using RobotSNAP;
using RobotSNAP.Core;
using RobotSNAP.CameraControl;
using RobotSNAP.Core.Scenario;

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
    private bool _isInitialized = false;

    private VisualElement _root;
    private Button _viewButton;
    private VisualElement _viewMenu;
    private Button _fullscreenButton;
    private Button _loadScenarioButton;
    private Button _startStopButton;
    private Button _pauseResumeButton;
    private VisualElement _cameraOverlay;

    private MinimapRenderer _minimapRenderer;
    private Transform _minimapTarget;

    private SimulationState _currentState = SimulationState.Idle;

    private void OnEnable()
    {
        MainViewController.OnViewLoaded += OnViewLoaded;
        if (uiDocument != null && uiDocument.rootVisualElement.Q<Button>("ViewButton") != null)
            TryInitialize();
    }

    private void OnDisable()
    {
        MainViewController.OnViewLoaded -= OnViewLoaded;
        EventBus.Instance.Unsubscribe<SimulationStateChangedEvent>(OnStateChanged);

        if (_loadScenarioButton != null)
            _loadScenarioButton.clicked -= OnLoadScenarioClicked;
        if (cameraController != null)
            cameraController.OnFollowTargetChanged -= OnFollowTargetChanged;
    }

    private void OnViewLoaded(string viewName)
    {
        if (viewName == "Simulator")
            TryInitialize();
    }

    private void TryInitialize()
    {
        if (_isInitialized) return;

        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null) return;

        _root = uiDocument.rootVisualElement;
        _viewButton = _root.Q<Button>("ViewButton");
        _viewMenu = _root.Q<VisualElement>("ViewMenu");
        _fullscreenButton = _root.Q<Button>("FullScreenButton");
        _loadScenarioButton = _root.Q<Button>("LoadScenarioButton");
        _startStopButton = _root.Q<Button>("StartStopButton");
        _pauseResumeButton = _root.Q<Button>("PauseResumeButton");
        _cameraOverlay = _root.Q<VisualElement>("CameraOverlay");

        if (_viewButton == null || _viewMenu == null ||
            _startStopButton == null || _pauseResumeButton == null)
        {
            Debug.LogWarning("SimulationTabController: certains éléments UI sont manquants.");
            return;
        }

        _supervisor = Supervisor.Instance;
        if (_supervisor == null)
        {
            Debug.LogError("Supervisor introuvable.");
            return;
        }

        if (scenarioManager == null)
            scenarioManager = FindObjectOfType<ScenarioManager>();
        if (scenarioManager == null)
        {
            Debug.LogError("ScenarioManager introuvable.");
            return;
        }

        if (scenarioSelectionController == null)
            scenarioSelectionController = GetComponent<ScenarioSelectionController>();
        if (scenarioSelectionController == null)
            scenarioSelectionController = FindObjectOfType<ScenarioSelectionController>();
        
        if (cameraController == null)
            cameraController = FindObjectOfType<CameraController>();
        if (cameraController == null)
            Debug.LogWarning("CameraController non trouvé dans la scène.");

        _isInitialized = true;
        Initialize();
        MainViewController.OnViewLoaded -= OnViewLoaded;
    }

    private void Initialize()
    {
        // === Menu de sélection de vue ===
        _viewButton.clicked += () =>
        {
            _viewMenu.style.display = (_viewMenu.style.display == DisplayStyle.Flex) ? DisplayStyle.None : DisplayStyle.Flex;
        };

        foreach (var option in _viewMenu.Children())
        {
            if (option is Button btn)
            {
                btn.clicked += () =>
                {
                    _viewButton.text = btn.text;
                    _viewMenu.style.display = DisplayStyle.None;
                    OnViewChanged(btn.text);
                };
            }
        }

        _root.RegisterCallback<ClickEvent>(evt =>
        {
            if (_viewMenu.style.display != DisplayStyle.Flex) return;
            VisualElement target = evt.target as VisualElement;
            if (target != _viewButton && !_viewMenu.Contains(target))
                _viewMenu.style.display = DisplayStyle.None;
        });

        // === Plein écran ===
        if (_fullscreenButton != null)
            _fullscreenButton.clicked += ToggleFullscreen;

        // === Load Scenario ===
        _loadScenarioButton.clicked += OnLoadScenarioClicked;

        // === Start / Stop ===
        _startStopButton.clicked += OnStartStopClicked;

        // === Pause / Resume ===
        _pauseResumeButton.clicked += OnPauseResumeClicked;

        SetupMinimap();

        // === Abonnement aux événements de changement de cible ===
        if (cameraController != null)
        {
            cameraController.OnFollowTargetChanged += OnFollowTargetChanged;
            // Initialiser la cible avec la valeur actuelle
            if (cameraController.CurrentFollowTarget != null)
                _minimapTarget = cameraController.CurrentFollowTarget;
        }

        // === Abonnement aux événements d'état ===
        EventBus.Instance.Subscribe<SimulationStateChangedEvent>(OnStateChanged);

        // === État initial ===
        UpdateUI();
    }

    private void SetupMinimap()
    {
        var minimapContainer = _root.Q<VisualElement>("MinimapContainer");
        if (minimapContainer == null)
        {
            Debug.LogWarning("MinimapContainer non trouvé.");
            return;
        }

        // Cacher l'ancien élément (MinimapImage)
        var minimapImage = minimapContainer.Q<VisualElement>("MinimapImage");
        if (minimapImage != null)
            minimapImage.style.display = DisplayStyle.None;

        // Créer le renderer ImmediateModeElement
        _minimapRenderer = new MinimapRenderer
        {
            minimapRT = minimapRenderTexture,
            style =
            {
                position = Position.Absolute,
                top = 8,
                left = 8,
                right = 8,
                bottom = 8
            }
        };
        // _minimapRenderer.AddToClassList("minimap-image");
        // minimapContainer.Add(_minimapRenderer);

        // Configurer la caméra de minicarte
        if (minimapCamera != null)
        {
            minimapCamera.targetTexture = minimapRenderTexture;
            minimapCamera.orthographic = true;
            minimapCamera.orthographicSize = 15f; // Ajustez
            minimapCamera.transform.rotation = Quaternion.Euler(90, 180, 0);
        }
    }

    private void Update()
    {
        UpdateMinimap();
    }

    private void UpdateMinimap()
    {
        if (minimapCamera == null || _minimapRenderer == null) return;

        Vector3 targetPos;
        if (_minimapTarget != null)
        {
            // Suivre la cible
            targetPos = _minimapTarget.position;
            targetPos.y = 30f; // Hauteur fixe de la caméra de minicarte
        }
        else
        {
            // Fallback : suivre la caméra principale (projetée au sol)
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                targetPos = mainCam.transform.position;
                targetPos.y = 30f;
            }
            else
            {
                // Position par défaut (centre de la scène)
                targetPos = new Vector3(0, 30, 0);
            }
        }

        minimapCamera.transform.position = targetPos;
    }

    private void OnLoadScenarioClicked()
    {
        if (scenarioSelectionController != null)
            scenarioSelectionController.OpenPopup();
        else
            Debug.LogWarning("[SimulationTabController] ScenarioSelectionController non trouvé.");
    }

    private void OnFollowTargetChanged(Transform newTarget)
    {
        _minimapTarget = newTarget;
        Debug.Log($"[Minimap] Suivi de : {newTarget?.name ?? "aucune cible"}");
    }

    private void OnStateChanged(SimulationStateChangedEvent evt)
    {
        _currentState = evt.NewState;
        UpdateUI();
    }

    private void OnViewChanged(string viewName)
    {
        Camera cam = GetComponent<Camera>();
        if (cam != null)
        {
            switch (viewName)
            {
                case "3D": cam.orthographic = false; break;
                case "2D": cam.orthographic = true; break;
            }
        }
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

    private void ToggleFullscreen()
    {
        Screen.fullScreen = !Screen.fullScreen;
        Debug.Log($"[SimulationTabController] Fullscreen: {Screen.fullScreen}");
    }

    private void UpdateUI()
    {
        var startIcon = _startStopButton?.Q<VisualElement>("StartStopIcon");
        var startLabel = _startStopButton?.Q<Label>("StartStopLabel");

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

        bool isRunningOrPaused = (_currentState == SimulationState.Running || _currentState == SimulationState.Paused);
        _startStopButton?.EnableInClassList("running", isRunningOrPaused);

        if (_cameraOverlay != null)
            _cameraOverlay.style.display = (_currentState == SimulationState.Paused) ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void SetPauseResumeText(string text, string iconName)
    {
        var label = _pauseResumeButton?.Q<Label>("PauseResumeLabel");
        var icon = _pauseResumeButton?.Q<VisualElement>("PauseResumeIcon");
        if (label != null) label.text = text;
        if (icon != null) SetIcon(icon, iconName);
    }

    private void SetIcon(VisualElement iconElement, string iconName)
    {
        Texture2D tex = Resources.Load<Texture2D>($"Icons/{iconName}");
        if (tex != null)
            iconElement.style.backgroundImage = new StyleBackground(tex);
        else
            Debug.LogWarning($"[SimulationTabController] Icône non trouvée: Icons/{iconName}");
    }
}
