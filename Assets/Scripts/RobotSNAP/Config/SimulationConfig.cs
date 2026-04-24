// Scripts/RobotSNAP/Core/SimulationConfig.cs
using System;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RobotSNAP.Core
{
    /// <summary>
    /// Configuration principale de la simulation
    /// Contient tous les paramètres modifiables par l'utilisateur
    /// </summary>
    [CreateAssetMenu(fileName = "NewSimulationConfig", menuName = "RobotSNAP/Simulation Config", order = 1)]
    public class SimulationConfig : ScriptableObject
    {
        #region Serialized Fields - Environment

        [Header("Environment")]
        [Tooltip("Number of parallel environments to simulate")]
        [SerializeField] private int _environmentCount = 1;
        
        [Tooltip("Spacing between environments when multiple are active")]
        [SerializeField] private float _environmentSpacing = 20f;

        [Tooltip("Path to the dataset folder (relative to StreamingAssets). Must contain subfolders with png/ and json/")]
        [SerializeField] private string _datasetPath = "Dataset";
        
        [Tooltip("Name of the default scenario to load (without extension)")]
        [SerializeField] private string _defaultScenario = "default";

        [Tooltip("Folder where scenario YAML files are stored (relative to StreamingAssets)")]
        [SerializeField] private string _scenariosFolder = "Scenarios";

        #endregion

        #region Serialized Fields - Time

        [Header("Time")]
        [Tooltip("Global time scale (1 = normal, 0.5 = half speed, 2 = double speed)")]
        [SerializeField] private float _timeScale = 1f;
        
        [Tooltip("Fixed timestep for physics (seconds)")]
        [SerializeField] private float _fixedTimestep = 0.02f;

        #endregion

        #region Serialized Fields - Spawn Settings

        [Header("Spawn Settings")]
        [Tooltip("Minimum number of humans per environment")]
        [SerializeField] private int _minHumans = 5;
        
        [Tooltip("Maximum number of humans per environment")]
        [SerializeField] private int _maxHumans = 15;
        
        [Tooltip("Minimum distance between spawned agents")]
        [SerializeField] private float _minAgentDistance = 1.5f;
        
        [Tooltip("Minimum path length for humans (meters)")]
        [SerializeField] private float _minPathLength = 3f;
        
        [Tooltip("Maximum path length for humans (meters)")]
        [SerializeField] private float _maxPathLength = 15f;

        #endregion

        #region Serialized Fields - Robot Settings

        [Header("Robot Settings")]
        [Tooltip("Default robot behavior")]
        [SerializeField] private string _robotBehavior = "normal";
        
        [Tooltip("Default robot speed (m/s)")]
        [SerializeField] private float _robotSpeed = 1.2f;
        
        [Tooltip("Robot max linear speed (m/s)")]
        [SerializeField] private float _robotMaxLinearSpeed = 2.0f;
        
        [Tooltip("Robot max angular speed (rad/s)")]
        [SerializeField] private float _robotMaxAngularSpeed = 2.0f;
        
        [Tooltip("Robot sensor range (meters)")]
        [SerializeField] private float _robotSensorRange = 5f;
        
        [Tooltip("Robot control mode: 0=Keyboard, 1=ROS, 2=Hybrid")]
        [SerializeField] private int _robotControlMode = 0;
        
        [Tooltip("Number of laser samples (36, 72, 180, 360, 720)")]
        [SerializeField] private int _robotLaserSamples = 180;

        #endregion

        #region Serialized Fields - Human Settings

        [Header("Human Settings")]
        [Tooltip("Default human speed (m/s)")]
        [SerializeField] private float _humanDefaultSpeed = 1.2f;
        
        [Tooltip("Maximum human speed (m/s)")]
        [SerializeField] private float _humanMaxSpeed = 2.0f;
        
        [Tooltip("Human controller type: 0=SFM, 1=ONNX, 2=Hybrid")]
        [SerializeField] private int _humanControllerType = 0;
        
        [Tooltip("Human interaction radius for social forces (meters)")]
        [SerializeField] private float _humanInteractionRadius = 1.5f;
        
        [Tooltip("Human personal space radius (meters)")]
        [SerializeField] private float _humanPersonalSpace = 0.8f;
        
        [Tooltip("Human assertiveness (0=timid, 1=aggressive)")]
        [SerializeField] private float _humanAssertiveness = 0.5f;
        
        [Tooltip("Human reaction time (seconds)")]
        [SerializeField] private float _humanReactionTime = 0.3f;

        #endregion

        #region Serialized Fields - ROS

        [Header("ROS")]
        [Tooltip("Enable ROS communication")]
        [SerializeField] private bool _enableROS = false;
        
        [Tooltip("ROS master URI")]
        [SerializeField] private string _rosMasterURI = "http://localhost:11311";
        
        [Tooltip("Prefix for ROS topics (for multi-environment)")]
        [SerializeField] private string _rosPrefix = "";
        
        [Tooltip("ROS publish frequency (Hz)")]
        [SerializeField] private float _rosPublishFrequency = 10f;

        #endregion

        #region Serialized Fields - Misc

        [Header("Miscellaneous")]
        [Tooltip("Enable inference mode (single environment, optimized for ML)")]
        [SerializeField] private bool _inferenceMode = false;
        
        [Tooltip("Enable data recording")]
        [SerializeField] private bool _recordData = false;
        
        [Tooltip("Output directory for recorded data")]
        [SerializeField] private string _outputDirectory = "SimulationData";

        #endregion

        #region Properties

        // Environment
        public int EnvironmentCount { get => _environmentCount; set => _environmentCount = Mathf.Max(1, value); }
        public float EnvironmentSpacing { get => _environmentSpacing; set => _environmentSpacing = Mathf.Max(0, value); }
        public string DatasetPath { get => _datasetPath; set => _datasetPath = value ?? ""; }
        public string DefaultScenario { get => _defaultScenario; set => _defaultScenario = value ?? "default"; }
        public string ScenariosFolder { get => _scenariosFolder; set => _scenariosFolder = value ?? "Scenarios"; }
        
        // Time
        public float TimeScale 
        { 
            get => _timeScale; 
            set => _timeScale = Mathf.Clamp(value, 0.1f, 10f); 
        }
        
        public float FixedTimestep 
        { 
            get => _fixedTimestep; 
            set => _fixedTimestep = Mathf.Clamp(value, 0.01f, 0.1f); 
        }
        
        // Spawn
        public int MinHumans 
        { 
            get => _minHumans; 
            set => _minHumans = Mathf.Max(0, value); 
        }
        
        public int MaxHumans 
        { 
            get => _maxHumans; 
            set => _maxHumans = Mathf.Max(_minHumans, value); 
        }
        
        public float MinAgentDistance 
        { 
            get => _minAgentDistance; 
            set => _minAgentDistance = Mathf.Max(0.5f, value); 
        }
        
        public float MinPathLength 
        { 
            get => _minPathLength; 
            set => _minPathLength = Mathf.Max(1f, value); 
        }
        
        public float MaxPathLength 
        { 
            get => _maxPathLength; 
            set => _maxPathLength = Mathf.Max(_minPathLength, value); 
        }
        
        // Robot
        public string RobotBehavior 
        { 
            get => _robotBehavior; 
            set => _robotBehavior = string.IsNullOrEmpty(value) ? "normal" : value; 
        }
        
        public float RobotSpeed 
        { 
            get => _robotSpeed; 
            set => _robotSpeed = Mathf.Clamp(value, 0.5f, 3f); 
        }
        
        public float RobotMaxLinearSpeed 
        { 
            get => _robotMaxLinearSpeed; 
            set => _robotMaxLinearSpeed = Mathf.Clamp(value, 0.5f, 5f); 
        }
        
        public float RobotMaxAngularSpeed 
        { 
            get => _robotMaxAngularSpeed; 
            set => _robotMaxAngularSpeed = Mathf.Clamp(value, 0.5f, 5f); 
        }
        
        public float RobotSensorRange 
        { 
            get => _robotSensorRange; 
            set => _robotSensorRange = Mathf.Clamp(value, 1f, 20f); 
        }
        
        public int RobotControlMode 
        { 
            get => _robotControlMode; 
            set => _robotControlMode = Mathf.Clamp(value, 0, 2); 
        }
        
        public int RobotLaserSamples 
        { 
            get => _robotLaserSamples; 
            set
            {
                int[] validSamples = { 36, 72, 180, 360, 720 };
                if (Array.Exists(validSamples, s => s == value))
                    _robotLaserSamples = value;
                else
                    _robotLaserSamples = 180;
            }
        }
        
        // Human
        public float HumanDefaultSpeed 
        { 
            get => _humanDefaultSpeed; 
            set => _humanDefaultSpeed = Mathf.Clamp(value, 0.5f, 3f); 
        }
        
        public float HumanMaxSpeed 
        { 
            get => _humanMaxSpeed; 
            set => _humanMaxSpeed = Mathf.Clamp(value, _humanDefaultSpeed, 5f); 
        }
        
        public int HumanControllerType 
        { 
            get => _humanControllerType; 
            set => _humanControllerType = Mathf.Clamp(value, 0, 2); 
        }
        
        public float HumanInteractionRadius 
        { 
            get => _humanInteractionRadius; 
            set => _humanInteractionRadius = Mathf.Clamp(value, 0.5f, 5f); 
        }
        
        public float HumanPersonalSpace 
        { 
            get => _humanPersonalSpace; 
            set => _humanPersonalSpace = Mathf.Clamp(value, 0.3f, 2f); 
        }
        
        public float HumanAssertiveness 
        { 
            get => _humanAssertiveness; 
            set => _humanAssertiveness = Mathf.Clamp(value, 0f, 1f); 
        }
        
        public float HumanReactionTime 
        { 
            get => _humanReactionTime; 
            set => _humanReactionTime = Mathf.Clamp(value, 0.1f, 1f); 
        }
        
        // ROS
        public bool EnableROS 
        { 
            get => _enableROS; 
            set => _enableROS = value; 
        }
        
        public string RosMasterURI 
        { 
            get => _rosMasterURI; 
            set => _rosMasterURI = string.IsNullOrEmpty(value) ? "http://localhost:11311" : value; 
        }
        
        public string RosPrefix 
        { 
            get => _rosPrefix; 
            set => _rosPrefix = value ?? ""; 
        }
        
        public float RosPublishFrequency 
        { 
            get => _rosPublishFrequency; 
            set => _rosPublishFrequency = Mathf.Clamp(value, 1f, 60f); 
        }
        
        // Misc
        public bool InferenceMode 
        { 
            get => _inferenceMode; 
            set => _inferenceMode = value; 
        }
        
        public bool RecordData 
        { 
            get => _recordData; 
            set => _recordData = value; 
        }
        
        public string OutputDirectory 
        { 
            get => _outputDirectory; 
            set => _outputDirectory = string.IsNullOrEmpty(value) ? "SimulationData" : value; 
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Apply time settings to Time class
        /// </summary>
        public void ApplyTimeSettings()
        {
            Time.timeScale = _timeScale;
            Time.fixedDeltaTime = _fixedTimestep;
        }
        
        /// <summary>
        /// Create a deep clone of this configuration
        /// </summary>
        public SimulationConfig Clone()
        {
            var clone = CreateInstance<SimulationConfig>();
            CopyTo(clone);
            return clone;
        }
        
        /// <summary>
        /// Copy all values from this config to another
        /// </summary>
        public void CopyTo(SimulationConfig target)
        {
            if (target == null) return;
            
            // Environment
            target._environmentCount = this._environmentCount;
            target._environmentSpacing = this._environmentSpacing;
            target._datasetPath = this._datasetPath;
            target._defaultScenario = this._defaultScenario;
            target._scenariosFolder = this._scenariosFolder;
            
            // Time
            target._timeScale = this._timeScale;
            target._fixedTimestep = this._fixedTimestep;
            
            // Spawn
            target._minHumans = this._minHumans;
            target._maxHumans = this._maxHumans;
            target._minAgentDistance = this._minAgentDistance;
            target._minPathLength = this._minPathLength;
            target._maxPathLength = this._maxPathLength;
            
            // Robot
            target._robotBehavior = this._robotBehavior;
            target._robotSpeed = this._robotSpeed;
            target._robotMaxLinearSpeed = this._robotMaxLinearSpeed;
            target._robotMaxAngularSpeed = this._robotMaxAngularSpeed;
            target._robotSensorRange = this._robotSensorRange;
            target._robotControlMode = this._robotControlMode;
            target._robotLaserSamples = this._robotLaserSamples;
            
            // Human
            target._humanDefaultSpeed = this._humanDefaultSpeed;
            target._humanMaxSpeed = this._humanMaxSpeed;
            target._humanControllerType = this._humanControllerType;
            target._humanInteractionRadius = this._humanInteractionRadius;
            target._humanPersonalSpace = this._humanPersonalSpace;
            target._humanAssertiveness = this._humanAssertiveness;
            target._humanReactionTime = this._humanReactionTime;
            
            // Data
            target._datasetPath = this._datasetPath;
            target._defaultScenario = this._defaultScenario;
            
            // ROS
            target._enableROS = this._enableROS;
            target._rosMasterURI = this._rosMasterURI;
            target._rosPrefix = this._rosPrefix;
            target._rosPublishFrequency = this._rosPublishFrequency;
            
            // Misc
            target._inferenceMode = this._inferenceMode;
            target._recordData = this._recordData;
            target._outputDirectory = this._outputDirectory;
        }
        
        /// <summary>
        /// Save configuration to JSON file
        /// </summary>
        public void SaveToJson(string filePath)
        {
            try
            {
                string json = JsonUtility.ToJson(this, true);
                File.WriteAllText(filePath, json);
                Debug.Log($"[SimulationConfig] Saved to {filePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[SimulationConfig] Failed to save: {e.Message}");
            }
        }
        
        /// <summary>
        /// Load configuration from JSON file
        /// </summary>
        public static SimulationConfig LoadFromJson(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Debug.LogError($"[SimulationConfig] File not found: {filePath}");
                    return null;
                }
                
                string json = File.ReadAllText(filePath);
                var config = CreateInstance<SimulationConfig>();
                JsonUtility.FromJsonOverwrite(json, config);
                
                Debug.Log($"[SimulationConfig] Loaded from {filePath}");
                return config;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SimulationConfig] Failed to load: {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Reset to default values
        /// </summary>
        public void ResetToDefaults()
        {
            _environmentCount = 1;
            _environmentSpacing = 20f;
            _datasetPath = "Dataset";
            _defaultScenario = "default";
            _scenariosFolder = "Scenarios";
            _timeScale = 1f;
            _fixedTimestep = 0.02f;
            _minHumans = 5;
            _maxHumans = 15;
            _minAgentDistance = 1.5f;
            _minPathLength = 3f;
            _maxPathLength = 15f;
            _robotBehavior = "normal";
            _robotSpeed = 1.2f;
            _robotMaxLinearSpeed = 2.0f;
            _robotMaxAngularSpeed = 2.0f;
            _robotSensorRange = 5f;
            _robotControlMode = 0;
            _robotLaserSamples = 180;
            _humanDefaultSpeed = 1.2f;
            _humanMaxSpeed = 2.0f;
            _humanControllerType = 0;
            _humanInteractionRadius = 1.5f;
            _humanPersonalSpace = 0.8f;
            _humanAssertiveness = 0.5f;
            _humanReactionTime = 0.3f;
            _datasetPath = "";
            _defaultScenario = "default";
            _enableROS = false;
            _rosMasterURI = "http://localhost:11311";
            _rosPrefix = "";
            _rosPublishFrequency = 10f;
            _inferenceMode = false;
            _recordData = false;
            _outputDirectory = "SimulationData";
        }
        
        /// <summary>
        /// Get a formatted string representation
        /// </summary>
        public string GetFormattedString()
        {
            return $"Simulation Config:\n" +
                   $"  Environments: {_environmentCount} (spacing: {_environmentSpacing})\n" +
                   $"  Time Scale: {_timeScale}\n" +
                   $"  Humans: {_minHumans}-{_maxHumans} (speed: {_humanDefaultSpeed}m/s)\n" +
                   $"  Human Controller: {GetHumanControllerName(_humanControllerType)}\n" +
                   $"  Human Interaction Radius: {_humanInteractionRadius}m\n" +
                   $"  Robot: {_robotBehavior} @ {_robotSpeed}m/s\n" +
                   $"  Robot Control: {GetRobotControlModeName(_robotControlMode)}\n" +
                   $"  Robot Laser: {_robotLaserSamples} samples\n" +
                   $"  Dataset: {_datasetPath}\n" +
                   $"  Default Scenario: {_defaultScenario}\n" +
                   $"  Inference Mode: {_inferenceMode}\n" +
                   $"  ROS: {(_enableROS ? _rosMasterURI : "disabled")}";
        }
        
        private string GetHumanControllerName(int type)
        {
            switch (type)
            {
                case 0: return "SFM";
                case 1: return "ONNX";
                case 2: return "Hybrid";
                default: return "Unknown";
            }
        }
        
        private string GetRobotControlModeName(int mode)
        {
            switch (mode)
            {
                case 0: return "Keyboard";
                case 1: return "ROS";
                case 2: return "Hybrid";
                default: return "Unknown";
            }
        }
        
        #endregion

        #region Editor Utilities

        #if UNITY_EDITOR
        
        [ContextMenu("Reset to Defaults")]
        private void EditorResetToDefaults()
        {
            ResetToDefaults();
            EditorUtility.SetDirty(this);
            Debug.Log("[SimulationConfig] Reset to defaults");
        }
        
        [ContextMenu("Copy to Clipboard")]
        private void EditorCopyToClipboard()
        {
            GUIUtility.systemCopyBuffer = GetFormattedString();
            Debug.Log("[SimulationConfig] Copied to clipboard");
        }
        
        [ContextMenu("Save to JSON")]
        private void EditorSaveToJson()
        {
            string path = EditorUtility.SaveFilePanel(
                "Save Simulation Config",
                Application.streamingAssetsPath,
                "simulation_config.json",
                "json");
            
            if (!string.IsNullOrEmpty(path))
            {
                SaveToJson(path);
            }
        }
        
        #endif
        
        #endregion
    }
}