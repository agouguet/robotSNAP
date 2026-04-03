using UnityEngine;
using System.Collections.Generic;
using System.IO;
using RobotSNAP.Core;
using RobotSNAP.Environment;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RobotSNAP
{
    /// <summary>
    /// Main supervisor that manages multiple environments, UI, and simulation settings.
    /// Orchestrates environment creation, destruction, and configuration.
    /// </summary>
    [ExecuteAlways]
    public class Supervisor : MonoBehaviour
    {
        private static Supervisor _instance;
        public static Supervisor Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<Supervisor>();
                }
                return _instance;
            }
        }

        [Header("Configuration")]
        [SerializeField] private SimulationConfig defaultConfig;
        [SerializeField] private GameObject environmentPrefab;
        
        [Header("View Settings")]
        [SerializeField] private GameObject soloView;
        [SerializeField] private GameObject multipleView;
        
        [Header("References")]
        [SerializeField] private Clock clock;

        [Header("Config Persistence")]
        [SerializeField] private bool autoSaveOnQuit = false;
        [SerializeField] private string configSavePath = "Assets/Configs/SavedConfig.asset";
        [SerializeField] private string configsFolderPath = "Assets/Configs/";
        
        [Header("Debug")]
        [SerializeField] private bool logEnvironmentEvents = true;
        
        // State
        private SimulationConfig _runtimeConfig; // Copie de travail utilisée en jeu
        private List<GameObject> _environmentInstances = new List<GameObject>();
        private List<GameManager> _gameManagers = new List<GameManager>();
        private bool _isQuitting;
        
        // Properties
        public SimulationConfig Config => Application.isPlaying ? _runtimeConfig : defaultConfig;
        public bool InferenceMode { get; private set; }
        public int EnvironmentCount => _environmentInstances.Count;
        public IReadOnlyList<GameObject> Environments => _environmentInstances;
        public IReadOnlyList<GameManager> GameManagers => _gameManagers;
        public string ConfigsFolderPath => configsFolderPath;
        
        // Events for UI synchronization
        public event System.Action OnConfigChanged;
        public event System.Action OnConfigLoaded;
        public event System.Action OnEnvironmentsChanged;
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            
            // Create default config if needed
            if (defaultConfig == null)
            {
                defaultConfig = ScriptableObject.CreateInstance<SimulationConfig>();
                defaultConfig.name = "DefaultConfig";
                Debug.Log("[Supervisor] Created default SimulationConfig");
            }
            
            // Ensure configs folder exists
            EnsureConfigsFolderExists();
            
            // Setup clock
            if (clock == null)
            {
                clock = FindObjectOfType<Clock>();
                if (clock == null)
                {
                    var clockGO = new GameObject("Clock");
                    clock = clockGO.AddComponent<Clock>();
                }
            }
        }
        
        private void Start()
        {
            if (Application.isPlaying)
            {
                // Créer une copie de travail pour les modifications en jeu
                _runtimeConfig = defaultConfig.Clone();
                _runtimeConfig.name = "RuntimeConfig";
                
                // Appliquer la config
                ApplyConfigToAll();
                CreateAllEnvironments();
            }
            
            // Notifier que la config est chargée
            OnConfigLoaded?.Invoke();
            
            if (logEnvironmentEvents)
            {
                Debug.Log($"[Supervisor] Started with time scale: {(Config != null ? Config.timeScale : 1f)}");
            }
        }
        
        private void Update()
        {
            if (Application.isPlaying)
            {
                HandleInput();
            }
        }
        
        private void OnDestroy()
        {
            if (!_isQuitting)
            {
                DestroyAllEnvironments();
            }
        }
        
        private void OnApplicationQuit()
        {
            _isQuitting = true;
            
            // Sauvegarde automatique si activée
            if (autoSaveOnQuit && Application.isPlaying && _runtimeConfig != null)
            {
                SaveDefaultConfig();
                Debug.Log("[Supervisor] Auto-saved config on quit");
            }
            
            DestroyAllEnvironmentsImmediate();
        }
        
        #endregion
        
        #region Config Path Management
        
        private void EnsureConfigsFolderExists()
        {
#if UNITY_EDITOR
            string folderPath = configsFolderPath.TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                string[] folders = folderPath.Split('/');
                string currentPath = "";
                
                foreach (string folder in folders)
                {
                    string newPath = string.IsNullOrEmpty(currentPath) ? folder : $"{currentPath}/{folder}";
                    if (!AssetDatabase.IsValidFolder(newPath))
                    {
                        if (string.IsNullOrEmpty(currentPath))
                            AssetDatabase.CreateFolder("Assets", folder);
                        else
                            AssetDatabase.CreateFolder(currentPath, folder);
                    }
                    currentPath = newPath;
                }
                
                Debug.Log($"[Supervisor] Created configs folder: {configsFolderPath}");
            }
#endif
        }
        
        private string GetFullSavePath()
        {
            return configSavePath;
        }
        
        private string GetFullSavePathWithName(string configName)
        {
            string fileName = configName.EndsWith(".asset") ? configName : $"{configName}.asset";
            return Path.Combine(configsFolderPath, fileName).Replace('\\', '/');
        }
        
        #endregion
        
        #region Environment Management
        
        /// <summary>
        /// Create all environments
        /// </summary>
        public void CreateAllEnvironments()
        {
            if (environmentPrefab == null)
            {
                Debug.LogError("[Supervisor] Environment prefab not assigned!");
                return;
            }
            
            SimulationConfig configToUse = Application.isPlaying && _runtimeConfig != null ? _runtimeConfig : defaultConfig;
            
            if (configToUse == null)
            {
                Debug.LogError("[Supervisor] Config is null!");
                return;
            }
            
            DestroyAllEnvironments();
            
            for (int i = 0; i < configToUse.environmentCount; i++)
            {
                CreateEnvironment(i, configToUse);
            }
            
            OnEnvironmentsChanged?.Invoke();
            
            if (logEnvironmentEvents)
            {
                Debug.Log($"[Supervisor] Created {configToUse.environmentCount} environments");
            }
        }
        
        private void CreateEnvironment(int index, SimulationConfig configToUse)
        {
            Vector3 position = new Vector3(index * configToUse.environmentSpacing, 0, 0);
            GameObject instance = Instantiate(environmentPrefab, position, Quaternion.identity, transform);
            instance.name = $"Environment_{index}";
            
            var gameManager = instance.GetComponent<GameManager>();
            if (gameManager != null)
            {
                gameManager.SetEnvironmentId(index);
                gameManager.ApplyConfig(configToUse);
                
                if (configToUse.environmentCount > 1)
                {
                    gameManager.SetROSPrefix($"env_{index}");
                }
                
                _gameManagers.Add(gameManager);
            }
            
            _environmentInstances.Add(instance);
        }
        
        /// <summary>
        /// Destroy all environments
        /// </summary>
        public void DestroyAllEnvironments()
        {
            if (_environmentInstances.Count == 0) return;
            
            foreach (var env in _environmentInstances)
            {
                if (env != null)
                {
                    if (Application.isPlaying)
                        Destroy(env);
                    else
                        DestroyImmediate(env);
                }
            }
            
            _environmentInstances.Clear();
            _gameManagers.Clear();
            
            OnEnvironmentsChanged?.Invoke();
            
            if (logEnvironmentEvents)
            {
                Debug.Log("[Supervisor] Destroyed all environments");
            }
        }
        
        private void DestroyAllEnvironmentsImmediate()
        {
            foreach (var env in _environmentInstances)
            {
                if (env != null)
                {
                    DestroyImmediate(env);
                }
            }
            
            _environmentInstances.Clear();
            _gameManagers.Clear();
        }
        
        /// <summary>
        /// Reset all environments
        /// </summary>
        public void ResetAllEnvironments()
        {
            foreach (var gameManager in _gameManagers)
            {
                if (gameManager != null)
                {
                    gameManager.EditorReset();
                }
            }
            
            if (logEnvironmentEvents)
            {
                Debug.Log("[Supervisor] Reset all environments");
            }
        }
        
        /// <summary>
        /// Rebuild all environments (after config changes)
        /// </summary>
        public void RebuildAllEnvironments()
        {
            CreateAllEnvironments();
        }
        
        #endregion
        
        #region Configuration
        
        /// <summary>
        /// Update configuration and apply to all environments
        /// </summary>
        public void UpdateConfig(SimulationConfig newConfig)
        {
            if (newConfig == null) return;
            
            SimulationConfig targetConfig = Application.isPlaying && _runtimeConfig != null ? _runtimeConfig : defaultConfig;
            if (targetConfig == null) return;
            
            newConfig.CopyTo(targetConfig);
            ApplyConfigToAll();
            
            // Notifier l'UI du changement
            OnConfigChanged?.Invoke();
        }
        
        /// <summary>
        /// Apply current config to all existing environments
        /// </summary>
        public void ApplyConfigToAll()
        {
            SimulationConfig configToApply = Application.isPlaying && _runtimeConfig != null ? _runtimeConfig : defaultConfig;
            if (configToApply == null) return;
            
            // Apply global settings
            configToApply.ApplyTimeSettings();
            
            // Apply to each GameManager
            foreach (var gameManager in _gameManagers)
            {
                if (gameManager != null)
                {
                    gameManager.ApplyConfig(configToApply);
                }
            }
            
            if (logEnvironmentEvents)
            {
                Debug.Log($"[Supervisor] Applied config to {_gameManagers.Count} environments");
            }
        }
        
        #endregion
        
        #region Save/Load Config
        
        /// <summary>
        /// Sauvegarde la config actuelle comme config par défaut
        /// </summary>
        public void SaveDefaultConfig()
        {
            if (defaultConfig == null)
            {
                Debug.LogError("[Supervisor] No default config to save!");
                return;
            }
            
            SimulationConfig source = Application.isPlaying && _runtimeConfig != null ? _runtimeConfig : defaultConfig;
            if (source == null) return;
            
            source.CopyTo(defaultConfig);
            
#if UNITY_EDITOR
            EditorUtility.SetDirty(defaultConfig);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Supervisor] Default config saved to: {AssetDatabase.GetAssetPath(defaultConfig)}");
#endif
        }
        
        /// <summary>
        /// Sauvegarde la configuration actuelle dans le chemin par défaut
        /// </summary>
        public void SaveCurrentConfig()
        {
            SaveDefaultConfig();
        }
        
        /// <summary>
        /// Sauvegarde la configuration dans un fichier JSON
        /// </summary>
        public void SaveConfigToJson(string fileName)
        {
            string fullPath = Path.Combine(Application.streamingAssetsPath, configsFolderPath, fileName);
            if (!fullPath.EndsWith(".json")) fullPath += ".json";
            
            string directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            SimulationConfig source = Application.isPlaying && _runtimeConfig != null ? _runtimeConfig : defaultConfig;
            source?.SaveToJson(fullPath);
            
            Debug.Log($"[Supervisor] Config saved to JSON: {fullPath}");
        }
        
        /// <summary>
        /// Charge une configuration depuis un fichier JSON
        /// </summary>
        public bool LoadConfigFromJson(string fileName)
        {
            string fullPath = Path.Combine(Application.streamingAssetsPath, configsFolderPath, fileName);
            if (!fullPath.EndsWith(".json")) fullPath += ".json";
            
            if (!File.Exists(fullPath))
            {
                Debug.LogError($"[Supervisor] Config not found: {fullPath}");
                return false;
            }
            
            SimulationConfig loaded = SimulationConfig.LoadFromJson(fullPath);
            if (loaded != null)
            {
                UpdateConfig(loaded);
                OnConfigLoaded?.Invoke();
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Retourne la liste des configs JSON disponibles
        /// </summary>
        public List<string> GetAvailableConfigs()
        {
            List<string> configs = new List<string>();
            string fullPath = Path.Combine(Application.streamingAssetsPath, configsFolderPath);
            
            if (Directory.Exists(fullPath))
            {
                string[] files = Directory.GetFiles(fullPath, "*.json");
                foreach (string file in files)
                {
                    configs.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
            
            return configs;
        }
        
        #endregion
        
        #region UI Callbacks
        
        public void OnEnvironmentCountChanged(int newCount)
        {
            SimulationConfig target = Application.isPlaying && _runtimeConfig != null ? _runtimeConfig : defaultConfig;
            if (target != null)
            {
                target.environmentCount = Mathf.Max(1, newCount);
                CreateAllEnvironments();
            }
        }
        
        public void OnHumanNumbersChanged(int min, int max)
        {
            SimulationConfig target = Application.isPlaying && _runtimeConfig != null ? _runtimeConfig : defaultConfig;
            if (target != null)
            {
                target.minHumans = min;
                target.maxHumans = max;
                ApplyConfigToAll();
            }
        }
        
        public void OnInferenceModeChanged(bool isOn)
        {
            InferenceMode = isOn;
            soloView?.SetActive(InferenceMode);
            multipleView?.SetActive(!InferenceMode);
            
            SimulationConfig target = Application.isPlaying && _runtimeConfig != null ? _runtimeConfig : defaultConfig;
            if (target != null)
            {
                target.inferenceMode = isOn;
                
                if (InferenceMode && target.environmentCount != 1)
                {
                    target.environmentCount = 1;
                    CreateAllEnvironments();
                }
            }
            
            if (logEnvironmentEvents)
            {
                Debug.Log($"[Supervisor] Inference mode changed to {InferenceMode}");
            }
        }
        
        public void OnFolderPathChanged(string folderPath)
        {
            SimulationConfig target = Application.isPlaying && _runtimeConfig != null ? _runtimeConfig : defaultConfig;
            if (target != null)
            {
                target.datasetPath = folderPath;
                ApplyConfigToAll();
            }
            
            if (logEnvironmentEvents)
            {
                Debug.Log($"[Supervisor] Folder path changed to {folderPath}");
            }
        }
        
        #endregion
        
        #region Input Handling
        
        private void HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                ResetAllEnvironments();
            }
            
            if (Input.GetKeyDown(KeyCode.C))
            {
                CreateAllEnvironments();
            }
            
            if (Input.GetKeyDown(KeyCode.Delete))
            {
                DestroyAllEnvironments();
            }
            
            if (Input.GetKeyDown(KeyCode.Space))
            {
                clock?.TogglePause();
            }
        }
        
        #endregion
        
        #region Public API
        
        public GameManager GetGameManager(int index)
        {
            if (index < 0 || index >= _gameManagers.Count)
                return null;
            
            return _gameManagers[index];
        }
        
        public GameObject GetEnvironment(int index)
        {
            if (index < 0 || index >= _environmentInstances.Count)
                return null;
            
            return _environmentInstances[index];
        }
        
        public List<GameManager> GetAllGameManagers()
        {
            return new List<GameManager>(_gameManagers);
        }
        
        #endregion
        
        #region Editor Utilities
        
        [ContextMenu("Create Environments")]
        private void EditorCreateEnvironments() => CreateAllEnvironments();
        
        [ContextMenu("Destroy Environments")]
        private void EditorDestroyEnvironments() => DestroyAllEnvironments();
        
        [ContextMenu("Reset Environments")]
        private void EditorResetEnvironments() => ResetAllEnvironments();
        
        [ContextMenu("Apply Config")]
        private void EditorApplyConfig() => ApplyConfigToAll();
        
        [ContextMenu("Save Default Config")]
        private void EditorSaveDefaultConfig() => SaveDefaultConfig();
        
        [ContextMenu("Save Config to JSON")]
        private void EditorSaveConfigToJson()
        {
            string fileName = $"config_{System.DateTime.Now:yyyyMMdd_HHmmss}";
            SaveConfigToJson(fileName);
        }
        
        [ContextMenu("Reset to Default Config")]
        private void EditorResetConfig()
        {
            if (defaultConfig != null)
            {
                var newDefault = ScriptableObject.CreateInstance<SimulationConfig>();
                newDefault.CopyTo(defaultConfig);
                
                if (Application.isPlaying && _runtimeConfig != null)
                {
                    newDefault.CopyTo(_runtimeConfig);
                    ApplyConfigToAll();
                }
                
#if UNITY_EDITOR
                EditorUtility.SetDirty(defaultConfig);
                AssetDatabase.SaveAssets();
#endif
                
                OnConfigChanged?.Invoke();
                Debug.Log("[Supervisor] Config reset to default");
            }
        }
        
        [ContextMenu("Log Configuration")]
        private void EditorLogConfiguration()
        {
            SimulationConfig configToLog = Config;
            
            if (configToLog == null)
            {
                Debug.Log("[Supervisor] Config is null!");
                return;
            }
            
            Debug.Log($"[Supervisor] Configuration:\n" +
                      $"  Environments: {configToLog.environmentCount}\n" +
                      $"  Spacing: {configToLog.environmentSpacing}\n" +
                      $"  Time Scale: {configToLog.timeScale}\n" +
                      $"  Inference Mode: {InferenceMode}\n" +
                      $"  Min Humans: {configToLog.minHumans}\n" +
                      $"  Max Humans: {configToLog.maxHumans}\n" +
                      $"  Min Agent Distance: {configToLog.minAgentDistance}\n" +
                      $"  Dataset Path: {configToLog.datasetPath}\n" +
                      $"  Active Environments: {_environmentInstances.Count}\n" +
                      $"  Mode: {(Application.isPlaying ? "Runtime (copy)" : "Editor")}");
        }
        
        [ContextMenu("Log Available Configs")]
        private void EditorLogAvailableConfigs()
        {
            var configs = GetAvailableConfigs();
            Debug.Log($"[Supervisor] Available configs ({configs.Count}):\n  {(configs.Count > 0 ? string.Join("\n  ", configs) : "none")}");
        }
        
        #endregion
    }
}