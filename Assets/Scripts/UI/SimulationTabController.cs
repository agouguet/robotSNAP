using UnityEngine.UIElements;
using UnityEngine;
using System.Collections;
using RobotSNAP;
using RobotSNAP.Core;
using RobotSNAP.Core.Scenario;

public class SimulationTabController : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private ScenarioManager scenarioManager; // à assigner dans l'inspecteur

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

    private enum SimState { Idle, Running, Paused }
    private SimState _currentState = SimState.Idle;

    private void OnEnable()
    {
        MainViewController.OnViewLoaded += OnViewLoaded;
        if (uiDocument != null && uiDocument.rootVisualElement.Q<Button>("ViewButton") != null)
            TryInitialize();
    }

    private void OnDisable()
    {
        MainViewController.OnViewLoaded -= OnViewLoaded;
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

        if (_viewButton == null || _viewMenu == null || _fullscreenButton == null ||
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
        _fullscreenButton.clicked += ToggleFullscreen;

        // === Load Scenario ===
        _loadScenarioButton.clicked += OnLoadScenarioClicked;

        // === Start / Stop ===
        _startStopButton.clicked += OnStartStopClicked;

        // === Pause / Resume ===
        _pauseResumeButton.clicked += OnPauseResumeClicked;

        // === État initial ===
        UpdateUI();

        // === Surveillance des changements d'état ===
        StartCoroutine(CheckStatePeriodically());

        // Souscrire aux événements de ScenarioManager et Supervisor si nécessaire
        // (par exemple, OnScenarioLoaded, OnScenarioApplied, etc.)
    }

    private void OnViewChanged(string viewName)
    {
        // Implémentez le changement de vue (caméra orthographique, etc.)
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

    private void OnLoadScenarioClicked()
    {
        // Ouvrir un dialogue pour choisir un scénario, puis charger
        // Exemple : scenarioManager.LoadScenario("mon_scenario");
    }

    private void OnStartStopClicked()
    {
        switch (_currentState)
        {
            case SimState.Idle:
                // Démarrer la simulation
                scenarioManager.StartSimulation();
                break;
            case SimState.Running:
            case SimState.Paused:
                // Arrêter (stop)
                scenarioManager.StopSimulation();
                break;
        }
        // L'UI sera mise à jour par la coroutine
    }

    private void OnPauseResumeClicked()
    {
        if (_currentState == SimState.Running)
        {
            _supervisor.Pause();
        }
        else if (_currentState == SimState.Paused)
        {
            _supervisor.Resume();
        }
    }

    private void ToggleFullscreen()
    {
        // Implémentez le plein écran (Screen.fullScreen = !Screen.fullScreen)
    }

    private IEnumerator CheckStatePeriodically()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.1f);
            UpdateUI();
        }
    }

    private void UpdateUI()
    {
        // Déterminer l'état
        SimState newState;
        bool hasScenario = scenarioManager.HasScenarioLoaded;
        bool isPaused = _supervisor.IsPaused;

        if (!hasScenario)
            newState = SimState.Idle;
        else if (!isPaused)
            newState = SimState.Running;
        else
            newState = SimState.Paused;

        if (newState != _currentState)
        {
            _currentState = newState;
            // Optionnel : déclencher un événement de changement d'état
        }

        // Mettre à jour les boutons
        var startIcon = _startStopButton?.Q<VisualElement>("StartStopIcon");
        var startLabel = _startStopButton?.Q<Label>("StartStopLabel");

        switch (newState)
        {
            case SimState.Idle:
                if (startLabel != null) startLabel.text = "Start";
                if (startIcon != null) SetIcon(startIcon, "play");
                _pauseResumeButton.SetEnabled(false);
                break;

            case SimState.Running:
                if (startLabel != null) startLabel.text = "Stop";
                if (startIcon != null) SetIcon(startIcon, "stop");
                _pauseResumeButton.SetEnabled(true);
                SetPauseResumeText("Pause", "pause");
                break;

            case SimState.Paused:
                if (startLabel != null) startLabel.text = "Stop";
                if (startIcon != null) SetIcon(startIcon, "stop");
                _pauseResumeButton.SetEnabled(true);
                SetPauseResumeText("Resume", "play");
                break;
        }

        // Overlay de pause
        if (_cameraOverlay != null)
            _cameraOverlay.style.display = (newState == SimState.Paused) ? DisplayStyle.Flex : DisplayStyle.None;
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
        // Chargez votre icône depuis Resources ou autre
        Texture2D tex = Resources.Load<Texture2D>($"Icons/{iconName}");
        if (tex != null)
            iconElement.style.backgroundImage = new StyleBackground(tex);
    }
}