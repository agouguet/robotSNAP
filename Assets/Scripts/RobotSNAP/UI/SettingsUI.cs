using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using RobotSNAP.Core;
using RobotSNAP.Core.Scenario;

namespace RobotSNAP.UI
{
    public class UnifiedSettingsUI : MonoBehaviour
    {
        [Header("Main Panel")]
        public GameObject settingsPanel;
        public Button toggleButton;

        [Header("Tabs")]
        public Button agentsTabButton;
        public Button viewTabButton;
        public Button debugTabButton;
        public Button rosTabButton;
        public GameObject agentsPanel;
        public GameObject viewPanel;
        public GameObject debugPanel;
        public GameObject rosPanel;

        // ==================== AGENTS PANEL (ex SimulationSettingsUI) ====================
        [Header("Environment Settings")]
        public TMP_InputField environmentCountInput;
        public TMP_InputField environmentSpacingInput;
        public DatasetDropdown datasetDropdown;

        [Header("Spawn Settings")]
        public TMP_InputField minHumansInput;
        public TMP_InputField maxHumansInput;
        public TMP_InputField minAgentDistanceInput;

        [Header("Robot Settings")]
        public TMP_InputField robotSpeedInput;
        public TMP_InputField robotSensorRangeInput;
        public TMP_Dropdown robotBehaviorDropdown;

        [Header("Human Settings")]
        public TMP_InputField humanDefaultSpeedInput;
        public TMP_Dropdown humanControllerTypeDropdown;
        public Slider humanInteractionRadiusSlider;
        public TextMeshProUGUI humanInteractionRadiusValue;

        [Header("Simulation Settings")]
        public Slider timeScaleSlider;
        public TextMeshProUGUI timeScaleValue;
        public Toggle pauseToggle;

        [Header("Scenario Settings")]
        public TMP_Dropdown scenarioDropdown;
        public Button applyScenarioButton;

        [Header("Agents Panel Buttons")]
        public Button applyButton;      // Applique les paramètres
        public Button resetButton;      // Reset simulation
        public Button saveButton;       // Sauvegarde config
        public Button showWorkingConfigButton;

        // ==================== VIEW PANEL (ex VisualizationSettingsUI) ====================
        [Header("Camera Presets")]
        public TMP_Dropdown cameraPresetDropdown;
        public Button saveCurrentViewButton;
        public TMP_InputField presetNameInput;

        [Header("Main Camera Settings")]
        public TMP_Dropdown cameraModeDropdown;
        public Toggle orthographicToggle;
        public Slider fieldOfViewSlider;
        public TextMeshProUGUI fieldOfViewValue;
        public Slider cameraDistanceSlider;
        public TextMeshProUGUI cameraDistanceValue;
        public Slider cameraHeightSlider;
        public TextMeshProUGUI cameraHeightValue;
        public Slider cameraRotationSlider;
        public TextMeshProUGUI cameraRotationValue;
        public Button resetViewButton;

        [Header("View Options")]
        public Toggle showGridToggle;
        public Toggle showAxesToggle;
        public Toggle showFloorToggle;
        public Toggle showWallsToggle;
        public Toggle wireframeModeToggle;
        public TMP_Dropdown renderQualityDropdown;

        [Header("Robot Visualization")]
        public Toggle showRobotTrajectoryToggle;
        public Slider trajectoryLengthSlider;
        public TextMeshProUGUI trajectoryLengthValue;
        public Toggle showRobotPathToggle;
        public Toggle showRobotGoalToggle;
        public Toggle showRobotVelocityVectorToggle;
        public Toggle showRobotSensorRaysToggle;
        public TMP_Dropdown sensorRayColorDropdown;
        public Slider sensorRayOpacitySlider;
        public TextMeshProUGUI sensorRayOpacityValue;

        [Header("Human Visualization")]
        public Toggle showHumanTrajectoriesToggle;
        public Toggle showHumanGoalsToggle;
        public Toggle showHumanVelocityVectorsToggle;
        public Toggle showHumanInteractionRadiusToggle;
        public Toggle colorHumansByStateToggle;
        public Slider humanOpacitySlider;
        public TextMeshProUGUI humanOpacityValue;

        [Header("Lighting & Post-Processing")]
        public Toggle shadowsToggle;
        public TMP_Dropdown shadowQualityDropdown;
        public Slider ambientIntensitySlider;
        public TextMeshProUGUI ambientIntensityValue;
        public Toggle postProcessingToggle;
        public Toggle antiAliasingToggle;

        [Header("Screenshot & Recording")]
        public Button takeScreenshotButton;
        public Button startRecordingButton;
        public Button stopRecordingButton;
        public TMP_InputField screenshotPrefixInput;
        public TMP_Dropdown screenshotResolutionDropdown;
        public Toggle includeUIInScreenshotToggle;
        public TextMeshProUGUI recordingStatusText;

        [Header("Multi-View")]
        public Toggle splitViewToggle;
        public TMP_Dropdown splitViewModeDropdown;
        public Button focusOnRobotButton;
        public Button focusOnHumanButton;
        public TMP_InputField targetAgentIDInput;
        public Button cycleViewButton;

        [Header("Performance")]
        public Toggle cullingToggle;
        public Slider renderDistanceSlider;
        public TextMeshProUGUI renderDistanceValue;
        public Toggle dynamicQualityToggle;
        public TextMeshProUGUI fpsDisplayText;

        [Header("Target Selection")]
        public Button selectTargetButton;
        public TargetSelectionPopup targetSelectionPopup;
        public Button nextTargetButton;
        public Button previousTargetButton;

        // ==================== DEBUG PANEL ====================
        [Header("Debug Visualization")]
        public Toggle showCollidersToggle;
        public Toggle showNavMeshToggle;
        public Toggle showOccupancyGridToggle;
        public Toggle showCostmapToggle;
        public Toggle showLaserScansToggle;
        public Toggle showBoundingBoxesToggle;
        public Toggle showAgentIDsToggle;
        public Toggle showDistanceLabelsToggle;
        public TMP_Dropdown debugOverlayDropdown;

        // ==================== ROS PANEL ====================
        [Header("ROS Settings")]
        public TMP_InputField rosPrefixInput;
        public Toggle rosEnableToggle;

        // ==================== Common Status ====================
        public TextMeshProUGUI statusText;
        public TextMeshProUGUI cameraInfoText;
        public bool showDebugLogs = true;

        // Private references
        private Supervisor _supervisor;
        private ScenarioManager _scenarioSupervisor;
        private Clock _clock;
        private CameraController _cameraController;
        private VisualizationManager _vizManager;
        private SimulationConfig _workingConfig;
        private bool _isPanelOpen = false;
        private bool _isInitialized = false;
        private bool _isRecording = false;
        private float _fpsUpdateInterval = 0.5f;
        private float _fpsAccumulator = 0f;
        private int _fpsFrameCount = 0;
        private float _fpsTimeLeft;
        private Dictionary<string, CameraPreset> _savedPresets = new Dictionary<string, CameraPreset>();

        [System.Serializable]
        public class CameraPreset
        {
            public Vector3 position;
            public Quaternion rotation;
            public bool orthographic;
            public float orthographicSize;
            public float fieldOfView;
        }

        #region Unity Lifecycle

        private void Start()
        {
            FindReferences();
            InitializeUI();
            SetupEvents();
            SetupTabs();
            LoadWorkingConfigAndUI();
            SetupCallbacks();
            LoadSettings();

            if (settingsPanel != null)
                settingsPanel.SetActive(false);

            _fpsTimeLeft = _fpsUpdateInterval;
        }

        private void Update()
        {
            UpdateCameraInfo();
            UpdateFPSDisplay();

            if (Input.GetKeyDown(KeyCode.V) && Input.GetKey(KeyCode.LeftControl))
                TogglePanel();
        }

        private void OnDestroy()
        {
            CleanupEvents();
            CleanupCallbacks();
        }

        #endregion

        #region Initialization

        private void FindReferences()
        {
            _supervisor = Supervisor.Instance;
            _scenarioSupervisor = FindObjectOfType<ScenarioManager>();
            _clock = Clock.Instance;
            _cameraController = FindObjectOfType<CameraController>();
            _vizManager = FindObjectOfType<VisualizationManager>();

            if (_cameraController != null)
                _cameraController.OnViewChanged += OnCameraViewChanged;

            if (_scenarioSupervisor == null && showDebugLogs)
                Debug.LogWarning("[UI] ScenarioManager not found");
        }

        private void InitializeUI()
        {
            // Agents panel dropdowns
            if (robotBehaviorDropdown != null)
            {
                robotBehaviorDropdown.ClearOptions();
                robotBehaviorDropdown.AddOptions(new List<string> { "normal", "cautious", "assertive", "socially_aware" });
            }
            if (humanControllerTypeDropdown != null)
            {
                humanControllerTypeDropdown.ClearOptions();
                humanControllerTypeDropdown.AddOptions(new List<string> { "SFM", "ONNX", "Hybrid" });
            }
            if (datasetDropdown != null)
                datasetDropdown.RefreshDatasetList();

            // View panel dropdowns
            InitializeViewDropdowns();
            InitializeSliders();

            // Scenario dropdown
            RefreshScenarioDropdown();
        }

        private void InitializeViewDropdowns()
        {
            if (cameraModeDropdown != null)
            {
                cameraModeDropdown.ClearOptions();
                cameraModeDropdown.AddOptions(new List<string> { "Free Camera", "Follow Robot", "Top-Down", "First Person (Robot)", "Orbit Robot", "Multi-Target", "Cinematic" });
            }
            if (cameraPresetDropdown != null)
            {
                cameraPresetDropdown.ClearOptions();
                cameraPresetDropdown.AddOptions(new List<string> { "Default View", "Top View", "Side View", "Front View", "Isometric", "Close-up Robot", "Overview" });
            }
            if (renderQualityDropdown != null)
            {
                renderQualityDropdown.ClearOptions();
                renderQualityDropdown.AddOptions(new List<string> { "Low", "Medium", "High", "Ultra", "Custom" });
                renderQualityDropdown.value = QualitySettings.GetQualityLevel();
            }
            if (splitViewModeDropdown != null)
            {
                splitViewModeDropdown.ClearOptions();
                splitViewModeDropdown.AddOptions(new List<string> { "2-View Horizontal", "2-View Vertical", "3-View", "4-View", "Picture-in-Picture" });
            }
            if (debugOverlayDropdown != null)
            {
                debugOverlayDropdown.ClearOptions();
                debugOverlayDropdown.AddOptions(new List<string> { "None", "FPS Counter", "Performance Stats", "Memory Usage", "Network Stats", "Full Debug" });
            }
            if (shadowQualityDropdown != null)
            {
                shadowQualityDropdown.ClearOptions();
                shadowQualityDropdown.AddOptions(new List<string> { "Disabled", "Hard Only", "Hard & Soft", "All" });
            }
            if (sensorRayColorDropdown != null)
            {
                sensorRayColorDropdown.ClearOptions();
                sensorRayColorDropdown.AddOptions(new List<string> { "Green (Free)", "Yellow (Warning)", "Red (Collision)", "Blue (Info)", "White (Default)", "Heatmap" });
            }
            if (screenshotResolutionDropdown != null)
            {
                screenshotResolutionDropdown.ClearOptions();
                screenshotResolutionDropdown.AddOptions(new List<string> { "1920x1080 (Full HD)", "1280x720 (HD)", "3840x2160 (4K)", "Screen Resolution", "Custom" });
            }
        }

        private void InitializeSliders()
        {
            // Agents panel sliders
            if (humanInteractionRadiusSlider != null)
            {
                humanInteractionRadiusSlider.minValue = 0.5f;
                humanInteractionRadiusSlider.maxValue = 3f;
                humanInteractionRadiusSlider.onValueChanged.AddListener(OnHumanInteractionRadiusChanged);
            }
            if (timeScaleSlider != null)
            {
                timeScaleSlider.minValue = 0.1f;
                timeScaleSlider.maxValue = 5f;
                timeScaleSlider.onValueChanged.AddListener(OnTimeScaleChanged);
            }

            // View panel sliders
            if (fieldOfViewSlider != null)
            {
                fieldOfViewSlider.minValue = 30f;
                fieldOfViewSlider.maxValue = 120f;
                fieldOfViewSlider.value = Camera.main?.fieldOfView ?? 60f;
                fieldOfViewSlider.onValueChanged.AddListener(OnFieldOfViewChanged);
            }
            if (cameraDistanceSlider != null)
            {
                cameraDistanceSlider.minValue = 2f;
                cameraDistanceSlider.maxValue = 50f;
                cameraDistanceSlider.value = 10f;
                cameraDistanceSlider.onValueChanged.AddListener(OnCameraDistanceChanged);
            }
            if (cameraHeightSlider != null)
            {
                cameraHeightSlider.minValue = 1f;
                cameraHeightSlider.maxValue = 30f;
                cameraHeightSlider.value = 5f;
                cameraHeightSlider.onValueChanged.AddListener(OnCameraHeightChanged);
            }
            if (cameraRotationSlider != null)
            {
                cameraRotationSlider.minValue = 0f;
                cameraRotationSlider.maxValue = 360f;
                cameraRotationSlider.value = 0f;
                cameraRotationSlider.onValueChanged.AddListener(OnCameraRotationChanged);
            }
            if (trajectoryLengthSlider != null)
            {
                trajectoryLengthSlider.minValue = 10;
                trajectoryLengthSlider.maxValue = 500;
                trajectoryLengthSlider.value = 100;
                trajectoryLengthSlider.onValueChanged.AddListener(OnTrajectoryLengthChanged);
            }
            if (sensorRayOpacitySlider != null)
            {
                sensorRayOpacitySlider.minValue = 0.1f;
                sensorRayOpacitySlider.maxValue = 1f;
                sensorRayOpacitySlider.value = 0.7f;
                sensorRayOpacitySlider.onValueChanged.AddListener(OnSensorRayOpacityChanged);
            }
            if (humanOpacitySlider != null)
            {
                humanOpacitySlider.minValue = 0.3f;
                humanOpacitySlider.maxValue = 1f;
                humanOpacitySlider.value = 1f;
                humanOpacitySlider.onValueChanged.AddListener(OnHumanOpacityChanged);
            }
            if (ambientIntensitySlider != null)
            {
                ambientIntensitySlider.minValue = 0f;
                ambientIntensitySlider.maxValue = 2f;
                ambientIntensitySlider.value = 1f;
                ambientIntensitySlider.onValueChanged.AddListener(OnAmbientIntensityChanged);
            }
            if (renderDistanceSlider != null)
            {
                renderDistanceSlider.minValue = 50f;
                renderDistanceSlider.maxValue = 500f;
                renderDistanceSlider.value = 200f;
                renderDistanceSlider.onValueChanged.AddListener(OnRenderDistanceChanged);
            }
        }

        private void SetupEvents()
        {
            if (_supervisor != null)
            {
                _supervisor.OnConfigChanged += OnSupervisorConfigChanged;
                if (!_supervisor.IsInitialized)
                    _supervisor.OnInitialized += OnSupervisorInitialized;
                else
                    LoadWorkingConfigAndUI();
            }
            if (_scenarioSupervisor != null)
                _scenarioSupervisor.OnScenarioLoaded += OnScenarioLoaded;
        }

        private void CleanupEvents()
        {
            if (_supervisor != null)
            {
                _supervisor.OnConfigChanged -= OnSupervisorConfigChanged;
                _supervisor.OnInitialized -= OnSupervisorInitialized;
            }
            if (_scenarioSupervisor != null)
                _scenarioSupervisor.OnScenarioLoaded -= OnScenarioLoaded;
            if (_cameraController != null)
                _cameraController.OnViewChanged -= OnCameraViewChanged;
        }

        private void SetupTabs()
        {
            SetActivePanel(agentsPanel, true);
            SetActivePanel(viewPanel, false);
            SetActivePanel(debugPanel, false);
            SetActivePanel(rosPanel, false);

            if (agentsTabButton != null) agentsTabButton.onClick.AddListener(() => ShowTab(agentsPanel, agentsTabButton));
            if (viewTabButton != null) viewTabButton.onClick.AddListener(() => ShowTab(viewPanel, viewTabButton));
            if (debugTabButton != null) debugTabButton.onClick.AddListener(() => ShowTab(debugPanel, debugTabButton));
            if (rosTabButton != null) rosTabButton.onClick.AddListener(() => ShowTab(rosPanel, rosTabButton));
        }

        private void ShowTab(GameObject panelToShow, Button activeButton)
        {
            SetActivePanel(agentsPanel, false);
            SetActivePanel(viewPanel, false);
            SetActivePanel(debugPanel, false);
            SetActivePanel(rosPanel, false);
            SetActivePanel(panelToShow, true);

            ResetTabButtons();
            if (activeButton != null)
            {
                ColorBlock colors = activeButton.colors;
                colors.normalColor = new Color(0.8f, 0.8f, 0.8f);
                activeButton.colors = colors;
            }
        }

        private void ResetTabButtons()
        {
            ResetButtonColor(agentsTabButton);
            ResetButtonColor(viewTabButton);
            ResetButtonColor(debugTabButton);
            ResetButtonColor(rosTabButton);
        }

        private void ResetButtonColor(Button btn)
        {
            if (btn == null) return;
            ColorBlock colors = btn.colors;
            colors.normalColor = Color.white;
            btn.colors = colors;
        }

        private void SetActivePanel(GameObject panel, bool active)
        {
            if (panel != null) panel.SetActive(active);
        }

        private void SetupCallbacks()
        {
            // Agents panel
            if (applyButton != null) applyButton.onClick.AddListener(ApplyAllSettings);
            if (resetButton != null) resetButton.onClick.AddListener(ResetSimulation);
            if (saveButton != null) saveButton.onClick.AddListener(SaveCurrentConfig);
            if (toggleButton != null) toggleButton.onClick.AddListener(TogglePanel);
            if (pauseToggle != null) pauseToggle.onValueChanged.AddListener(OnPauseToggle);
            if (applyScenarioButton != null) applyScenarioButton.onClick.AddListener(ApplySelectedScenario);
            if (scenarioDropdown != null) scenarioDropdown.onValueChanged.AddListener(OnScenarioSelected);
            if (showWorkingConfigButton != null) showWorkingConfigButton.onClick.AddListener(ShowWorkingConfig);

            // View panel
            if (cameraPresetDropdown != null) cameraPresetDropdown.onValueChanged.AddListener(OnCameraPresetChanged);
            if (cameraModeDropdown != null) cameraModeDropdown.onValueChanged.AddListener(OnCameraModeChanged);
            if (orthographicToggle != null) orthographicToggle.onValueChanged.AddListener(OnOrthographicToggle);
            if (resetViewButton != null) resetViewButton.onClick.AddListener(ResetView);
            if (saveCurrentViewButton != null) saveCurrentViewButton.onClick.AddListener(SaveCurrentViewAsPreset);
            if (showGridToggle != null) showGridToggle.onValueChanged.AddListener(OnShowGridToggle);
            if (wireframeModeToggle != null) wireframeModeToggle.onValueChanged.AddListener(OnWireframeModeToggle);
            if (splitViewToggle != null) splitViewToggle.onValueChanged.AddListener(OnSplitViewToggle);
            if (postProcessingToggle != null) postProcessingToggle.onValueChanged.AddListener(OnPostProcessingToggle);
            if (takeScreenshotButton != null) takeScreenshotButton.onClick.AddListener(TakeScreenshot);
            if (startRecordingButton != null) startRecordingButton.onClick.AddListener(StartRecording);
            if (stopRecordingButton != null) stopRecordingButton.onClick.AddListener(StopRecording);
            if (focusOnRobotButton != null) focusOnRobotButton.onClick.AddListener(FocusOnRobot);
            if (focusOnHumanButton != null) focusOnHumanButton.onClick.AddListener(FocusOnHuman);
            if (cycleViewButton != null) cycleViewButton.onClick.AddListener(CycleView);
            if (selectTargetButton != null) selectTargetButton.onClick.AddListener(ShowTargetSelection);
            if (nextTargetButton != null) nextTargetButton.onClick.AddListener(() => targetSelectionPopup?.CycleNext());
            if (previousTargetButton != null) previousTargetButton.onClick.AddListener(() => targetSelectionPopup?.CyclePrevious());
        }

        private void CleanupCallbacks()
        {
            // Agents panel
            if (applyButton != null) applyButton.onClick.RemoveListener(ApplyAllSettings);
            if (resetButton != null) resetButton.onClick.RemoveListener(ResetSimulation);
            if (saveButton != null) saveButton.onClick.RemoveListener(SaveCurrentConfig);
            if (toggleButton != null) toggleButton.onClick.RemoveListener(TogglePanel);
            if (pauseToggle != null) pauseToggle.onValueChanged.RemoveListener(OnPauseToggle);
            if (applyScenarioButton != null) applyScenarioButton.onClick.RemoveListener(ApplySelectedScenario);
            if (scenarioDropdown != null) scenarioDropdown.onValueChanged.RemoveListener(OnScenarioSelected);
            if (showWorkingConfigButton != null) showWorkingConfigButton.onClick.RemoveListener(ShowWorkingConfig);

            // View panel
            if (cameraPresetDropdown != null) cameraPresetDropdown.onValueChanged.RemoveListener(OnCameraPresetChanged);
            if (cameraModeDropdown != null) cameraModeDropdown.onValueChanged.RemoveListener(OnCameraModeChanged);
            if (orthographicToggle != null) orthographicToggle.onValueChanged.RemoveListener(OnOrthographicToggle);
            if (resetViewButton != null) resetViewButton.onClick.RemoveListener(ResetView);
            if (saveCurrentViewButton != null) saveCurrentViewButton.onClick.RemoveListener(SaveCurrentViewAsPreset);
            if (showGridToggle != null) showGridToggle.onValueChanged.RemoveListener(OnShowGridToggle);
            if (wireframeModeToggle != null) wireframeModeToggle.onValueChanged.RemoveListener(OnWireframeModeToggle);
            if (splitViewToggle != null) splitViewToggle.onValueChanged.RemoveListener(OnSplitViewToggle);
            if (postProcessingToggle != null) postProcessingToggle.onValueChanged.RemoveListener(OnPostProcessingToggle);
            if (takeScreenshotButton != null) takeScreenshotButton.onClick.RemoveListener(TakeScreenshot);
            if (startRecordingButton != null) startRecordingButton.onClick.RemoveListener(StartRecording);
            if (stopRecordingButton != null) stopRecordingButton.onClick.RemoveListener(StopRecording);
            if (focusOnRobotButton != null) focusOnRobotButton.onClick.RemoveListener(FocusOnRobot);
            if (focusOnHumanButton != null) focusOnHumanButton.onClick.RemoveListener(FocusOnHuman);
            if (cycleViewButton != null) cycleViewButton.onClick.RemoveListener(CycleView);
            if (selectTargetButton != null) selectTargetButton.onClick.RemoveListener(ShowTargetSelection);
            if (nextTargetButton != null) nextTargetButton.onClick.RemoveAllListeners();
            if (previousTargetButton != null) previousTargetButton.onClick.RemoveAllListeners();
        }

        #endregion

        #region Data Loading

        private void OnSupervisorInitialized() => LoadWorkingConfigAndUI();

        private void OnSupervisorConfigChanged(SimulationConfig config)
        {
            if (showDebugLogs) Debug.Log("[UI] Supervisor config changed, updating UI");
            LoadWorkingConfigFromSupervisor();
            LoadUIFromConfig();
        }

        private void LoadWorkingConfigAndUI()
        {
            LoadWorkingConfigFromSupervisor();
            LoadUIFromConfig();
            _isInitialized = true;
        }

        private void LoadWorkingConfigFromSupervisor()
        {
            if (_supervisor != null && _supervisor.ActiveConfig != null)
            {
                _workingConfig = _supervisor.ActiveConfig.Clone();
                if (showDebugLogs) Debug.Log("[UI] Working config loaded from supervisor");
            }
            else
            {
                _workingConfig = ScriptableObject.CreateInstance<SimulationConfig>();
                if (showDebugLogs) Debug.Log("[UI] Created new working config");
            }
        }

        private void OnScenarioLoaded(ScenarioData scenario) => RefreshScenarioDropdown();

        private void RefreshScenarioDropdown()
        {
            if (scenarioDropdown == null || _scenarioSupervisor == null) return;
            var scenarios = _scenarioSupervisor.GetAvailableScenarios();
            scenarioDropdown.ClearOptions();
            scenarioDropdown.AddOptions(new List<string>(scenarios));
            string current = _scenarioSupervisor.CurrentScenarioId;
            int idx = scenarios.IndexOf(current);
            if (idx >= 0) scenarioDropdown.value = idx;
        }

        private void LoadUIFromConfig()
        {
            if (_workingConfig == null) return;

            // Agents panel
            if (environmentCountInput != null) environmentCountInput.text = _workingConfig.EnvironmentCount.ToString();
            if (environmentSpacingInput != null) environmentSpacingInput.text = _workingConfig.EnvironmentSpacing.ToString();
            if (minHumansInput != null) minHumansInput.text = _workingConfig.MinHumans.ToString();
            if (maxHumansInput != null) maxHumansInput.text = _workingConfig.MaxHumans.ToString();
            if (minAgentDistanceInput != null) minAgentDistanceInput.text = _workingConfig.MinAgentDistance.ToString();
            if (robotSpeedInput != null) robotSpeedInput.text = _workingConfig.RobotSpeed.ToString();
            if (robotSensorRangeInput != null) robotSensorRangeInput.text = _workingConfig.RobotSensorRange.ToString();
            if (humanDefaultSpeedInput != null) humanDefaultSpeedInput.text = _workingConfig.HumanDefaultSpeed.ToString();
            if (humanInteractionRadiusSlider != null) humanInteractionRadiusSlider.value = _workingConfig.HumanInteractionRadius;
            if (timeScaleSlider != null) timeScaleSlider.value = _workingConfig.TimeScale;
            if (rosPrefixInput != null) rosPrefixInput.text = _workingConfig.RosPrefix;
            if (rosEnableToggle != null) rosEnableToggle.isOn = _workingConfig.EnableROS;

            // Dropdowns
            if (robotBehaviorDropdown != null)
            {
                int idx = GetRobotBehaviorIndex(_workingConfig.RobotBehavior);
                if (idx >= 0 && idx < robotBehaviorDropdown.options.Count) robotBehaviorDropdown.value = idx;
            }
            if (humanControllerTypeDropdown != null)
            {
                int ct = _workingConfig.HumanControllerType;
                if (ct >= 0 && ct < humanControllerTypeDropdown.options.Count) humanControllerTypeDropdown.value = ct;
            }
            RefreshScenarioDropdown();
        }

        #endregion

        #region Agents Panel Callbacks

        private void TogglePanel()
        {
            _isPanelOpen = !_isPanelOpen;
            if (settingsPanel != null) settingsPanel.SetActive(_isPanelOpen);
            if (_isPanelOpen)
            {
                RefreshScenarioDropdown();
                datasetDropdown?.RefreshDatasetList();
            }
        }

        private void ApplyAllSettings()
        {
            if (_workingConfig == null || _supervisor == null) return;

            if (environmentCountInput != null && int.TryParse(environmentCountInput.text, out int envC)) _workingConfig.EnvironmentCount = envC;
            if (environmentSpacingInput != null && float.TryParse(environmentSpacingInput.text, out float sp)) _workingConfig.EnvironmentSpacing = sp;
            if (minHumansInput != null && int.TryParse(minHumansInput.text, out int minH)) _workingConfig.MinHumans = minH;
            if (maxHumansInput != null && int.TryParse(maxHumansInput.text, out int maxH)) _workingConfig.MaxHumans = maxH;
            if (minAgentDistanceInput != null && float.TryParse(minAgentDistanceInput.text, out float minD)) _workingConfig.MinAgentDistance = minD;
            if (robotSpeedInput != null && float.TryParse(robotSpeedInput.text, out float rSpeed)) _workingConfig.RobotSpeed = rSpeed;
            if (robotSensorRangeInput != null && float.TryParse(robotSensorRangeInput.text, out float rRange)) _workingConfig.RobotSensorRange = rRange;
            if (robotBehaviorDropdown != null) _workingConfig.RobotBehavior = GetRobotBehaviorValue(robotBehaviorDropdown.value);
            if (humanDefaultSpeedInput != null && float.TryParse(humanDefaultSpeedInput.text, out float hSpeed)) _workingConfig.HumanDefaultSpeed = hSpeed;
            if (humanControllerTypeDropdown != null) _workingConfig.HumanControllerType = humanControllerTypeDropdown.value;
            if (humanInteractionRadiusSlider != null) _workingConfig.HumanInteractionRadius = humanInteractionRadiusSlider.value;
            if (timeScaleSlider != null) _workingConfig.TimeScale = timeScaleSlider.value;
            if (rosPrefixInput != null) _workingConfig.RosPrefix = rosPrefixInput.text;
            if (rosEnableToggle != null) _workingConfig.EnableROS = rosEnableToggle.isOn;
            if (datasetDropdown != null)
            {
                string path = datasetDropdown.GetPendingPath();
                if (!string.IsNullOrEmpty(path)) _workingConfig.DatasetPath = path;
            }

            _supervisor.UpdateConfig(_workingConfig);
            UpdateStatus("Paramètres appliqués");
        }

        private void ResetSimulation() => _scenarioSupervisor?.ResetAndReapply();

        private void SaveCurrentConfig()
        {
            if (_supervisor != null)
            {
                _supervisor.SaveDefaultConfig();
                UpdateStatus("Configuration sauvegardée");
            }
        }

        private void OnPauseToggle(bool isPaused)
        {
            if (_clock != null)
            {
                if (isPaused) _clock.Pause();
                else _clock.Resume();
                UpdateStatus(isPaused ? "Simulation en pause" : "Simulation reprise");
            }
        }

        private void OnTimeScaleChanged(float value)
        {
            if (timeScaleValue != null) timeScaleValue.text = $"{value:F1}x";
            if (_workingConfig != null) _workingConfig.TimeScale = value;
        }

        private void OnHumanInteractionRadiusChanged(float value)
        {
            if (humanInteractionRadiusValue != null) humanInteractionRadiusValue.text = $"{value:F1}m";
            if (_workingConfig != null) _workingConfig.HumanInteractionRadius = value;
        }

        private void OnScenarioSelected(int index)
        {
            if (scenarioDropdown != null && _scenarioSupervisor != null)
            {
                var list = new List<string>(_scenarioSupervisor.GetAvailableScenarios());
                if (index >= 0 && index < list.Count)
                    UpdateStatus($"Scénario sélectionné: {list[index]}");
            }
        }

        private void ApplySelectedScenario()
        {
            if (scenarioDropdown == null || _scenarioSupervisor == null) return;
            var list = new List<string>(_scenarioSupervisor.GetAvailableScenarios());
            if (scenarioDropdown.value >= 0 && scenarioDropdown.value < list.Count)
            {
                string name = list[scenarioDropdown.value];
                _scenarioSupervisor.LoadScenario(name);
                UpdateStatus($"Scénario appliqué: {name}");
            }
        }

        private void ShowWorkingConfig()
        {
            if (_workingConfig == null) { UpdateStatus("Aucune config de travail"); return; }
            string info = $"Config de travail :\n" +
                          $"Environnements: {_workingConfig.EnvironmentCount}\n" +
                          $"Humains: {_workingConfig.MinHumans}-{_workingConfig.MaxHumans}\n" +
                          $"Robot: {_workingConfig.RobotBehavior} @ {_workingConfig.RobotSpeed} m/s\n" +
                          $"Time scale: {_workingConfig.TimeScale}\n" +
                          $"Dataset: {_workingConfig.DatasetPath}\n" +
                          $"Scenario: {_workingConfig.DefaultScenario}";
            Debug.Log(info);
            UpdateStatus("Config affichée dans la console");
        }

        #endregion

        #region View Panel Callbacks

        private void ApplyCameraSettings()
        {
            if (_cameraController == null) return;
            if (fieldOfViewSlider != null && Camera.main != null) Camera.main.fieldOfView = fieldOfViewSlider.value;
            if (orthographicToggle != null && Camera.main != null) Camera.main.orthographic = orthographicToggle.isOn;
            if (cameraModeDropdown != null) _cameraController.SetCameraMode(cameraModeDropdown.value);
        }

        private void ApplyViewOptions()
        {
            if (_vizManager == null) return;
            _vizManager.showGrid = showGridToggle?.isOn ?? true;
            _vizManager.showAxes = showAxesToggle?.isOn ?? false;
            _vizManager.showFloor = showFloorToggle?.isOn ?? true;
            _vizManager.showWalls = showWallsToggle?.isOn ?? true;
            _vizManager.wireframeMode = wireframeModeToggle?.isOn ?? false;
        }

        private void ApplyVisualizationOptions()
        {
            if (_vizManager == null) return;
            _vizManager.showRobotTrajectory = showRobotTrajectoryToggle?.isOn ?? true;
            _vizManager.showRobotPath = showRobotPathToggle?.isOn ?? true;
            _vizManager.showRobotGoal = showRobotGoalToggle?.isOn ?? true;
            _vizManager.showRobotVelocityVector = showRobotVelocityVectorToggle?.isOn ?? false;
            _vizManager.showRobotSensorRays = showRobotSensorRaysToggle?.isOn ?? true;
            if (trajectoryLengthSlider != null) _vizManager.trajectoryLength = (int)trajectoryLengthSlider.value;
            _vizManager.showHumanTrajectories = showHumanTrajectoriesToggle?.isOn ?? true;
            _vizManager.showHumanGoals = showHumanGoalsToggle?.isOn ?? true;
            _vizManager.showHumanVelocityVectors = showHumanVelocityVectorsToggle?.isOn ?? false;
            _vizManager.showHumanInteractionRadius = showHumanInteractionRadiusToggle?.isOn ?? true;
            _vizManager.colorHumansByState = colorHumansByStateToggle?.isOn ?? true;
        }

        private void ApplyDebugOptions()
        {
            if (_vizManager == null) return;
            _vizManager.showColliders = showCollidersToggle?.isOn ?? false;
            _vizManager.showNavMesh = showNavMeshToggle?.isOn ?? false;
            _vizManager.showOccupancyGrid = showOccupancyGridToggle?.isOn ?? false;
            _vizManager.showCostmap = showCostmapToggle?.isOn ?? false;
            _vizManager.showLaserScans = showLaserScansToggle?.isOn ?? true;
            _vizManager.showBoundingBoxes = showBoundingBoxesToggle?.isOn ?? false;
            _vizManager.showAgentIDs = showAgentIDsToggle?.isOn ?? true;
            _vizManager.showDistanceLabels = showDistanceLabelsToggle?.isOn ?? false;
        }

        private void ApplyLightingSettings()
        {
            if (shadowsToggle != null) QualitySettings.shadows = shadowsToggle.isOn ? ShadowQuality.All : ShadowQuality.Disable;
            if (ambientIntensitySlider != null) RenderSettings.ambientIntensity = ambientIntensitySlider.value;
            if (antiAliasingToggle != null) QualitySettings.antiAliasing = antiAliasingToggle.isOn ? 4 : 1;
        }

        private void ApplyPerformanceSettings()
        {
            if (cullingToggle != null && Camera.main != null) Camera.main.useOcclusionCulling = cullingToggle.isOn;
            if (renderDistanceSlider != null && Camera.main != null) Camera.main.farClipPlane = renderDistanceSlider.value;
        }

        private void ResetView() => _cameraController?.ResetToDefaultView();

        private void OnCameraPresetChanged(int index)
        {
            if (cameraPresetDropdown == null) return;
            string preset = cameraPresetDropdown.options[index].text;
            ApplyPreset(preset);
        }

        private void OnCameraModeChanged(int mode)
        {
            if (_cameraController != null)
            {
                _cameraController.SetCameraMode(mode);
                UpdateStatus($"Mode caméra: {cameraModeDropdown.options[mode].text}");
            }
        }

        private void OnOrthographicToggle(bool isOrtho)
        {
            if (Camera.main != null) Camera.main.orthographic = isOrtho;
        }

        private void OnFieldOfViewChanged(float value) => fieldOfViewValue?.SetText($"{value:F0}°");
        private void OnCameraDistanceChanged(float value) => cameraDistanceValue?.SetText($"{value:F1}m");
        private void OnCameraHeightChanged(float value) => cameraHeightValue?.SetText($"{value:F1}m");
        private void OnCameraRotationChanged(float value) => cameraRotationValue?.SetText($"{value:F0}°");
        private void OnTrajectoryLengthChanged(float value) => trajectoryLengthValue?.SetText($"{(int)value} pts");
        private void OnSensorRayOpacityChanged(float value) => sensorRayOpacityValue?.SetText($"{value:P0}");
        private void OnHumanOpacityChanged(float value) => humanOpacityValue?.SetText($"{value:P0}");
        private void OnAmbientIntensityChanged(float value) => ambientIntensityValue?.SetText($"{value:F1}x");
        private void OnRenderDistanceChanged(float value) => renderDistanceValue?.SetText($"{value:F0}m");

        private void OnSplitViewToggle(bool isOn)
        {
            if (splitViewModeDropdown != null) splitViewModeDropdown.interactable = isOn;
            _cameraController?.SetSplitView(isOn, splitViewModeDropdown?.value ?? 0);
        }

        private void OnShowGridToggle(bool show) { if (_vizManager != null) _vizManager.showGrid = show; }
        private void OnWireframeModeToggle(bool wire) { if (_vizManager != null) _vizManager.wireframeMode = wire; }
        private void OnPostProcessingToggle(bool enable)
        {
            var vol = FindObjectOfType<UnityEngine.Rendering.Volume>();
            if (vol != null) vol.enabled = enable;
        }
        private void OnCameraViewChanged() => UpdateCameraInfo();
        private void ShowTargetSelection() => targetSelectionPopup?.ShowPopup();

        private void TakeScreenshot()
        {
            string prefix = screenshotPrefixInput?.text ?? "screenshot";
            string filename = $"{prefix}_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";
            string path = System.IO.Path.Combine(Application.dataPath, "../Screenshots", filename);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            ScreenCapture.CaptureScreenshot(path);
            UpdateStatus($"Capture d'écran: {filename}");
        }

        private void StartRecording()
        {
            if (_isRecording) return;
            _isRecording = true;
            if (startRecordingButton != null) startRecordingButton.interactable = false;
            if (stopRecordingButton != null) stopRecordingButton.interactable = true;
            if (recordingStatusText != null) recordingStatusText.text = "● REC";
            UpdateStatus("Enregistrement démarré");
        }

        private void StopRecording()
        {
            if (!_isRecording) return;
            _isRecording = false;
            if (startRecordingButton != null) startRecordingButton.interactable = true;
            if (stopRecordingButton != null) stopRecordingButton.interactable = false;
            if (recordingStatusText != null) recordingStatusText.text = "";
            UpdateStatus("Enregistrement arrêté");
        }

        private void FocusOnRobot() => _cameraController?.FocusOnRobot();
        private void FocusOnHuman()
        {
            if (_cameraController != null && targetAgentIDInput != null && int.TryParse(targetAgentIDInput.text, out int id))
                _cameraController.FocusOnAgent(id);
        }
        private void CycleView() => _cameraController?.CycleToNextView();

        #endregion

        #region Presets

        private void SaveCurrentViewAsPreset()
        {
            if (Camera.main == null) return;
            string name = presetNameInput?.text;
            if (string.IsNullOrEmpty(name)) name = $"Preset_{System.DateTime.Now.Ticks}";
            var preset = new CameraPreset
            {
                position = Camera.main.transform.position,
                rotation = Camera.main.transform.rotation,
                orthographic = Camera.main.orthographic,
                orthographicSize = Camera.main.orthographicSize,
                fieldOfView = Camera.main.fieldOfView
            };
            _savedPresets[name] = preset;
            UpdatePresetDropdown();
            UpdateStatus($"Preset '{name}' sauvegardé");
        }

        private void ApplyPreset(string presetName)
        {
            if (_savedPresets.TryGetValue(presetName, out var preset))
                ApplyPreset(preset);
            else
                ApplyDefaultPreset(presetName);
        }

        private void ApplyPreset(CameraPreset preset)
        {
            if (_cameraController != null)
            {
                _cameraController.SetCameraTransform(preset.position, preset.rotation);
                if (Camera.main != null)
                {
                    Camera.main.orthographic = preset.orthographic;
                    Camera.main.orthographicSize = preset.orthographicSize;
                    Camera.main.fieldOfView = preset.fieldOfView;
                }
            }
        }

        private void ApplyDefaultPreset(string presetName)
        {
            if (_cameraController == null) return;
            switch (presetName)
            {
                case "Top View":
                    _cameraController.SetCameraTransform(new Vector3(0, 20, 0), Quaternion.Euler(90, 0, 0));
                    break;
                case "Side View":
                    _cameraController.SetCameraTransform(new Vector3(-20, 5, 0), Quaternion.Euler(15, 90, 0));
                    break;
                case "Front View":
                    _cameraController.SetCameraTransform(new Vector3(0, 5, -20), Quaternion.Euler(15, 0, 0));
                    break;
                case "Isometric":
                    _cameraController.SetCameraTransform(new Vector3(-15, 15, -15), Quaternion.Euler(35, 45, 0));
                    break;
                default:
                    _cameraController.ResetToDefaultView();
                    break;
            }
        }

        private void UpdatePresetDropdown()
        {
            if (cameraPresetDropdown == null) return;
            var options = new List<string>(_savedPresets.Keys);
            options.InsertRange(0, new[] { "Default View", "Top View", "Side View", "Front View", "Isometric" });
            cameraPresetDropdown.ClearOptions();
            cameraPresetDropdown.AddOptions(options);
        }

        #endregion

        #region Helpers

        private int GetRobotBehaviorIndex(string behavior)
        {
            switch (behavior?.ToLower())
            {
                case "normal": return 0;
                case "cautious": return 1;
                case "assertive": return 2;
                case "socially_aware": return 3;
                default: return 0;
            }
        }

        private string GetRobotBehaviorValue(int index)
        {
            switch (index)
            {
                case 0: return "normal";
                case 1: return "cautious";
                case 2: return "assertive";
                case 3: return "socially_aware";
                default: return "normal";
            }
        }

        private void LoadSettings()
        {
            if (showGridToggle != null) showGridToggle.isOn = PlayerPrefs.GetInt("Viz_ShowGrid", 1) == 1;
            if (showRobotTrajectoryToggle != null) showRobotTrajectoryToggle.isOn = PlayerPrefs.GetInt("Viz_ShowTrajectory", 1) == 1;
            if (fieldOfViewSlider != null) fieldOfViewSlider.value = PlayerPrefs.GetFloat("Viz_FOV", 60f);
        }

        private void SaveSettings()
        {
            PlayerPrefs.SetInt("Viz_ShowGrid", showGridToggle?.isOn == true ? 1 : 0);
            PlayerPrefs.SetInt("Viz_ShowTrajectory", showRobotTrajectoryToggle?.isOn == true ? 1 : 0);
            PlayerPrefs.SetFloat("Viz_FOV", fieldOfViewSlider?.value ?? 60f);
            PlayerPrefs.Save();
        }

        private void UpdateCameraInfo()
        {
            if (cameraInfoText == null || Camera.main == null) return;
            Vector3 pos = Camera.main.transform.position;
            Vector3 rot = Camera.main.transform.eulerAngles;
            cameraInfoText.text = $"Pos: ({pos.x:F1}, {pos.y:F1}, {pos.z:F1}) | Rot: ({rot.x:F0}°, {rot.y:F0}°, {rot.z:F0}°)";
        }

        private void UpdateFPSDisplay()
        {
            if (fpsDisplayText == null) return;
            _fpsTimeLeft -= Time.deltaTime;
            _fpsAccumulator += Time.timeScale / Time.deltaTime;
            _fpsFrameCount++;
            if (_fpsTimeLeft <= 0.0f)
            {
                float fps = _fpsAccumulator / _fpsFrameCount;
                fpsDisplayText.text = $"FPS: {fps:F0}";
                fpsDisplayText.color = fps < 30 ? Color.red : (fps < 60 ? Color.yellow : Color.green);
                _fpsTimeLeft = _fpsUpdateInterval;
                _fpsAccumulator = 0f;
                _fpsFrameCount = 0;
            }
        }

        private void UpdateStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
                CancelInvoke(nameof(ClearStatus));
                Invoke(nameof(ClearStatus), 3f);
            }
            if (showDebugLogs) Debug.Log($"[UI] {message}");
        }

        private void ClearStatus()
        {
            if (statusText != null) statusText.text = "";
        }

        #endregion
    }
}