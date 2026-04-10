// Scripts/RobotSNAP/Supervisor.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using RobotSNAP.Core;
using RobotSNAP.Core.Scenario;

namespace RobotSNAP
{
    /// <summary>
    /// Supervisor principal - Orchestrateur de la simulation
    /// Gère le cycle de vie, la configuration et coordonne les managers
    /// </summary>
    [ExecuteAlways]
    public sealed class Supervisor : MonoBehaviour
    {
        #region Singleton

        private static Supervisor _instance;
        
        /// <summary>
        /// Instance singleton du Supervisor (lecture seule)
        /// </summary>
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

        #endregion

        #region Serialized Fields (Configuration dans l'inspecteur)

        [Header("Core Managers")]
        [Tooltip("Manager responsable de la création/destruction des environnements")]
        [SerializeField] private EnvironmentManager _environmentManager;
        
        [Tooltip("Manager responsable de la sauvegarde/chargement des configurations")]
        [SerializeField] private ConfigManager _configManager;
        
        [Tooltip("Manager responsable des scénarios YAML")]
        [SerializeField] private ScenarioSupervisor _scenarioSupervisor;
        
        [Tooltip("Horloge globale de la simulation")]
        [SerializeField] private Clock _clock;

        [Header("Configuration")]
        [Tooltip("Configuration par défaut (ScriptableObject)")]
        [SerializeField] private SimulationConfig _defaultConfig;

        [Header("View Settings")]
        [Tooltip("Vue pour le mode inférence (un seul environnement)")]
        [SerializeField] private GameObject _soloView;
        
        [Tooltip("Vue pour le mode multiple (plusieurs environnements)")]
        [SerializeField] private GameObject _multipleView;

        [Header("Debug")]
        [Tooltip("Active les logs de débogage")]
        [SerializeField] private bool _logEvents = true;

        #endregion

        #region Private Fields

        private SimulationConfig _runtimeConfig;
        private bool _isQuitting;
        private bool _inferenceMode;
        private bool _isInitialized;

        #endregion

        #region Public Properties (Lecture seule)

        /// <summary>
        /// Configuration active (runtime en jeu, default dans l'éditeur)
        /// </summary>
        public SimulationConfig ActiveConfig => Application.isPlaying ? _runtimeConfig : _defaultConfig;

        /// <summary>
        /// Mode inférence actif (true = un seul environnement)
        /// </summary>
        public bool IsInferenceMode 
        { 
            get => _inferenceMode;
            private set
            {
                if (_inferenceMode != value)
                {
                    _inferenceMode = value;
                    OnInferenceModeChangedInternal();
                }
            }
        }

        /// <summary>
        /// Nombre d'environnements actifs
        /// </summary>
        public int EnvironmentCount => _environmentManager?.EnvironmentCount ?? 0;

        /// <summary>
        /// Liste des environnements (lecture seule)
        /// </summary>
        public IReadOnlyList<GameObject> Environments => _environmentManager?.Environments;

        /// <summary>
        /// Liste des GameManagers (lecture seule)
        /// </summary>
        public IReadOnlyList<GameManager> GameManagers => _environmentManager?.GameManagers;

        /// <summary>
        /// Le Supervisor est-il initialisé ?
        /// </summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// Le Supervisor est-il en train de quitter ?
        /// </summary>
        public bool IsQuitting => _isQuitting;

        #endregion

        #region Events

        /// <summary>
        /// Événement déclenché quand la configuration change
        /// </summary>
        public event Action<SimulationConfig> OnConfigChanged;
        
        /// <summary>
        /// Événement déclenché quand le mode inférence change
        /// </summary>
        public event Action<bool> OnInferenceModeChanged;
        
        /// <summary>
        /// Événement déclenché quand le Supervisor est initialisé
        /// </summary>
        public event Action OnInitialized;
        
        /// <summary>
        /// Événement déclenché avant la destruction du Supervisor
        /// </summary>
        public event Action OnDestroying;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            // Gestion du singleton
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[Supervisor] Duplicate instance detected, destroying...");
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            
            // Initialisation de base
            InitializeDefaultConfig();
            EnsureManagersExist();
            SetupClock();
            
            // Persistance entre les scènes
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }
        }
        
        private void Start()
        {
            if (Application.isPlaying)
            {
                InitializeRuntime();
            }
            
            _isInitialized = true;
            OnInitialized?.Invoke();
            
            if (_logEvents)
            {
                Debug.Log($"[Supervisor] Initialized - Time scale: {(_runtimeConfig != null ? _runtimeConfig.TimeScale : 1f)}");
            }
        }
        
        private void OnDestroy()
        {
            if (_isQuitting) return;
            
            OnDestroying?.Invoke();
            
            if (!_isQuitting && _environmentManager != null)
            {
                _environmentManager.ClearAllEnvironments();
            }
        }
        
        private void OnApplicationQuit()
        {
            _isQuitting = true;
            
            // Sauvegarde automatique si activée
            if (Application.isPlaying && _configManager != null && _configManager.AutoSaveOnQuit && _runtimeConfig != null)
            {
                _configManager.SaveToScriptableObject(_runtimeConfig, _defaultConfig);
                Debug.Log("[Supervisor] Auto-saved configuration on quit");
            }
        }

        #endregion

        #region Private Initialization Methods

        private void InitializeDefaultConfig()
        {
            if (_defaultConfig == null)
            {
                _defaultConfig = ScriptableObject.CreateInstance<SimulationConfig>();
                _defaultConfig.name = "DefaultConfig";
                Debug.Log("[Supervisor] Created default SimulationConfig");
            }
        }
        
        private void EnsureManagersExist()
        {
            // EnvironmentManager
            if (_environmentManager == null)
            {
                _environmentManager = GetComponent<EnvironmentManager>();
                if (_environmentManager == null)
                {
                    _environmentManager = gameObject.AddComponent<EnvironmentManager>();
                    Debug.Log("[Supervisor] Created EnvironmentManager");
                }
            }
            
            // ConfigManager
            if (_configManager == null)
            {
                _configManager = GetComponent<ConfigManager>();
                if (_configManager == null)
                {
                    _configManager = gameObject.AddComponent<ConfigManager>();
                    Debug.Log("[Supervisor] Created ConfigManager");
                }
            }
            
            // ScenarioSupervisor (optionnel)
            if (_scenarioSupervisor == null)
            {
                _scenarioSupervisor = GetComponent<ScenarioSupervisor>();
                // Ne pas créer automatiquement - optionnel
            }
        }
        
        private void SetupClock()
        {
            if (_clock == null)
            {
                _clock = FindObjectOfType<Clock>();
                if (_clock == null)
                {
                    var clockGO = new GameObject("Clock");
                    _clock = clockGO.AddComponent<Clock>();
                    if (Application.isPlaying)
                    {
                        DontDestroyOnLoad(clockGO);
                    }
                    Debug.Log("[Supervisor] Created Clock");
                }
            }
        }
        
        private void InitializeRuntime()
        {
            // Créer une copie de travail pour les modifications en jeu
            _runtimeConfig = _defaultConfig.Clone();
            _runtimeConfig.name = "RuntimeConfig";
            
            // Appliquer les paramètres de temps
            _runtimeConfig.ApplyTimeSettings();
            
            // Créer les environnements
            _environmentManager.CreateAllEnvironments(_runtimeConfig);
            
            // Charger le scénario par défaut si disponible
            if (_scenarioSupervisor != null && _scenarioSupervisor.HasDefaultScenario)
            {
                _scenarioSupervisor.LoadDefaultScenario();
            }
        }

        #endregion

        #region Private Event Handlers

        private void OnInferenceModeChangedInternal()
        {
            // Mettre à jour les vues
            if (_soloView != null)
                _soloView.SetActive(_inferenceMode);
            if (_multipleView != null)
                _multipleView.SetActive(!_inferenceMode);
            
            // Mettre à jour la configuration
            var targetConfig = Application.isPlaying ? _runtimeConfig : _defaultConfig;
            if (targetConfig != null)
            {
                targetConfig.InferenceMode = _inferenceMode;
                
                // En mode inférence, un seul environnement
                if (_inferenceMode && targetConfig.EnvironmentCount != 1)
                {
                    targetConfig.EnvironmentCount = 1;
                    _environmentManager.CreateAllEnvironments(targetConfig);
                }
            }
            
            OnInferenceModeChanged?.Invoke(_inferenceMode);
            
            if (_logEvents)
            {
                Debug.Log($"[Supervisor] Inference mode changed to {_inferenceMode}");
            }
        }

        #endregion

        #region Public API - Configuration Management

        /// <summary>
        /// Met à jour la configuration active
        /// </summary>
        /// <param name="newConfig">Nouvelle configuration</param>
        public void UpdateConfig(SimulationConfig newConfig)
        {
            if (newConfig == null)
            {
                Debug.LogError("[Supervisor] Cannot update config: newConfig is null");
                return;
            }
            
            var targetConfig = Application.isPlaying ? _runtimeConfig : _defaultConfig;
            if (targetConfig == null)
            {
                Debug.LogError("[Supervisor] Cannot update config: target config is null");
                return;
            }
            
            newConfig.CopyTo(targetConfig);
            targetConfig.ApplyTimeSettings();
            
            _environmentManager?.ApplyConfigToAll(targetConfig);
            
            OnConfigChanged?.Invoke(targetConfig);
            
            if (_logEvents)
            {
                Debug.Log($"[Supervisor] Configuration updated");
            }
        }
        
        /// <summary>
        /// Charge une configuration depuis un fichier JSON
        /// </summary>
        /// <param name="fileName">Nom du fichier (sans extension)</param>
        /// <returns>True si chargement réussi</returns>
        public bool LoadConfigFromJson(string fileName)
        {
            if (_configManager == null)
            {
                Debug.LogError("[Supervisor] ConfigManager is null");
                return false;
            }
            
            var loadedConfig = _configManager.LoadFromJson(fileName);
            if (loadedConfig != null)
            {
                UpdateConfig(loadedConfig);
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Sauvegarde la configuration active en JSON
        /// </summary>
        /// <param name="fileName">Nom du fichier (sans extension)</param>
        public void SaveConfigToJson(string fileName)
        {
            if (_configManager == null)
            {
                Debug.LogError("[Supervisor] ConfigManager is null");
                return;
            }
            
            var sourceConfig = Application.isPlaying ? _runtimeConfig : _defaultConfig;
            _configManager.SaveToJson(sourceConfig, fileName);
        }
        
        /// <summary>
        /// Liste des configurations JSON disponibles
        /// </summary>
        public IReadOnlyList<string> GetAvailableConfigs()
        {
            return _configManager?.GetAvailableConfigs() ?? new List<string>();
        }
        
        /// <summary>
        /// Sauvegarde la configuration active comme config par défaut
        /// </summary>
        public void SaveDefaultConfig()
        {
            if (_configManager == null)
            {
                Debug.LogError("[Supervisor] ConfigManager is null");
                return;
            }
            
            var sourceConfig = Application.isPlaying ? _runtimeConfig : _defaultConfig;
            _configManager.SaveToScriptableObject(sourceConfig, _defaultConfig);
        }
        
        /// <summary>
        /// Réinitialise la configuration aux valeurs par défaut
        /// </summary>
        public void ResetConfigToDefault()
        {
            var defaultValues = ScriptableObject.CreateInstance<SimulationConfig>();
            
            if (Application.isPlaying && _runtimeConfig != null)
            {
                defaultValues.CopyTo(_runtimeConfig);
                _runtimeConfig.ApplyTimeSettings();
                _environmentManager?.ApplyConfigToAll(_runtimeConfig);
            }
            
            defaultValues.CopyTo(_defaultConfig);
            
            OnConfigChanged?.Invoke(ActiveConfig);
            Debug.Log("[Supervisor] Configuration reset to default");
        }

        #endregion

        #region Public API - Environment Management

        /// <summary>
        /// Crée tous les environnements selon la configuration active
        /// </summary>
        public void CreateAllEnvironments()
        {
            var config = Application.isPlaying ? _runtimeConfig : _defaultConfig;
            if (config == null)
            {
                Debug.LogError("[Supervisor] Cannot create environments: config is null");
                return;
            }
            
            _environmentManager?.CreateAllEnvironments(config);
        }
        
        /// <summary>
        /// Détruit tous les environnements
        /// </summary>
        public void DestroyAllEnvironments()
        {
            _environmentManager?.ClearAllEnvironments();
        }
        
        /// <summary>
        /// Réinitialise tous les environnements
        /// </summary>
        public void ResetAllEnvironments()
        {
            _environmentManager?.ResetAllEnvironments();
        }
        
        /// <summary>
        /// Applique la configuration active à tous les environnements
        /// </summary>
        public void ApplyConfigToAll()
        {
            var config = Application.isPlaying ? _runtimeConfig : _defaultConfig;
            _environmentManager?.ApplyConfigToAll(config);
        }
        
        /// <summary>
        /// Reconstruit tous les environnements (destruction + création)
        /// </summary>
        public void RebuildAllEnvironments()
        {
            DestroyAllEnvironments();
            CreateAllEnvironments();
        }
        
        /// <summary>
        /// Obtient un GameManager par index
        /// </summary>
        /// <param name="index">Index de l'environnement</param>
        /// <returns>GameManager ou null</returns>
        public GameManager GetGameManager(int index)
        {
            return _environmentManager?.GetGameManager(index);
        }
        
        /// <summary>
        /// Obtient tous les GameManagers
        /// </summary>
        public IReadOnlyList<GameManager> GetAllGameManagers()
        {
            return _environmentManager?.GetAllGameManagers() ?? new List<GameManager>();
        }

        #endregion

        #region Public API - Scenario Management

        /// <summary>
        /// Charge un scénario YAML
        /// </summary>
        /// <param name="scenarioName">Nom du scénario (sans extension)</param>
        public void LoadScenario(string scenarioName)
        {
            if (_scenarioSupervisor == null)
            {
                Debug.LogWarning("[Supervisor] ScenarioSupervisor not available");
                return;
            }
            
            _scenarioSupervisor.LoadScenario(scenarioName);
        }
        
        /// <summary>
        /// Applique le scénario courant à tous les environnements
        /// </summary>
        public void ApplyCurrentScenarioToAll()
        {
            if (_scenarioSupervisor == null)
            {
                Debug.LogWarning("[Supervisor] ScenarioSupervisor not available");
                return;
            }
            
            _scenarioSupervisor.ApplyLoadedScenarioToAll();
        }
        
        /// <summary>
        /// Liste des scénarios disponibles
        /// </summary>
        public List<string> GetAvailableScenarios()
        {
            return _scenarioSupervisor?.GetAvailableScenarios() ?? new List<string>();
        }
        
        /// <summary>
        /// Nom du scénario courant
        /// </summary>
        public string CurrentScenarioId => _scenarioSupervisor?.CurrentScenarioId ?? string.Empty;

        #endregion

        #region Public API - UI Callbacks (pour l'UI)

        /// <summary>
        /// Callback UI pour changer le nombre d'environnements
        /// </summary>
        public void OnEnvironmentCountChanged(int newCount)
        {
            var targetConfig = Application.isPlaying ? _runtimeConfig : _defaultConfig;
            if (targetConfig != null)
            {
                targetConfig.EnvironmentCount = Mathf.Max(1, newCount);
                
                if (!IsInferenceMode)
                {
                    CreateAllEnvironments();
                }
            }
        }
        
        /// <summary>
        /// Callback UI pour changer le nombre d'humains (min/max)
        /// </summary>
        public void OnHumanCountChanged(int minHumans, int maxHumans)
        {
            var targetConfig = Application.isPlaying ? _runtimeConfig : _defaultConfig;
            if (targetConfig != null)
            {
                targetConfig.MinHumans = minHumans;
                targetConfig.MaxHumans = maxHumans;
                ApplyConfigToAll();
            }
        }
        
        /// <summary>
        /// Callback UI pour changer le mode inférence
        /// </summary>
        public void OnInferenceModeToggled(bool isOn)
        {
            IsInferenceMode = isOn;
        }
        
        /// <summary>
        /// Callback UI pour changer le chemin du dataset
        /// </summary>
        public void OnDatasetPathChanged(string path)
        {
            var targetConfig = Application.isPlaying ? _runtimeConfig : _defaultConfig;
            if (targetConfig != null)
            {
                targetConfig.DatasetPath = path;
                ApplyConfigToAll();
            }
        }
        
        /// <summary>
        /// Callback UI pour changer l'échelle de temps
        /// </summary>
        public void OnTimeScaleChanged(float timeScale)
        {
            var targetConfig = Application.isPlaying ? _runtimeConfig : _defaultConfig;
            if (targetConfig != null)
            {
                targetConfig.TimeScale = Mathf.Max(0.1f, timeScale);
                targetConfig.ApplyTimeSettings();
            }
        }

        #endregion

        #region Public API - Clock Control

        /// <summary>
        /// Met en pause ou reprend la simulation
        /// </summary>
        public void TogglePause()
        {
            _clock?.TogglePause();
        }
        
        /// <summary>
        /// Met la simulation en pause
        /// </summary>
        public void Pause()
        {
            _clock?.Pause();
        }
        
        /// <summary>
        /// Reprend la simulation
        /// </summary>
        public void Resume()
        {
            _clock?.Resume();
        }
        
        /// <summary>
        /// La simulation est-elle en pause ?
        /// </summary>
        public bool IsPaused => _clock != null && _clock.IsPaused;

        #endregion


        #region Public API - Scenario Management (à ajouter)


        /// <summary>
        /// Applique un scénario à un environnement spécifique
        /// </summary>
        public void ApplyScenarioToEnvironment(int envIndex, ScenarioData scenario)
        {
            if (_scenarioSupervisor != null)
            {
                _scenarioSupervisor.ApplyScenarioToEnvironment(envIndex, scenario);
            }
        }

        #endregion


        #region Editor Utilities (ContextMenu)

        #if UNITY_EDITOR
        
        [ContextMenu("Environment/Create All")]
        private void EditorCreateEnvironments() => CreateAllEnvironments();
        
        [ContextMenu("Environment/Destroy All")]
        private void EditorDestroyEnvironments() => DestroyAllEnvironments();
        
        [ContextMenu("Environment/Reset All")]
        private void EditorResetEnvironments() => ResetAllEnvironments();
        
        [ContextMenu("Environment/Rebuild All")]
        private void EditorRebuildEnvironments() => RebuildAllEnvironments();
        
        [ContextMenu("Config/Apply to All")]
        private void EditorApplyConfig() => ApplyConfigToAll();
        
        [ContextMenu("Config/Save as Default")]
        private void EditorSaveDefaultConfig() => SaveDefaultConfig();
        
        [ContextMenu("Config/Reset to Default")]
        private void EditorResetConfig() => ResetConfigToDefault();
        
        [ContextMenu("Config/Save to JSON")]
        private void EditorSaveConfigToJson()
        {
            string fileName = $"config_{DateTime.Now:yyyyMMdd_HHmmss}";
            SaveConfigToJson(fileName);
        }
        
        [ContextMenu("Debug/Log Status")]
        private void EditorLogStatus()
        {
            var config = ActiveConfig;
            Debug.Log($"[Supervisor] Status:\n" +
                      $"  Initialized: {_isInitialized}\n" +
                      $"  Inference Mode: {IsInferenceMode}\n" +
                      $"  Environment Count: {EnvironmentCount}\n" +
                      $"  Time Scale: {(config != null ? config.TimeScale : 1f)}\n" +
                      $"  Min Humans: {(config != null ? config.MinHumans : 0)}\n" +
                      $"  Max Humans: {(config != null ? config.MaxHumans : 0)}\n" +
                      $"  Dataset Path: {(config != null ? config.DatasetPath : "none")}\n" +
                      $"  Current Scenario: {CurrentScenarioId}");
        }
        
        [ContextMenu("Scenario/Load Default")]
        private void EditorLoadDefaultScenario()
        {
            if (_scenarioSupervisor != null)
            {
                _scenarioSupervisor.LoadDefaultScenario();
            }
        }
        
        [ContextMenu("Scenario/Apply to All")]
        private void EditorApplyScenarioToAll()
        {
            if (_scenarioSupervisor != null)
            {
                _scenarioSupervisor.ApplyLoadedScenarioToAll();
            }
        }
        
        #endif

        #endregion
    }
}