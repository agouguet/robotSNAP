using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using RobotSNAP.Core;

namespace RobotSNAP.UI
{
    public class SimulationSettingsUI : MonoBehaviour
    {
        [Header("References")]
        public GameObject settingsPanel;
        public Button toggleButton;
        public Button applyButton;
        public Button resetButton;
        public Button saveButton;
        
        [Header("Environment Settings")]
        public TMP_InputField environmentCountInput;
        public TMP_InputField environmentSpacingInput;
        public DatasetDropdown datasetDropdown;
        
        [Header("Spawn Settings")]
        public TMP_InputField minHumansInput;
        public TMP_InputField maxHumansInput;
        public TMP_InputField minAgentDistanceInput;
        
        [Header("Robot Settings")]
        public TMP_InputField robotMaxLinearSpeedInput;
        public TMP_InputField robotMaxAngularSpeedInput;
        public TMP_Dropdown robotControlModeDropdown;
        public TMP_Dropdown robotLaserSampleDropdown;
        
        [Header("Human Settings")]
        public TMP_InputField humanDefaultSpeedInput;
        public TMP_InputField humanMaxSpeedInput;
        public TMP_Dropdown humanControllerTypeDropdown;
        public Slider humanInteractionRadiusSlider;
        public TextMeshProUGUI humanInteractionRadiusValue;
        
        [Header("Simulation Settings")]
        public Slider timeScaleSlider;
        public TextMeshProUGUI timeScaleValue;
        public Toggle pauseToggle;
        public Toggle inferenceModeToggle;
        
        [Header("ROS Settings")]
        public TMP_InputField rosPrefixInput;
        public Toggle rosPublishToggle;
        public TMP_InputField rosPublishFrequencyInput;
        
        [Header("Debug")]
        public TextMeshProUGUI statusText;
        public bool showDebugLogs = true;
        
        private Supervisor _supervisor;
        private Clock _clock;
        private SimulationConfig _workingConfig;
        private bool _isPanelOpen = false;
        
        #region Unity Lifecycle
        
        private void Start()
        {
            FindReferences();
            InitializeUI();
            SetupSupervisorEvents();
            LoadWorkingConfigFromSupervisor();
            LoadUIFromConfig();
            SetupCallbacks();
            
            if (settingsPanel != null)
                settingsPanel.SetActive(false);
        }
        
        private void OnDestroy()
        {
            if (_supervisor != null)
            {
                _supervisor.OnConfigChanged -= OnSupervisorConfigChanged;
                _supervisor.OnConfigLoaded -= OnSupervisorConfigLoaded;
            }
            
            // Cleanup callbacks
            if (applyButton != null) applyButton.onClick.RemoveListener(ApplyAllSettings);
            if (resetButton != null) resetButton.onClick.RemoveListener(ResetSimulation);
            if (saveButton != null) saveButton.onClick.RemoveListener(SaveCurrentConfig);
            if (toggleButton != null) toggleButton.onClick.RemoveListener(TogglePanel);
            if (pauseToggle != null) pauseToggle.onValueChanged.RemoveListener(OnPauseToggle);
            if (inferenceModeToggle != null) inferenceModeToggle.onValueChanged.RemoveListener(OnInferenceModeChanged);
        }
        
        #endregion
        
        #region Initialization
        
        private void FindReferences()
        {
            _supervisor = Supervisor.Instance;
            _clock = Clock.Instance;
        }
        
        private void SetupSupervisorEvents()
        {
            if (_supervisor != null)
            {
                _supervisor.OnConfigChanged += OnSupervisorConfigChanged;
                _supervisor.OnConfigLoaded += OnSupervisorConfigLoaded;
            }
        }
        
        private void OnSupervisorConfigChanged()
        {
            if (showDebugLogs) Debug.Log("[UI] Supervisor config changed, updating UI");
            LoadWorkingConfigFromSupervisor();
            LoadUIFromConfig();
        }
        
        private void OnSupervisorConfigLoaded()
        {
            if (showDebugLogs) Debug.Log("[UI] Supervisor config loaded, updating UI");
            LoadWorkingConfigFromSupervisor();
            LoadUIFromConfig();
            UpdateStatus("Configuration chargée");
        }
        
        private void LoadWorkingConfigFromSupervisor()
        {
            if (_supervisor != null && _supervisor.Config != null)
            {
                _workingConfig = _supervisor.Config.Clone();
                if (showDebugLogs) Debug.Log("[UI] Working config loaded from supervisor");
            }
            else
            {
                _workingConfig = ScriptableObject.CreateInstance<SimulationConfig>();
                if (showDebugLogs) Debug.Log("[UI] Created new working config");
            }
        }
        
        private void InitializeUI()
        {
            InitializeDropdowns();
            InitializeSliders();
        }
        
        private void InitializeDropdowns()
        {
            if (robotControlModeDropdown != null)
            {
                robotControlModeDropdown.ClearOptions();
                robotControlModeDropdown.AddOptions(new List<string> { "Keyboard", "ROS", "Hybrid" });
            }
            
            if (robotLaserSampleDropdown != null)
            {
                robotLaserSampleDropdown.ClearOptions();
                robotLaserSampleDropdown.AddOptions(new List<string> { "36", "72", "180", "360", "720" });
            }
            
            if (humanControllerTypeDropdown != null)
            {
                humanControllerTypeDropdown.ClearOptions();
                humanControllerTypeDropdown.AddOptions(new List<string> { "SFM", "ONNX", "Hybrid" });
            }
            
            if (datasetDropdown != null)
            {
                datasetDropdown.RefreshDatasetList();
            }
        }
        
        private void InitializeSliders()
        {
            if (humanInteractionRadiusSlider != null)
            {
                humanInteractionRadiusSlider.minValue = 1f;
                humanInteractionRadiusSlider.maxValue = 10f;
                humanInteractionRadiusSlider.onValueChanged.AddListener(OnHumanInteractionRadiusChanged);
            }
            
            if (timeScaleSlider != null)
            {
                timeScaleSlider.minValue = 0.1f;
                timeScaleSlider.maxValue = 5f;
                timeScaleSlider.onValueChanged.AddListener(OnTimeScaleChanged);
            }
        }
        
        private void LoadUIFromConfig()
        {
            if (_workingConfig == null) return;
            
            // Environment
            if (environmentCountInput != null)
                environmentCountInput.text = _workingConfig.environmentCount.ToString();
            if (environmentSpacingInput != null)
                environmentSpacingInput.text = _workingConfig.environmentSpacing.ToString();
            
            // Spawn
            if (minHumansInput != null)
                minHumansInput.text = _workingConfig.minHumans.ToString();
            if (maxHumansInput != null)
                maxHumansInput.text = _workingConfig.maxHumans.ToString();
            if (minAgentDistanceInput != null)
                minAgentDistanceInput.text = _workingConfig.minAgentDistance.ToString();
            
            // Robot
            if (robotMaxLinearSpeedInput != null)
                robotMaxLinearSpeedInput.text = _workingConfig.robotMaxLinearSpeed.ToString();
            if (robotMaxAngularSpeedInput != null)
                robotMaxAngularSpeedInput.text = _workingConfig.robotMaxAngularSpeed.ToString();
            if (robotControlModeDropdown != null)
                robotControlModeDropdown.value = _workingConfig.robotControlMode;
            if (robotLaserSampleDropdown != null)
                robotLaserSampleDropdown.value = GetLaserSampleIndex(_workingConfig.robotLaserSamples);
            
            // Human
            if (humanDefaultSpeedInput != null)
                humanDefaultSpeedInput.text = _workingConfig.humanDefaultSpeed.ToString();
            if (humanMaxSpeedInput != null)
                humanMaxSpeedInput.text = _workingConfig.humanMaxSpeed.ToString();
            if (humanControllerTypeDropdown != null)
                humanControllerTypeDropdown.value = _workingConfig.humanControllerType;
            if (humanInteractionRadiusSlider != null)
                humanInteractionRadiusSlider.value = _workingConfig.humanInteractionRadius;
            
            // Simulation
            if (timeScaleSlider != null)
                timeScaleSlider.value = _workingConfig.timeScale;
            if (inferenceModeToggle != null)
                inferenceModeToggle.isOn = _workingConfig.inferenceMode;
            
            // ROS
            if (rosPrefixInput != null)
                rosPrefixInput.text = _workingConfig.rosPrefix;
            if (rosPublishFrequencyInput != null)
                rosPublishFrequencyInput.text = _workingConfig.rosPublishFrequency.ToString();
            if (rosPublishToggle != null)
                rosPublishToggle.isOn = _workingConfig.rosEnabled;
            
            if (showDebugLogs) Debug.Log("[UI] UI loaded from config");
        }
        
        private void SetupCallbacks()
        {
            if (toggleButton != null) toggleButton.onClick.AddListener(TogglePanel);
            if (applyButton != null) applyButton.onClick.AddListener(ApplyAllSettings);
            if (resetButton != null) resetButton.onClick.AddListener(ResetSimulation);
            if (saveButton != null) saveButton.onClick.AddListener(SaveCurrentConfig);
            if (pauseToggle != null) pauseToggle.onValueChanged.AddListener(OnPauseToggle);
            if (inferenceModeToggle != null) inferenceModeToggle.onValueChanged.AddListener(OnInferenceModeChanged);
        }
        
        #endregion
        
        #region UI Callbacks
        
        private void TogglePanel()
        {
            _isPanelOpen = !_isPanelOpen;
            if (settingsPanel != null) settingsPanel.SetActive(_isPanelOpen);
        }
        
        private void ApplyAllSettings()
        {
            if (_workingConfig == null || _supervisor == null) return;
            
            // Environment
            if (environmentCountInput != null && int.TryParse(environmentCountInput.text, out int envCount))
                _workingConfig.environmentCount = envCount;
            if (environmentSpacingInput != null && float.TryParse(environmentSpacingInput.text, out float spacing))
                _workingConfig.environmentSpacing = spacing;
            
            // Spawn
            if (minHumansInput != null && int.TryParse(minHumansInput.text, out int minH))
                _workingConfig.minHumans = minH;
            if (maxHumansInput != null && int.TryParse(maxHumansInput.text, out int maxH))
                _workingConfig.maxHumans = maxH;
            if (minAgentDistanceInput != null && float.TryParse(minAgentDistanceInput.text, out float minDist))
                _workingConfig.minAgentDistance = minDist;
            
            // Robot
            if (robotMaxLinearSpeedInput != null && float.TryParse(robotMaxLinearSpeedInput.text, out float linSpeed))
                _workingConfig.robotMaxLinearSpeed = linSpeed;
            if (robotMaxAngularSpeedInput != null && float.TryParse(robotMaxAngularSpeedInput.text, out float angSpeed))
                _workingConfig.robotMaxAngularSpeed = angSpeed;
            if (robotControlModeDropdown != null)
                _workingConfig.robotControlMode = robotControlModeDropdown.value;
            if (robotLaserSampleDropdown != null)
                _workingConfig.robotLaserSamples = GetLaserSampleValue(robotLaserSampleDropdown.value);
            
            // Human
            if (humanDefaultSpeedInput != null && float.TryParse(humanDefaultSpeedInput.text, out float hSpeed))
                _workingConfig.humanDefaultSpeed = hSpeed;
            if (humanMaxSpeedInput != null && float.TryParse(humanMaxSpeedInput.text, out float hMaxSpeed))
                _workingConfig.humanMaxSpeed = hMaxSpeed;
            if (humanControllerTypeDropdown != null)
                _workingConfig.humanControllerType = humanControllerTypeDropdown.value;
            if (humanInteractionRadiusSlider != null)
                _workingConfig.humanInteractionRadius = humanInteractionRadiusSlider.value;
            
            // Simulation
            if (timeScaleSlider != null)
                _workingConfig.timeScale = timeScaleSlider.value;
            if (inferenceModeToggle != null)
                _workingConfig.inferenceMode = inferenceModeToggle.isOn;
            
            // ROS
            if (rosPrefixInput != null)
                _workingConfig.rosPrefix = rosPrefixInput.text;
            if (rosPublishFrequencyInput != null && float.TryParse(rosPublishFrequencyInput.text, out float freq))
                _workingConfig.rosPublishFrequency = freq;
            if (rosPublishToggle != null)
                _workingConfig.rosEnabled = rosPublishToggle.isOn;
            
            // Dataset
            if (datasetDropdown != null)
            {
                string datasetPath = datasetDropdown.GetPendingPath();
                if (!string.IsNullOrEmpty(datasetPath))
                    _workingConfig.datasetPath = datasetPath;
            }
            
            // Apply to supervisor
            _supervisor.UpdateConfig(_workingConfig);
            
            UpdateStatus("Paramètres appliqués");
            if (showDebugLogs) Debug.Log("[UI] Settings applied");
        }
        
        private void ResetSimulation()
        {
            if (_supervisor != null)
            {
                _supervisor.ResetAllEnvironments();
                UpdateStatus("Simulation réinitialisée");
            }
        }
        
        private void SaveCurrentConfig()
        {
            if (_supervisor != null)
            {
                _supervisor.SaveDefaultConfig();
                UpdateStatus("Configuration sauvegardée");
                if (showDebugLogs) Debug.Log("[UI] Config saved");
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
            if (_workingConfig != null) _workingConfig.timeScale = value;
        }
        
        private void OnHumanInteractionRadiusChanged(float value)
        {
            if (humanInteractionRadiusValue != null) humanInteractionRadiusValue.text = $"{value:F1}m";
            if (_workingConfig != null) _workingConfig.humanInteractionRadius = value;
        }
        
        private void OnInferenceModeChanged(bool isOn)
        {
            if (_supervisor != null) _supervisor.OnInferenceModeChanged(isOn);
            if (_workingConfig != null) _workingConfig.inferenceMode = isOn;
        }
        
        #endregion
        
        #region Helpers
        
        private int GetLaserSampleIndex(int samples)
        {
            switch (samples)
            {
                case 36: return 0;
                case 72: return 1;
                case 180: return 2;
                case 360: return 3;
                case 720: return 4;
                default: return 2;
            }
        }
        
        private int GetLaserSampleValue(int index)
        {
            switch (index)
            {
                case 0: return 36;
                case 1: return 72;
                case 2: return 180;
                case 3: return 360;
                case 4: return 720;
                default: return 180;
            }
        }
        
        private void UpdateStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
                Invoke(nameof(ClearStatus), 2f);
            }
        }
        
        private void ClearStatus()
        {
            if (statusText != null && statusText.text != "Prêt")
                statusText.text = "Prêt";
        }
        
        #endregion
    }
}