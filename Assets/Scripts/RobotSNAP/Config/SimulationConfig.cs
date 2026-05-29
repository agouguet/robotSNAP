using System;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RobotSNAP.Core
{
    /// <summary>
    /// Configuration globale de l'exécution de la simulation.
    /// Ne contient aucun paramètre descriptif du contenu (ceux-ci sont dans les scénarios YAML).
    /// </summary>
    [CreateAssetMenu(fileName = "SimulationConfig", menuName = "RobotSNAP/Simulation Config", order = 1)]
    public class SimulationConfig : ScriptableObject
    {
        #region Serialized Fields - Environments

        [Header("Environments")]
        [Tooltip("Number of parallel environment instances to create")]
        [SerializeField] private int _environmentCount = 1;
        
        [Tooltip("Spacing between environment instances (meters)")]
        [SerializeField] private float _environmentSpacing = 20f;

        #endregion

        #region Serialized Fields - Scenario Loading

        [Header("Scenario Loading")]
        [Tooltip("Name of the default scenario (without .yaml extension)")]
        [SerializeField] private string _defaultScenario = "default";
        
        [Tooltip("Folder containing scenario YAML files (relative to StreamingAssets)")]
        [SerializeField] private string _scenariosFolder = "Scenarios";

        [Tooltip("If true, the simulation starts in paused state after scenario is applied")]
        [SerializeField] private bool _startPaused = true;

        #endregion

        #region Serialized Fields - Execution Control

        [Header("Execution")]
        [Tooltip("Global time scale (1 = normal, 0.5 = half speed, 2 = double speed)")]
        [SerializeField] private float _timeScale = 1f;
        
        [Tooltip("Fixed timestep for physics (seconds)")]
        [SerializeField] private float _fixedTimestep = 0.02f;
        
        [Tooltip("Random seed for reproducibility. -1 means use system time.")]
        [SerializeField] private int _randomSeed = -1;

        #endregion

        #region Serialized Fields - ROS Communication

        [Header("ROS")]
        [Tooltip("Enable ROS communication")]
        [SerializeField] private bool _enableROS = false;
        
        [Tooltip("ROS master URI")]
        [SerializeField] private string _rosMasterURI = "http://localhost:11311";
        
        [Tooltip("Prefix for ROS topics (e.g., '/robot0/')")]
        [SerializeField] private string _rosPrefix = "";
        
        [Tooltip("ROS publish frequency (Hz)")]
        [SerializeField] private float _rosPublishFrequency = 10f;

        #endregion

        #region Serialized Fields - Data Recording

        [Header("Data Recording")]
        [Tooltip("Enable data recording (metrics, trajectories, etc.)")]
        [SerializeField] private bool _recordData = false;
        
        [Tooltip("Output directory for recorded data (relative to project root)")]
        [SerializeField] private string _outputDirectory = "SimulationData";
        
        [Tooltip("Dataset root folder (relative to StreamingAssets)")]
        [SerializeField] private string _datasetPath = "Dataset";

        #endregion

        #region Serialized Fields - Performance / Debug

        [Header("Performance")]
        [Tooltip("Enable inference mode (single environment, optimized for ML)")]
        [SerializeField] private bool _inferenceMode = false;

        #endregion

        #region Properties

        public int EnvironmentCount 
        { 
            get => _environmentCount; 
            set => _environmentCount = Mathf.Max(1, value); 
        }
        
        public float EnvironmentSpacing 
        { 
            get => _environmentSpacing; 
            set => _environmentSpacing = Mathf.Max(0, value); 
        }
        
        public string DefaultScenario 
        { 
            get => _defaultScenario; 
            set => _defaultScenario = string.IsNullOrEmpty(value) ? "default" : value; 
        }
        
        public string ScenariosFolder 
        { 
            get => _scenariosFolder; 
            set => _scenariosFolder = string.IsNullOrEmpty(value) ? "Scenarios" : value; 
        }
        
        public bool StartPaused 
        { 
            get => _startPaused; 
            set => _startPaused = value; 
        }
        
        public float TimeScale 
        { 
            get => _timeScale; 
            set => _timeScale = Mathf.Clamp(value, 0f, 10f); 
        }
        
        public float FixedTimestep 
        { 
            get => _fixedTimestep; 
            set => _fixedTimestep = Mathf.Clamp(value, 0.01f, 0.1f); 
        }
        
        public int RandomSeed 
        { 
            get => _randomSeed; 
            set => _randomSeed = value; 
        }
        
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
        
        public string DatasetPath 
        { 
            get => _datasetPath; 
            set => _datasetPath = value ?? "Dataset"; 
        }
        
        public bool InferenceMode 
        { 
            get => _inferenceMode; 
            set => _inferenceMode = value; 
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Applique les réglages de temps (time scale, fixed timestep) à Unity.
        /// </summary>
        public void ApplyTimeSettings()
        {
            Time.timeScale = _timeScale;
            Time.fixedDeltaTime = _fixedTimestep;
        }
        
        /// <summary>
        /// Crée une copie profonde de cette configuration.
        /// </summary>
        public SimulationConfig Clone()
        {
            var clone = CreateInstance<SimulationConfig>();
            CopyTo(clone);
            return clone;
        }
        
        /// <summary>
        /// Copie toutes les valeurs vers une autre configuration.
        /// </summary>
        public void CopyTo(SimulationConfig target)
        {
            if (target == null) return;
            
            target._environmentCount = this._environmentCount;
            target._environmentSpacing = this._environmentSpacing;
            target._defaultScenario = this._defaultScenario;
            target._scenariosFolder = this._scenariosFolder;
            target._startPaused = this._startPaused;
            target._timeScale = this._timeScale;
            target._fixedTimestep = this._fixedTimestep;
            target._randomSeed = this._randomSeed;
            target._enableROS = this._enableROS;
            target._rosMasterURI = this._rosMasterURI;
            target._rosPrefix = this._rosPrefix;
            target._rosPublishFrequency = this._rosPublishFrequency;
            target._recordData = this._recordData;
            target._outputDirectory = this._outputDirectory;
            target._datasetPath = this._datasetPath;
            target._inferenceMode = this._inferenceMode;
        }
        
        /// <summary>
        /// Sauvegarde la configuration au format JSON.
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
        /// Charge une configuration depuis un fichier JSON.
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
        /// Remet tous les paramètres à leurs valeurs par défaut.
        /// </summary>
        public void ResetToDefaults()
        {
            _environmentCount = 1;
            _environmentSpacing = 20f;
            _defaultScenario = "default";
            _scenariosFolder = "Scenarios";
            _startPaused = true;
            _timeScale = 1f;
            _fixedTimestep = 0.02f;
            _randomSeed = -1;
            _enableROS = false;
            _rosMasterURI = "http://localhost:11311";
            _rosPrefix = "";
            _rosPublishFrequency = 10f;
            _recordData = false;
            _outputDirectory = "SimulationData";
            _datasetPath = "Dataset";
            _inferenceMode = false;
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
        
        [ContextMenu("Save to JSON")]
        private void EditorSaveToJson()
        {
            string path = EditorUtility.SaveFilePanel(
                "Save Simulation Config",
                Application.streamingAssetsPath,
                "simulation_config.json",
                "json");
            
            if (!string.IsNullOrEmpty(path))
                SaveToJson(path);
        }
#endif

        #endregion
    }
}