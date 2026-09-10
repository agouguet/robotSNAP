// Scripts/RobotSNAP/Supervisor.cs
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using RobotSNAP.Core;

namespace RobotSNAP
{
    /// <summary>
    /// Supervisor principal - Gère la configuration globale, l'horloge et la persistance.
    /// Ne gère PAS les environnements (délégué à ScenarioManager).
    /// </summary>
    [ExecuteAlways]
    public sealed class Supervisor : MonoBehaviour
    {
        #region Singleton

        private static Supervisor _instance;
        public static Supervisor Instance
        {
            get
            {
                if (_instance == null)
                    _instance = FindObjectOfType<Supervisor>();
                return _instance;
            }
        }

        #endregion

        #region Serialized Fields

        [Header("Startup")]
        [SerializeField] private bool _startPaused = true;

        [Header("Configuration")]
        [Tooltip("Configuration par défaut (ScriptableObject)")]
        [SerializeField] private SimulationConfig _defaultConfig;

        [Header("References")]
        [Tooltip("Horloge globale de la simulation")]
        [SerializeField] private Clock _clock;

        [Header("Config Persistence")]
        [Tooltip("Sauvegarde automatique à la fermeture")]
        [SerializeField] private bool _autoSaveOnQuit = false;

        [Header("Debug")]
        [Tooltip("Active les logs de débogage")]
        [SerializeField] private bool _logEvents = true;

        #endregion

        #region Private Fields

        private SimulationConfig _runtimeConfig;
        private bool _isQuitting;
        private bool _isInitialized;

        #endregion

        #region Public Properties

        public SimulationConfig ActiveConfig => Application.isPlaying ? _runtimeConfig : _defaultConfig;
        public bool IsInitialized => _isInitialized;
        public bool IsQuitting => _isQuitting;

        #endregion

        #region Events

        public event Action<SimulationConfig> OnConfigChanged;
        public event Action OnInitialized;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            InitializeDefaultConfig();
            SetupClock();

            if (Application.isPlaying)
                DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (Application.isPlaying)
            {
                _runtimeConfig = _defaultConfig.Clone();
                _runtimeConfig.name = "RuntimeConfig";
                _runtimeConfig.ApplyTimeSettings();
            }

            _isInitialized = true;
            OnInitialized?.Invoke();

            if (_logEvents)
                Debug.Log($"[Supervisor] Initialized. Time scale: {ActiveConfig.TimeScale}");
        }

        private void OnApplicationQuit()
        {
            _isQuitting = true;
            if (_autoSaveOnQuit && _runtimeConfig != null)
                SaveDefaultConfig();
        }

        #endregion

        #region Private Methods

        private void InitializeDefaultConfig()
        {
            if (_defaultConfig == null)
            {
                _defaultConfig = ScriptableObject.CreateInstance<SimulationConfig>();
                _defaultConfig.name = "DefaultConfig";
                Debug.Log("[Supervisor] Created default SimulationConfig");
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
                        DontDestroyOnLoad(clockGO);
                    Debug.Log("[Supervisor] Created Clock");
                }
            }
        }

        #endregion

        #region Public API - Configuration

        public void UpdateConfig(SimulationConfig newConfig)
        {
            if (newConfig == null) return;

            var target = Application.isPlaying ? _runtimeConfig : _defaultConfig;
            if (target == null) return;

            newConfig.CopyTo(target);
            target.ApplyTimeSettings();

            OnConfigChanged?.Invoke(target);
            if (_logEvents) Debug.Log("[Supervisor] Configuration updated");
        }

        public void SaveDefaultConfig()
        {
            if (_defaultConfig == null) return;
            var source = Application.isPlaying ? _runtimeConfig : _defaultConfig;
            source?.CopyTo(_defaultConfig);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(_defaultConfig);
            UnityEditor.AssetDatabase.SaveAssets();
#endif
            if (_logEvents) Debug.Log("[Supervisor] Default config saved");
        }

        public void SaveConfigToJson(string fileName)
        {
            var source = Application.isPlaying ? _runtimeConfig : _defaultConfig;
            if (source == null) return;

            string fullPath = Path.Combine(Application.persistentDataPath, "Configs", fileName);
            if (!fullPath.EndsWith(".json")) fullPath += ".json";

            string directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            source.SaveToJson(fullPath);
            if (_logEvents) Debug.Log($"[Supervisor] Config saved to JSON: {fullPath}");
        }

        public bool LoadConfigFromJson(string fileName)
        {
            string fullPath = Path.Combine(Application.persistentDataPath, "Configs", fileName);
            if (!fullPath.EndsWith(".json")) fullPath += ".json";

            if (!File.Exists(fullPath))
            {
                Debug.LogError($"[Supervisor] Config file not found: {fullPath}");
                return false;
            }

            var loaded = SimulationConfig.LoadFromJson(fullPath);
            if (loaded != null)
            {
                UpdateConfig(loaded);
                return true;
            }
            return false;
        }

        public List<string> GetAvailableConfigs()
        {
            List<string> configs = new List<string>();
            string fullPath = Path.Combine(Application.persistentDataPath, "Configs");
            if (Directory.Exists(fullPath))
            {
                foreach (var file in Directory.GetFiles(fullPath, "*.json"))
                    configs.Add(Path.GetFileNameWithoutExtension(file));
            }
            return configs;
        }

        public void ResetConfigToDefault()
        {
            var fresh = ScriptableObject.CreateInstance<SimulationConfig>();
            fresh.ResetToDefaults();

            if (Application.isPlaying && _runtimeConfig != null)
            {
                fresh.CopyTo(_runtimeConfig);
                _runtimeConfig.ApplyTimeSettings();
            }
            fresh.CopyTo(_defaultConfig);

            OnConfigChanged?.Invoke(ActiveConfig);
            Debug.Log("[Supervisor] Config reset to default");
        }

        #endregion

        #region Public API - Clock Control

        public void TogglePause() => _clock?.TogglePause();
        public void Pause() => _clock?.Pause();
        public void Resume() => _clock?.Resume();
        public bool IsPaused => _clock != null && _clock.IsPaused;

        #endregion

        #region Editor Utilities

#if UNITY_EDITOR
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
            var cfg = ActiveConfig;
            Debug.Log($"[Supervisor] Status:\n" +
                      $"  Initialized: {_isInitialized}\n" +
                      $"  Time Scale: {(cfg != null ? cfg.TimeScale : 1f)}\n" +
                      $"  Dataset Path: {(cfg != null ? cfg.DatasetPath : "none")}\n" +
                      $"  Default Scenario: {(cfg != null ? cfg.DefaultScenario : "none")}");
        }
#endif

        #endregion
    }
}
