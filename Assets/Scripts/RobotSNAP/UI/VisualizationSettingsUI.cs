using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using RobotSNAP.Core;

namespace RobotSNAP.UI
{
    public class VisualizationSettingsUI : MonoBehaviour
    {
        [Header("References")]
        public GameObject visualizationPanel;
        public Button toggleButton;
        public Button applyButton;
        public Button resetViewButton;
        
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
        
        [Header("Status")]
        public TextMeshProUGUI statusText;
        public TextMeshProUGUI cameraInfoText;
        public bool showDebugLogs = true;

        [Header("Target Selection")]
        public Button selectTargetButton;
        public TargetSelectionPopup targetSelectionPopup;
        public Button nextTargetButton;
        public Button previousTargetButton;
        
        private CameraController _cameraController;
        private VisualizationManager _vizManager;
        private Supervisor _supervisor;
        private Clock _clock;
        private bool _isPanelOpen = false;
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
            SetupCallbacks();
            LoadSettings();
            
            if (visualizationPanel != null)
                visualizationPanel.SetActive(false);
            
            _fpsTimeLeft = _fpsUpdateInterval;
        }
        
        private void Update()
        {
            UpdateCameraInfo();
            UpdateFPSDisplay();
            
            if (Input.GetKeyDown(KeyCode.V) && Input.GetKey(KeyCode.LeftControl))
            {
                TogglePanel();
            }
        }
        
        private void OnDestroy()
        {
            if (_cameraController != null)
            {
                _cameraController.OnViewChanged -= OnCameraViewChanged;
            }
            
            // Cleanup callbacks
            CleanupCallbacks();
        }
        
        #endregion
        
        #region Initialization
        
        private void FindReferences()
        {
            _cameraController = FindObjectOfType<CameraController>();
            _vizManager = FindObjectOfType<VisualizationManager>();
            _supervisor = Supervisor.Instance;
            _clock = Clock.Instance;
            
            if (_cameraController != null)
                _cameraController.OnViewChanged += OnCameraViewChanged;
        }
        
        private void InitializeUI()
        {
            InitializeCameraModeDropdown();
            InitializePresetDropdown();
            InitializeRenderQualityDropdown();
            InitializeSplitViewDropdown();
            InitializeDebugOverlayDropdown();
            InitializeShadowQualityDropdown();
            InitializeSensorRayColorDropdown();
            InitializeScreenshotResolutionDropdown();
            InitializeSliders();
            LoadSavedPresets();
        }
        
        private void InitializeCameraModeDropdown()
        {
            if (cameraModeDropdown != null)
            {
                cameraModeDropdown.ClearOptions();
                cameraModeDropdown.AddOptions(new List<string> 
                { 
                    "Free Camera", 
                    "Follow Robot", 
                    "Top-Down", 
                    "First Person (Robot)", 
                    "Orbit Robot",
                    "Multi-Target",
                    "Cinematic"
                });
            }
        }
        
        private void InitializePresetDropdown()
        {
            if (cameraPresetDropdown != null)
            {
                cameraPresetDropdown.ClearOptions();
                cameraPresetDropdown.AddOptions(new List<string> 
                { 
                    "Default View",
                    "Top View", 
                    "Side View", 
                    "Front View",
                    "Isometric",
                    "Close-up Robot",
                    "Overview"
                });
            }
        }
        
        private void InitializeRenderQualityDropdown()
        {
            if (renderQualityDropdown != null)
            {
                renderQualityDropdown.ClearOptions();
                renderQualityDropdown.AddOptions(new List<string> 
                { 
                    "Low", 
                    "Medium", 
                    "High", 
                    "Ultra",
                    "Custom"
                });
                renderQualityDropdown.value = QualitySettings.GetQualityLevel();
            }
        }
        
        private void InitializeSplitViewDropdown()
        {
            if (splitViewModeDropdown != null)
            {
                splitViewModeDropdown.ClearOptions();
                splitViewModeDropdown.AddOptions(new List<string> 
                { 
                    "2-View Horizontal",
                    "2-View Vertical", 
                    "3-View", 
                    "4-View",
                    "Picture-in-Picture"
                });
            }
        }
        
        private void InitializeDebugOverlayDropdown()
        {
            if (debugOverlayDropdown != null)
            {
                debugOverlayDropdown.ClearOptions();
                debugOverlayDropdown.AddOptions(new List<string> 
                { 
                    "None",
                    "FPS Counter", 
                    "Performance Stats", 
                    "Memory Usage",
                    "Network Stats",
                    "Full Debug"
                });
            }
        }
        
        private void InitializeShadowQualityDropdown()
        {
            if (shadowQualityDropdown != null)
            {
                shadowQualityDropdown.ClearOptions();
                shadowQualityDropdown.AddOptions(new List<string> 
                { 
                    "Disabled",
                    "Hard Only", 
                    "Hard & Soft", 
                    "All"
                });
            }
        }
        
        private void InitializeSensorRayColorDropdown()
        {
            if (sensorRayColorDropdown != null)
            {
                sensorRayColorDropdown.ClearOptions();
                sensorRayColorDropdown.AddOptions(new List<string> 
                { 
                    "Green (Free)",
                    "Yellow (Warning)", 
                    "Red (Collision)", 
                    "Blue (Info)",
                    "White (Default)",
                    "Heatmap"
                });
            }
        }
        
        private void InitializeScreenshotResolutionDropdown()
        {
            if (screenshotResolutionDropdown != null)
            {
                screenshotResolutionDropdown.ClearOptions();
                screenshotResolutionDropdown.AddOptions(new List<string> 
                { 
                    "1920x1080 (Full HD)",
                    "1280x720 (HD)", 
                    "3840x2160 (4K)", 
                    "Screen Resolution",
                    "Custom"
                });
            }
        }
        
        private void InitializeSliders()
        {
            // Field of View
            if (fieldOfViewSlider != null)
            {
                fieldOfViewSlider.minValue = 30f;
                fieldOfViewSlider.maxValue = 120f;
                fieldOfViewSlider.value = Camera.main?.fieldOfView ?? 60f;
                fieldOfViewSlider.onValueChanged.AddListener(OnFieldOfViewChanged);
            }
            
            // Camera Distance
            if (cameraDistanceSlider != null)
            {
                cameraDistanceSlider.minValue = 2f;
                cameraDistanceSlider.maxValue = 50f;
                cameraDistanceSlider.value = 10f;
                cameraDistanceSlider.onValueChanged.AddListener(OnCameraDistanceChanged);
            }
            
            // Camera Height
            if (cameraHeightSlider != null)
            {
                cameraHeightSlider.minValue = 1f;
                cameraHeightSlider.maxValue = 30f;
                cameraHeightSlider.value = 5f;
                cameraHeightSlider.onValueChanged.AddListener(OnCameraHeightChanged);
            }
            
            // Camera Rotation
            if (cameraRotationSlider != null)
            {
                cameraRotationSlider.minValue = 0f;
                cameraRotationSlider.maxValue = 360f;
                cameraRotationSlider.value = 0f;
                cameraRotationSlider.onValueChanged.AddListener(OnCameraRotationChanged);
            }
            
            // Trajectory Length
            if (trajectoryLengthSlider != null)
            {
                trajectoryLengthSlider.minValue = 10;
                trajectoryLengthSlider.maxValue = 500;
                trajectoryLengthSlider.value = 100;
                trajectoryLengthSlider.onValueChanged.AddListener(OnTrajectoryLengthChanged);
            }
            
            // Sensor Ray Opacity
            if (sensorRayOpacitySlider != null)
            {
                sensorRayOpacitySlider.minValue = 0.1f;
                sensorRayOpacitySlider.maxValue = 1f;
                sensorRayOpacitySlider.value = 0.7f;
                sensorRayOpacitySlider.onValueChanged.AddListener(OnSensorRayOpacityChanged);
            }
            
            // Human Opacity
            if (humanOpacitySlider != null)
            {
                humanOpacitySlider.minValue = 0.3f;
                humanOpacitySlider.maxValue = 1f;
                humanOpacitySlider.value = 1f;
                humanOpacitySlider.onValueChanged.AddListener(OnHumanOpacityChanged);
            }
            
            // Ambient Intensity
            if (ambientIntensitySlider != null)
            {
                ambientIntensitySlider.minValue = 0f;
                ambientIntensitySlider.maxValue = 2f;
                ambientIntensitySlider.value = 1f;
                ambientIntensitySlider.onValueChanged.AddListener(OnAmbientIntensityChanged);
            }
            
            // Render Distance
            if (renderDistanceSlider != null)
            {
                renderDistanceSlider.minValue = 50f;
                renderDistanceSlider.maxValue = 500f;
                renderDistanceSlider.value = 200f;
                renderDistanceSlider.onValueChanged.AddListener(OnRenderDistanceChanged);
            }
        }
        
        private void SetupCallbacks()
        {
            if (toggleButton != null) toggleButton.onClick.AddListener(TogglePanel);
            if (applyButton != null) applyButton.onClick.AddListener(ApplyAllSettings);
            if (resetViewButton != null) resetViewButton.onClick.AddListener(ResetView);
            if (saveCurrentViewButton != null) saveCurrentViewButton.onClick.AddListener(SaveCurrentViewAsPreset);
            if (takeScreenshotButton != null) takeScreenshotButton.onClick.AddListener(TakeScreenshot);
            if (startRecordingButton != null) startRecordingButton.onClick.AddListener(StartRecording);
            if (stopRecordingButton != null) stopRecordingButton.onClick.AddListener(StopRecording);
            if (focusOnRobotButton != null) focusOnRobotButton.onClick.AddListener(FocusOnRobot);
            if (focusOnHumanButton != null) focusOnHumanButton.onClick.AddListener(FocusOnHuman);
            if (cycleViewButton != null) cycleViewButton.onClick.AddListener(CycleView);
            
            if (cameraPresetDropdown != null) cameraPresetDropdown.onValueChanged.AddListener(OnCameraPresetChanged);
            if (cameraModeDropdown != null) cameraModeDropdown.onValueChanged.AddListener(OnCameraModeChanged);
            if (orthographicToggle != null) orthographicToggle.onValueChanged.AddListener(OnOrthographicToggle);
            if (splitViewToggle != null) splitViewToggle.onValueChanged.AddListener(OnSplitViewToggle);
            if (showGridToggle != null) showGridToggle.onValueChanged.AddListener(OnShowGridToggle);
            if (wireframeModeToggle != null) wireframeModeToggle.onValueChanged.AddListener(OnWireframeModeToggle);
            if (postProcessingToggle != null) postProcessingToggle.onValueChanged.AddListener(OnPostProcessingToggle);
            if (selectTargetButton != null) selectTargetButton.onClick.AddListener(ShowTargetSelection);
            if (nextTargetButton != null) nextTargetButton.onClick.AddListener(OnNextTargetClicked);
            if (previousTargetButton != null) previousTargetButton.onClick.AddListener(OnPreviousTargetClicked);
        }
        
        private void CleanupCallbacks()
        {
            if (toggleButton != null) toggleButton.onClick.RemoveListener(TogglePanel);
            if (applyButton != null) applyButton.onClick.RemoveListener(ApplyAllSettings);
            if (resetViewButton != null) resetViewButton.onClick.RemoveListener(ResetView);
            if (saveCurrentViewButton != null) saveCurrentViewButton.onClick.RemoveListener(SaveCurrentViewAsPreset);
            if (takeScreenshotButton != null) takeScreenshotButton.onClick.RemoveListener(TakeScreenshot);
            if (startRecordingButton != null) startRecordingButton.onClick.RemoveListener(StartRecording);
            if (stopRecordingButton != null) stopRecordingButton.onClick.RemoveListener(StopRecording);
            if (focusOnRobotButton != null) focusOnRobotButton.onClick.RemoveListener(FocusOnRobot);
            if (focusOnHumanButton != null) focusOnHumanButton.onClick.RemoveListener(FocusOnHuman);
            if (cycleViewButton != null) cycleViewButton.onClick.RemoveListener(CycleView);
            if (selectTargetButton != null) selectTargetButton.onClick.RemoveListener(ShowTargetSelection);
            if (nextTargetButton != null) nextTargetButton.onClick.RemoveListener(OnNextTargetClicked);
            if (previousTargetButton != null) previousTargetButton.onClick.RemoveListener(OnPreviousTargetClicked);
        }
        
        #endregion
        
        #region UI Callbacks
        
        private void TogglePanel()
        {
            _isPanelOpen = !_isPanelOpen;
            if (visualizationPanel != null) 
                visualizationPanel.SetActive(_isPanelOpen);
            
            UpdateStatus(_isPanelOpen ? "Panneau de visualisation ouvert" : "Panneau fermé");
        }
        
        private void ApplyAllSettings()
        {
            ApplyCameraSettings();
            ApplyViewOptions();
            ApplyVisualizationOptions();
            ApplyLightingSettings();
            ApplyPerformanceSettings();
            
            UpdateStatus("Paramètres de visualisation appliqués");
            if (showDebugLogs) Debug.Log("[VizUI] Settings applied");
        }
        
        private void ApplyCameraSettings()
        {
            if (_cameraController == null) return;

            Debug.Log(Camera.main);   
            
            // Apply FOV
            if (fieldOfViewSlider != null && Camera.main != null)
                Camera.main.fieldOfView = fieldOfViewSlider.value;
            
            // Apply orthographic
            if (orthographicToggle != null && Camera.main != null)
                Camera.main.orthographic = orthographicToggle.isOn;
            
            // Apply camera mode
            if (cameraModeDropdown != null)
                _cameraController.SetCameraMode(cameraModeDropdown.value);
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
            
            // Robot visualization
            _vizManager.showRobotTrajectory = showRobotTrajectoryToggle?.isOn ?? true;
            _vizManager.showRobotPath = showRobotPathToggle?.isOn ?? true;
            _vizManager.showRobotGoal = showRobotGoalToggle?.isOn ?? true;
            _vizManager.showRobotVelocityVector = showRobotVelocityVectorToggle?.isOn ?? false;
            _vizManager.showRobotSensorRays = showRobotSensorRaysToggle?.isOn ?? true;
            
            if (trajectoryLengthSlider != null)
                _vizManager.trajectoryLength = (int)trajectoryLengthSlider.value;
            
            // Human visualization
            _vizManager.showHumanTrajectories = showHumanTrajectoriesToggle?.isOn ?? true;
            _vizManager.showHumanGoals = showHumanGoalsToggle?.isOn ?? true;
            _vizManager.showHumanVelocityVectors = showHumanVelocityVectorsToggle?.isOn ?? false;
            _vizManager.showHumanInteractionRadius = showHumanInteractionRadiusToggle?.isOn ?? true;
            _vizManager.colorHumansByState = colorHumansByStateToggle?.isOn ?? true;
            
            // Debug visualization
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
            if (shadowsToggle != null)
                QualitySettings.shadows = shadowsToggle.isOn ? ShadowQuality.All : ShadowQuality.Disable;
            
            if (ambientIntensitySlider != null)
                RenderSettings.ambientIntensity = ambientIntensitySlider.value;
            
            if (antiAliasingToggle != null)
                QualitySettings.antiAliasing = antiAliasingToggle.isOn ? 4 : 1;
        }
        
        private void ApplyPerformanceSettings()
        {
            if (cullingToggle != null && Camera.main != null)
                Camera.main.useOcclusionCulling = cullingToggle.isOn;
            
            if (renderDistanceSlider != null && Camera.main != null)
                Camera.main.farClipPlane = renderDistanceSlider.value;
        }
        
        private void ResetView()
        {
            if (_cameraController != null)
            {
                _cameraController.ResetToDefaultView();
                UpdateStatus("Vue réinitialisée");
            }
        }
        
        private void OnCameraPresetChanged(int index)
        {
            string presetName = cameraPresetDropdown.options[index].text;
            ApplyPreset(presetName);
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
            if (Camera.main != null)
                Camera.main.orthographic = isOrtho;
        }
        
        private void OnFieldOfViewChanged(float value)
        {
            if (fieldOfViewValue != null)
                fieldOfViewValue.text = $"{value:F0}°";
        }
        
        private void OnCameraDistanceChanged(float value)
        {
            if (cameraDistanceValue != null)
                cameraDistanceValue.text = $"{value:F1}m";
        }
        
        private void OnCameraHeightChanged(float value)
        {
            if (cameraHeightValue != null)
                cameraHeightValue.text = $"{value:F1}m";
        }
        
        private void OnCameraRotationChanged(float value)
        {
            if (cameraRotationValue != null)
                cameraRotationValue.text = $"{value:F0}°";
        }
        
        private void OnTrajectoryLengthChanged(float value)
        {
            if (trajectoryLengthValue != null)
                trajectoryLengthValue.text = $"{(int)value} pts";
        }
        
        private void OnSensorRayOpacityChanged(float value)
        {
            if (sensorRayOpacityValue != null)
                sensorRayOpacityValue.text = $"{value:P0}";
        }
        
        private void OnHumanOpacityChanged(float value)
        {
            if (humanOpacityValue != null)
                humanOpacityValue.text = $"{value:P0}";
        }
        
        private void OnAmbientIntensityChanged(float value)
        {
            if (ambientIntensityValue != null)
                ambientIntensityValue.text = $"{value:F1}x";
        }
        
        private void OnRenderDistanceChanged(float value)
        {
            if (renderDistanceValue != null)
                renderDistanceValue.text = $"{value:F0}m";
        }
        
        private void OnSplitViewToggle(bool isOn)
        {
            if (splitViewModeDropdown != null)
                splitViewModeDropdown.interactable = isOn;
            
            if (_cameraController != null)
                _cameraController.SetSplitView(isOn, splitViewModeDropdown?.value ?? 0);
        }
        
        private void OnShowGridToggle(bool show)
        {
            if (_vizManager != null)
                _vizManager.showGrid = show;
        }
        
        private void OnWireframeModeToggle(bool wireframe)
        {
            if (_vizManager != null)
                _vizManager.wireframeMode = wireframe;
        }
        
        private void OnPostProcessingToggle(bool enable)
        {
            // Enable/disable post-processing volume
            var postProcessVolume = FindObjectOfType<UnityEngine.Rendering.Volume>();
            if (postProcessVolume != null)
                postProcessVolume.enabled = enable;
        }
        
        private void OnCameraViewChanged()
        {
            UpdateCameraInfo();
        }

        private void ShowTargetSelection()
        {
            if (targetSelectionPopup != null)
                targetSelectionPopup.ShowPopup();
        }

        private void OnNextTargetClicked()
        {
            if (targetSelectionPopup != null)
                targetSelectionPopup.CycleNext();
        }

        private void OnPreviousTargetClicked()
        {
            if (targetSelectionPopup != null)
                targetSelectionPopup.CyclePrevious();
        }
        
        #endregion
        
        #region Screenshot & Recording
        
        private void TakeScreenshot()
        {
            string prefix = screenshotPrefixInput?.text ?? "screenshot";
            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filename = $"{prefix}_{timestamp}.png";
            
            string path = System.IO.Path.Combine(Application.dataPath, "../Screenshots", filename);
            
            // Ensure directory exists
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            
            ScreenCapture.CaptureScreenshot(path);
            UpdateStatus($"Capture d'écran sauvegardée: {filename}");
            
            if (showDebugLogs) Debug.Log($"[VizUI] Screenshot saved to: {path}");
        }
        
        private void StartRecording()
        {
            if (_isRecording) return;
            
            _isRecording = true;
            
            if (startRecordingButton != null)
                startRecordingButton.interactable = false;
            if (stopRecordingButton != null)
                stopRecordingButton.interactable = true;
            
            if (recordingStatusText != null)
                recordingStatusText.text = "● REC";
            
            // Start recording logic here
            UpdateStatus("Enregistrement démarré");
        }
        
        private void StopRecording()
        {
            if (!_isRecording) return;
            
            _isRecording = false;
            
            if (startRecordingButton != null)
                startRecordingButton.interactable = true;
            if (stopRecordingButton != null)
                stopRecordingButton.interactable = false;
            
            if (recordingStatusText != null)
                recordingStatusText.text = "";
            
            // Stop recording logic here
            UpdateStatus("Enregistrement arrêté");
        }
        
        #endregion
        
        #region Focus & Navigation
        
        private void FocusOnRobot()
        {
            if (_cameraController != null)
            {
                _cameraController.FocusOnRobot();
                UpdateStatus("Caméra centrée sur le robot");
            }
        }
        
        private void FocusOnHuman()
        {
            if (_cameraController != null && targetAgentIDInput != null)
            {
                if (int.TryParse(targetAgentIDInput.text, out int agentId))
                {
                    _cameraController.FocusOnAgent(agentId);
                    UpdateStatus($"Caméra centrée sur l'agent {agentId}");
                }
            }
        }
        
        private void CycleView()
        {
            if (_cameraController != null)
            {
                _cameraController.CycleToNextView();
                UpdateStatus("Vue suivante");
            }
        }
        
        #endregion
        
        #region Presets
        
        private void SaveCurrentViewAsPreset()
        {
            if (Camera.main == null) return;
            
            string presetName = presetNameInput?.text;
            if (string.IsNullOrEmpty(presetName))
            {
                presetName = $"Preset_{System.DateTime.Now.Ticks}";
            }
            
            var preset = new CameraPreset
            {
                position = Camera.main.transform.position,
                rotation = Camera.main.transform.rotation,
                orthographic = Camera.main.orthographic,
                orthographicSize = Camera.main.orthographicSize,
                fieldOfView = Camera.main.fieldOfView
            };
            
            _savedPresets[presetName] = preset;
            UpdatePresetDropdown();
            
            UpdateStatus($"Preset '{presetName}' sauvegardé");
            
            if (showDebugLogs) Debug.Log($"[VizUI] Saved camera preset: {presetName}");
        }
        
        private void ApplyPreset(string presetName)
        {
            if (_savedPresets.TryGetValue(presetName, out CameraPreset preset))
            {
                ApplyPreset(preset);
                UpdateStatus($"Preset '{presetName}' appliqué");
            }
            else
            {
                ApplyDefaultPreset(presetName);
            }
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
            
            Debug.Log(presetName);

            switch (presetName)
            {
                case "Top View":
                    _cameraController.SetCameraTransform(
                        new Vector3(0, 20, 0),
                        Quaternion.Euler(90, 0, 0)
                    );
                    break;
                case "Side View":
                    _cameraController.SetCameraTransform(
                        new Vector3(-20, 5, 0),
                        Quaternion.Euler(15, 90, 0)
                    );
                    break;
                case "Front View":
                    _cameraController.SetCameraTransform(
                        new Vector3(0, 5, -20),
                        Quaternion.Euler(15, 0, 0)
                    );
                    break;
                case "Isometric":
                    _cameraController.SetCameraTransform(
                        new Vector3(-15, 15, -15),
                        Quaternion.Euler(35, 45, 0)
                    );
                    break;
                case "Default View":
                default:
                    _cameraController.ResetToDefaultView();
                    break;
            }
        }
        
        private void UpdatePresetDropdown()
        {
            if (cameraPresetDropdown == null) return;
            
            var options = new List<string>(_savedPresets.Keys);
            options.Insert(0, "Default View");
            options.Insert(1, "Top View");
            options.Insert(2, "Side View");
            options.Insert(3, "Front View");
            options.Insert(4, "Isometric");
            
            cameraPresetDropdown.ClearOptions();
            cameraPresetDropdown.AddOptions(options);
        }
        
        private void LoadSavedPresets()
        {
            // Load from PlayerPrefs or file
            // This is a placeholder - implement actual loading logic
            UpdatePresetDropdown();
        }
        
        #endregion
        
        #region Helpers
        
        private void LoadSettings()
        {
            // Load saved settings from PlayerPrefs or config file
            // Placeholder implementation
            
            if (showGridToggle != null) 
                showGridToggle.isOn = PlayerPrefs.GetInt("Viz_ShowGrid", 1) == 1;
            if (showRobotTrajectoryToggle != null)
                showRobotTrajectoryToggle.isOn = PlayerPrefs.GetInt("Viz_ShowTrajectory", 1) == 1;
            if (fieldOfViewSlider != null)
                fieldOfViewSlider.value = PlayerPrefs.GetFloat("Viz_FOV", 60f);
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
                
                // Color based on performance
                if (fps < 30)
                    fpsDisplayText.color = Color.red;
                else if (fps < 60)
                    fpsDisplayText.color = Color.yellow;
                else
                    fpsDisplayText.color = Color.green;
                
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
            
            if (showDebugLogs) Debug.Log($"[VizUI] {message}");
        }
        
        private void ClearStatus()
        {
            if (statusText != null)
                statusText.text = "";
        }
        
        #endregion
    }
}