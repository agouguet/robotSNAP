// Scripts/RobotSNAP/UI/SimulationSettingsUI.cs
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
        public Toggle inferenceModeToggle;
        
        [Header("ROS Settings")]
        public TMP_InputField rosPrefixInput;
        public Toggle rosEnableToggle;
        
        [Header("Scenario Settings")]
        public TMP_Dropdown scenarioDropdown;
        public Button applyScenarioButton;
        
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
            }
            
            // Cleanup callbacks
            if (applyButton != null) applyButton.onClick.RemoveListener(ApplyAllSettings);
            if (resetButton != null) resetButton.onClick.RemoveListener(ResetSimulation);
            if (saveButton != null) saveButton.onClick.RemoveListener(SaveCurrentConfig);
            if (toggleButton != null) toggleButton.onClick.RemoveListener(TogglePanel);
            if (pauseToggle != null) pauseToggle.onValueChanged.RemoveListener(OnPauseToggle);
            if (inferenceModeToggle != null) inferenceModeToggle.onValueChanged.RemoveListener(OnInferenceModeChanged);
            if (applyScenarioButton != null) applyScenarioButton.onClick.RemoveListener(ApplySelectedScenario);
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
            }
        }
        
        private void OnSupervisorConfigChanged(SimulationConfig config)
        {
            if (showDebugLogs) Debug.Log("[UI] Supervisor config changed, updating UI");
            LoadWorkingConfigFromSupervisor();
            LoadUIFromConfig();
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
        
        private void InitializeUI()
        {
            InitializeDropdowns();
            InitializeSliders();
            InitializeScenarioDropdown();
        }
        
        private void InitializeDropdowns()
        {
            // Robot behavior dropdown
            if (robotBehaviorDropdown != null)
            {
                robotBehaviorDropdown.ClearOptions();
                robotBehaviorDropdown.AddOptions(new List<string> { "normal", "cautious", "assertive", "socially_aware" });
            }
            
            // Human controller type dropdown
            if (humanControllerTypeDropdown != null)
            {
                humanControllerTypeDropdown.ClearOptions();
                humanControllerTypeDropdown.AddOptions(new List<string> { "SFM", "ONNX", "Hybrid" });
            }
            
            // Dataset dropdown
            if (datasetDropdown != null)
            {
                datasetDropdown.RefreshDatasetList();
            }
        }
        
        private void InitializeSliders()
        {
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
        }
        
        private void InitializeScenarioDropdown()
        {
            if (scenarioDropdown != null && _supervisor != null)
            {
                RefreshScenarioDropdown();
            }
        }
        
        private void RefreshScenarioDropdown()
        {
            if (scenarioDropdown == null || _supervisor == null) return;
            
            var scenarios = _supervisor.GetAvailableScenarios();
            scenarioDropdown.ClearOptions();
            scenarioDropdown.AddOptions(new List<string>(scenarios));
            
            string currentScenario = _supervisor.CurrentScenarioId;
            int currentIndex = scenarios.IndexOf(currentScenario);
            if (currentIndex >= 0)
                scenarioDropdown.value = currentIndex;
        }
        
        private void LoadUIFromConfig()
        {
            if (_workingConfig == null) return;
            
            // Environment
            if (environmentCountInput != null)
                environmentCountInput.text = _workingConfig.EnvironmentCount.ToString();
            if (environmentSpacingInput != null)
                environmentSpacingInput.text = _workingConfig.EnvironmentSpacing.ToString();
            
            // Spawn
            if (minHumansInput != null)
                minHumansInput.text = _workingConfig.MinHumans.ToString();
            if (maxHumansInput != null)
                maxHumansInput.text = _workingConfig.MaxHumans.ToString();
            if (minAgentDistanceInput != null)
                minAgentDistanceInput.text = _workingConfig.MinAgentDistance.ToString();
            
            // Robot
            if (robotSpeedInput != null)
                robotSpeedInput.text = _workingConfig.RobotSpeed.ToString();
            if (robotSensorRangeInput != null)
                robotSensorRangeInput.text = _workingConfig.RobotSensorRange.ToString();
            if (robotBehaviorDropdown != null)
                robotBehaviorDropdown.value = GetRobotBehaviorIndex(_workingConfig.RobotBehavior);
            
            // Human
            if (humanDefaultSpeedInput != null)
                humanDefaultSpeedInput.text = _workingConfig.HumanDefaultSpeed.ToString();
            if (humanControllerTypeDropdown != null)
                humanControllerTypeDropdown.value = _workingConfig.HumanControllerType;
            if (humanInteractionRadiusSlider != null)
                humanInteractionRadiusSlider.value = _workingConfig.HumanInteractionRadius;
            
            // Simulation
            if (timeScaleSlider != null)
                timeScaleSlider.value = _workingConfig.TimeScale;
            if (inferenceModeToggle != null)
                inferenceModeToggle.isOn = _workingConfig.InferenceMode;
            
            // ROS
            if (rosPrefixInput != null)
                rosPrefixInput.text = _workingConfig.RosPrefix;
            if (rosEnableToggle != null)
                rosEnableToggle.isOn = _workingConfig.EnableROS;
            
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
            if (applyScenarioButton != null) applyScenarioButton.onClick.AddListener(ApplySelectedScenario);
            if (scenarioDropdown != null) scenarioDropdown.onValueChanged.AddListener(OnScenarioSelected);
        }
        
        #endregion
        
        #region UI Callbacks
        
        private void TogglePanel()
        {
            _isPanelOpen = !_isPanelOpen;
            if (settingsPanel != null) settingsPanel.SetActive(_isPanelOpen);
            
            // Refresh dropdowns when opening
            if (_isPanelOpen)
            {
                RefreshScenarioDropdown();
                if (datasetDropdown != null) datasetDropdown.RefreshDatasetList();
            }
        }
        
        private void ApplyAllSettings()
        {
            if (_workingConfig == null || _supervisor == null) return;
            
            // Environment
            if (environmentCountInput != null && int.TryParse(environmentCountInput.text, out int envCount))
                _workingConfig.EnvironmentCount = envCount;
            if (environmentSpacingInput != null && float.TryParse(environmentSpacingInput.text, out float spacing))
                _workingConfig.EnvironmentSpacing = spacing;
            
            // Spawn
            if (minHumansInput != null && int.TryParse(minHumansInput.text, out int minH))
                _workingConfig.MinHumans = minH;
            if (maxHumansInput != null && int.TryParse(maxHumansInput.text, out int maxH))
                _workingConfig.MaxHumans = maxH;
            if (minAgentDistanceInput != null && float.TryParse(minAgentDistanceInput.text, out float minDist))
                _workingConfig.MinAgentDistance = minDist;
            
            // Robot
            if (robotSpeedInput != null && float.TryParse(robotSpeedInput.text, out float speed))
                _workingConfig.RobotSpeed = speed;
            if (robotSensorRangeInput != null && float.TryParse(robotSensorRangeInput.text, out float range))
                _workingConfig.RobotSensorRange = range;
            if (robotBehaviorDropdown != null)
                _workingConfig.RobotBehavior = GetRobotBehaviorValue(robotBehaviorDropdown.value);
            
            // Human
            if (humanDefaultSpeedInput != null && float.TryParse(humanDefaultSpeedInput.text, out float hSpeed))
                _workingConfig.HumanDefaultSpeed = hSpeed;
            if (humanControllerTypeDropdown != null)
                _workingConfig.HumanControllerType = humanControllerTypeDropdown.value;
            if (humanInteractionRadiusSlider != null)
                _workingConfig.HumanInteractionRadius = humanInteractionRadiusSlider.value;
            
            // Simulation
            if (timeScaleSlider != null)
                _workingConfig.TimeScale = timeScaleSlider.value;
            if (inferenceModeToggle != null)
                _workingConfig.InferenceMode = inferenceModeToggle.isOn;
            
            // ROS
            if (rosPrefixInput != null)
                _workingConfig.RosPrefix = rosPrefixInput.text;
            if (rosEnableToggle != null)
                _workingConfig.EnableROS = rosEnableToggle.isOn;
            
            // Dataset
            if (datasetDropdown != null)
            {
                string datasetPath = datasetDropdown.GetPendingPath(); // ou GetSelectedPath selon ce qui existe
                if (!string.IsNullOrEmpty(datasetPath))
                    _workingConfig.DatasetPath = datasetPath;
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
            if (_workingConfig != null) _workingConfig.TimeScale = value;
        }
        
        private void OnHumanInteractionRadiusChanged(float value)
        {
            if (humanInteractionRadiusValue != null) humanInteractionRadiusValue.text = $"{value:F1}m";
            if (_workingConfig != null) _workingConfig.HumanInteractionRadius = value;
        }
        
        private void OnInferenceModeChanged(bool isOn)
        {
            if (_supervisor != null) _supervisor.OnInferenceModeToggled(isOn);
            if (_workingConfig != null) _workingConfig.InferenceMode = isOn;
        }
        
        private void OnScenarioSelected(int index)
        {
            if (scenarioDropdown != null && _supervisor != null)
            {
                var scenarios = _supervisor.GetAvailableScenarios();
                // Convertir IReadOnlyList en List pour utiliser IndexOf
                var scenariosList = new List<string>(scenarios);
                if (index >= 0 && index < scenariosList.Count)
                {
                    string scenarioName = scenariosList[index];
                    UpdateStatus($"Scénario sélectionné: {scenarioName}");
                }
            }
        }
        
        private void ApplySelectedScenario()
        {
            if (scenarioDropdown == null || _supervisor == null) return;
            
            var scenarios = _supervisor.GetAvailableScenarios();
            var scenariosList = new List<string>(scenarios);
            
            if (scenarioDropdown.value >= 0 && scenarioDropdown.value < scenariosList.Count)
            {
                string scenarioName = scenariosList[scenarioDropdown.value];
                _supervisor.LoadScenario(scenarioName);
                _supervisor.ApplyCurrentScenarioToAll();  // Utilise la nouvelle méthode
                UpdateStatus($"Scénario appliqué: {scenarioName}");
                if (showDebugLogs) Debug.Log($"[UI] Scenario applied: {scenarioName}");
            }
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