using System;
using System.IO;
using RobotSNAP.ROS;
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
        [Tooltip("Global time scale (1 = normal, 0.5 = half speed, 2 = double speed). Training runs fast - " +
                 "tens of times real time - while inference runs at 1; past a few tens of times, the machine, " +
                 "not this bound, is what limits the run.")]
        [SerializeField] private float _timeScale = 1f;
        
        [Tooltip("Fixed timestep for physics (seconds)")]
        [SerializeField] private float _fixedTimestep = 0.02f;

        [Tooltip("Cap on how much simulation one rendered frame may catch up on, in seconds of simulated " +
                 "time. This is what bounds a fast session when the frame rate falls: a frame that draws at " +
                 "one hertz while the session asks for ten times speed owes ten seconds of simulation, and " +
                 "will not do more than this one. Raised past what the machine can pay for, it is no limit " +
                 "at all - the machine is - but a frame then runs long enough to make the editor look stuck.")]
        [SerializeField] private float _maximumDeltaTime = 10f;
        
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
        
        [Tooltip("Rate every scheduled stream publishes at, in hertz. Zero - the shipped answer - means " +
                 "each stream keeps the rate it was authored with: the lidar and the odometry of one robot " +
                 "do not share a cadence, and one number for the session would flatten that.")]
        [SerializeField] private float _rosPublishFrequency = 0f;

        [Tooltip("The name of every stream the session speaks. The ten names of the contract keep their " +
                 "shipped spelling unless a reader renames them here; a client on the other side of the " +
                 "socket reads whatever is set here, so renaming a stream is a change to the interface, " +
                 "not a local preference.")]
        [SerializeField] private RosTopicNames _topics = new RosTopicNames();

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
            // The ceiling is a guard against a typo, not a speed limit: an RL session trains at tens of times
            // real time and infers at 1, and past a few tens of times the machine is the bottleneck rather
            // than this bound. The floor stays 0, which is what a caller asking for "no time" gets.
            set => _timeScale = Mathf.Clamp(value, 0f, 100f); 
        }
        
        public float FixedTimestep 
        { 
            get => _fixedTimestep; 
            set => _fixedTimestep = Mathf.Clamp(value, 0.01f, 0.1f); 
        }

        /// <summary>
        /// Most simulation one frame may catch up on, in seconds of simulated time. Left at ten by default,
        /// which is Unity's own ceiling written into this project's settings, so a session that does not ask
        /// for more keeps the behaviour it had.
        /// </summary>
        public float MaximumDeltaTime
        {
            get => _maximumDeltaTime;
            set => _maximumDeltaTime = Mathf.Clamp(value, 0.02f, 600f);
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
            // Zero is a real setting: it is what says "each stream keeps the rate it was authored with",
            // which is the behaviour the application has always had. A positive value overrides every
            // scheduled stream at once, and the ceiling is the fastest rate a client can be trusted to read.
            set => _rosPublishFrequency = Mathf.Clamp(value, 0f, 60f); 
        }

        /// <summary>
        /// The names of the streams this configuration speaks. Never null: a configuration that carries none
        /// answers with the ones the application ships with, so nothing downstream has to check.
        /// </summary>
        public RosTopicNames Topics
        {
            get => _topics ?? (_topics = new RosTopicNames());
            set => _topics = value ?? new RosTopicNames();
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
            Time.maximumDeltaTime = _maximumDeltaTime;
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
            target._maximumDeltaTime = this._maximumDeltaTime;
            target._randomSeed = this._randomSeed;
            target._enableROS = this._enableROS;
            target._rosMasterURI = this._rosMasterURI;
            target._rosPrefix = this._rosPrefix;
            target._rosPublishFrequency = this._rosPublishFrequency;
            Topics.CopyTo(target.Topics);
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
            _maximumDeltaTime = 10f;
            _randomSeed = -1;
            _enableROS = false;
            _rosMasterURI = "http://localhost:11311";
            _rosPrefix = "";
            _rosPublishFrequency = 0f;
            Topics.ResetToDefaults();
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
