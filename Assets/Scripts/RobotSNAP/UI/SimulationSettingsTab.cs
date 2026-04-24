// Scripts/RobotSNAP/UI/SimulationSettingsTab.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Collections;
using RobotSNAP.Core;
using RobotSNAP.Core.Scenario;

namespace RobotSNAP.UI
{
    // Classe de données pour la sérialisation des paramètres de cet onglet
    [System.Serializable]
    public class AgentsSettings
    {
        public int environmentCount;
        public float environmentSpacing;
        public string datasetPath;
        public int minHumans;
        public int maxHumans;
        public float minAgentDistance;
        public float robotSpeed;
        public float robotSensorRange;
        public string robotBehavior;
        public float humanDefaultSpeed;
        public int humanControllerType;
        public float humanInteractionRadius;
        public float timeScale;
        public string rosPrefix;
        public bool rosEnable;
        public string defaultScenario;
    }

    public class SimulationSettingsTab : MonoBehaviour, ISettingsTab
    {
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

        [Header("Buttons")]
        public Button resetButton;
        public Button saveButton;
        public Button showWorkingConfigButton;

        [Header("Status")]
        public TextMeshProUGUI statusText;
        public bool showDebugLogs = true;

        // Private references
        private Supervisor _supervisor;
        private ScenarioManager _scenarioSupervisor;
        private Clock _clock;
        private SimulationConfig _workingConfig;
        private bool _isInitialized = false;

        private void Start()
        {
            FindReferences();
            InitializeDropdowns();
            SetupEvents();
            StartCoroutine(DelayedRefresh());
        }

        private System.Collections.IEnumerator DelayedRefresh()
        {
            yield return null;
            RefreshUI();
            SetupCallbacks();
        }

        private void OnDestroy()
        {
            CleanupEvents();
            CleanupCallbacks();
        }

        private void FindReferences()
        {
            _supervisor = Supervisor.Instance;
            _scenarioSupervisor = FindObjectOfType<ScenarioManager>();
            _clock = Clock.Instance;
        }

        private void InitializeDropdowns()
        {
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
        }

        private void SetupEvents()
        {
            if (_supervisor != null)
            {
                _supervisor.OnConfigChanged += OnSupervisorConfigChanged;
                if (!_supervisor.IsInitialized)
                    _supervisor.OnInitialized += OnSupervisorInitialized;
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
        }

        private void SetupCallbacks()
        {
            if (resetButton != null) resetButton.onClick.AddListener(ResetSimulation);
            if (saveButton != null) saveButton.onClick.AddListener(SaveCurrentConfig);
            if (applyScenarioButton != null) applyScenarioButton.onClick.AddListener(ApplySelectedScenario);
            if (scenarioDropdown != null) scenarioDropdown.onValueChanged.AddListener(OnScenarioSelected);
            if (showWorkingConfigButton != null) showWorkingConfigButton.onClick.AddListener(ShowWorkingConfig);
            if (pauseToggle != null) pauseToggle.onValueChanged.AddListener(OnPauseToggle);
            if (timeScaleSlider != null) timeScaleSlider.onValueChanged.AddListener(OnTimeScaleChanged);
            if (humanInteractionRadiusSlider != null) humanInteractionRadiusSlider.onValueChanged.AddListener(OnHumanInteractionRadiusChanged);
        }

        private void CleanupCallbacks()
        {
            if (resetButton != null) resetButton.onClick.RemoveListener(ResetSimulation);
            if (saveButton != null) saveButton.onClick.RemoveListener(SaveCurrentConfig);
            if (applyScenarioButton != null) applyScenarioButton.onClick.RemoveListener(ApplySelectedScenario);
            if (scenarioDropdown != null) scenarioDropdown.onValueChanged.RemoveListener(OnScenarioSelected);
            if (showWorkingConfigButton != null) showWorkingConfigButton.onClick.RemoveListener(ShowWorkingConfig);
            if (pauseToggle != null) pauseToggle.onValueChanged.RemoveListener(OnPauseToggle);
            if (timeScaleSlider != null) timeScaleSlider.onValueChanged.RemoveListener(OnTimeScaleChanged);
            if (humanInteractionRadiusSlider != null) humanInteractionRadiusSlider.onValueChanged.RemoveListener(OnHumanInteractionRadiusChanged);
        }

        #region Event Handlers

        private void OnSupervisorInitialized() => RefreshUI();

        private void OnSupervisorConfigChanged(SimulationConfig config)
        {
            if (showDebugLogs) Debug.Log("[SimulationTab] Supervisor config changed");
            RefreshUI();
        }

        private void OnScenarioLoaded(ScenarioData scenario) => RefreshScenarioDropdown();

        #endregion

        #region UI Refresh

        public void RefreshUI()
        {
            if (_supervisor == null) return;
            _workingConfig = _supervisor.ActiveConfig.Clone();

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

            // Dropdowns
            if (robotBehaviorDropdown != null)
            {
                int idx = GetRobotBehaviorIndex(_workingConfig.RobotBehavior);
                if (idx >= 0 && idx < robotBehaviorDropdown.options.Count)
                    robotBehaviorDropdown.value = idx;
            }
            if (humanControllerTypeDropdown != null)
            {
                int ct = _workingConfig.HumanControllerType;
                if (ct >= 0 && ct < humanControllerTypeDropdown.options.Count)
                    humanControllerTypeDropdown.value = ct;
            }

            if (datasetDropdown != null)
            {
                datasetDropdown.RefreshDatasetList();
                if (!string.IsNullOrEmpty(_workingConfig.DatasetPath))
                    datasetDropdown.SetSelectedPath(_workingConfig.DatasetPath, false);
            }

            RefreshScenarioDropdown();
            _isInitialized = true;
        }

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

        #endregion

        #region ISettingsTab implementation

        public void ApplySettings()
        {
            if (_workingConfig == null || _supervisor == null) return;

            if (datasetDropdown != null)
            {
                datasetDropdown.ApplySelection();
                string path = datasetDropdown.GetPendingPath();
                if (!string.IsNullOrEmpty(path))
                    _workingConfig.DatasetPath = path;
            }

            if (environmentCountInput != null && int.TryParse(environmentCountInput.text, out int envC))
                _workingConfig.EnvironmentCount = envC;
            if (environmentSpacingInput != null && float.TryParse(environmentSpacingInput.text, out float sp))
                _workingConfig.EnvironmentSpacing = sp;
            if (minHumansInput != null && int.TryParse(minHumansInput.text, out int minH))
                _workingConfig.MinHumans = minH;
            if (maxHumansInput != null && int.TryParse(maxHumansInput.text, out int maxH))
                _workingConfig.MaxHumans = maxH;
            if (minAgentDistanceInput != null && float.TryParse(minAgentDistanceInput.text, out float minD))
                _workingConfig.MinAgentDistance = minD;
            if (robotSpeedInput != null && float.TryParse(robotSpeedInput.text, out float rSpeed))
                _workingConfig.RobotSpeed = rSpeed;
            if (robotSensorRangeInput != null && float.TryParse(robotSensorRangeInput.text, out float rRange))
                _workingConfig.RobotSensorRange = rRange;
            if (robotBehaviorDropdown != null)
                _workingConfig.RobotBehavior = GetRobotBehaviorValue(robotBehaviorDropdown.value);
            if (humanDefaultSpeedInput != null && float.TryParse(humanDefaultSpeedInput.text, out float hSpeed))
                _workingConfig.HumanDefaultSpeed = hSpeed;
            if (humanControllerTypeDropdown != null)
                _workingConfig.HumanControllerType = humanControllerTypeDropdown.value;
            if (humanInteractionRadiusSlider != null)
                _workingConfig.HumanInteractionRadius = humanInteractionRadiusSlider.value;
            if (timeScaleSlider != null)
                _workingConfig.TimeScale = timeScaleSlider.value;

            _supervisor.UpdateConfig(_workingConfig);

            StartCoroutine(DelayedDatasetRefresh());

            UpdateStatus("Paramètres appliqués");
        }

        public object GetSettings()
        {
            var settings = new AgentsSettings();
            // Remplir à partir de l'UI actuelle
            if (environmentCountInput != null && int.TryParse(environmentCountInput.text, out int envC))
                settings.environmentCount = envC;
            if (environmentSpacingInput != null && float.TryParse(environmentSpacingInput.text, out float sp))
                settings.environmentSpacing = sp;
            if (minHumansInput != null && int.TryParse(minHumansInput.text, out int minH))
                settings.minHumans = minH;
            if (maxHumansInput != null && int.TryParse(maxHumansInput.text, out int maxH))
                settings.maxHumans = maxH;
            if (minAgentDistanceInput != null && float.TryParse(minAgentDistanceInput.text, out float minD))
                settings.minAgentDistance = minD;
            if (robotSpeedInput != null && float.TryParse(robotSpeedInput.text, out float rSpeed))
                settings.robotSpeed = rSpeed;
            if (robotSensorRangeInput != null && float.TryParse(robotSensorRangeInput.text, out float rRange))
                settings.robotSensorRange = rRange;
            if (robotBehaviorDropdown != null)
                settings.robotBehavior = GetRobotBehaviorValue(robotBehaviorDropdown.value);
            if (humanDefaultSpeedInput != null && float.TryParse(humanDefaultSpeedInput.text, out float hSpeed))
                settings.humanDefaultSpeed = hSpeed;
            if (humanControllerTypeDropdown != null)
                settings.humanControllerType = humanControllerTypeDropdown.value;
            if (humanInteractionRadiusSlider != null)
                settings.humanInteractionRadius = humanInteractionRadiusSlider.value;
            if (timeScaleSlider != null)
                settings.timeScale = timeScaleSlider.value;
            if (datasetDropdown != null)
                settings.datasetPath = datasetDropdown.GetPendingPath() ?? "";
            // ROS settings (si présents)
            // if (UIManager.Instance != null)
            // {
            //     // On peut récupérer depuis un autre onglet, mais ici on suppose que les champs ROS sont dans cet onglet ? Non, ils sont dans RosPanel.
            //     // Pour l'instant, on ne les inclut pas, mais on pourrait les récupérer via un autre mécanisme.
            // }
            return settings;
        }

        public void SetSettings(object settings)
        {
            if (!(settings is AgentsSettings s)) return;

            environmentCountInput.text = s.environmentCount.ToString();
            environmentSpacingInput.text = s.environmentSpacing.ToString();
            minHumansInput.text = s.minHumans.ToString();
            maxHumansInput.text = s.maxHumans.ToString();
            minAgentDistanceInput.text = s.minAgentDistance.ToString();
            robotSpeedInput.text = s.robotSpeed.ToString();
            robotSensorRangeInput.text = s.robotSensorRange.ToString();
            robotBehaviorDropdown.value = GetRobotBehaviorIndex(s.robotBehavior);
            humanDefaultSpeedInput.text = s.humanDefaultSpeed.ToString();
            humanControllerTypeDropdown.value = s.humanControllerType;
            humanInteractionRadiusSlider.value = s.humanInteractionRadius;
            timeScaleSlider.value = s.timeScale;
            if (datasetDropdown != null && !string.IsNullOrEmpty(s.datasetPath))
                datasetDropdown.SetSelectedPath(s.datasetPath, false);
        }

        #endregion

        #region UI Callbacks

        private IEnumerator DelayedDatasetRefresh()
        {
            yield return null;
            if (datasetDropdown != null && !string.IsNullOrEmpty(_workingConfig.DatasetPath))
                datasetDropdown.SetSelectedPath(_workingConfig.DatasetPath, true);
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
            if (_workingConfig == null)
            {
                UpdateStatus("Aucune config de travail");
                return;
            }
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
                CancelInvoke(nameof(ClearStatus));
                Invoke(nameof(ClearStatus), 3f);
            }
            if (showDebugLogs) Debug.Log($"[SimulationTab] {message}");
        }

        private void ClearStatus()
        {
            if (statusText != null && statusText.text != "Prêt")
                statusText.text = "Prêt";
        }

        #endregion
    }
}