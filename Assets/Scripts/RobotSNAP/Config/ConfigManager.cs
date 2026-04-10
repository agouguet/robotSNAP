// Scripts/RobotSNAP/Core/ConfigManager.cs
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RobotSNAP.Core
{
    /// <summary>
    /// Gère la sauvegarde/chargement des configurations
    /// </summary>
    public class ConfigManager : MonoBehaviour
    {
        [Header("Config Persistence")]
        [SerializeField] private bool _autoSaveOnQuit = false;
        [SerializeField] private string _configSavePath = "Assets/Configs/SavedConfig.asset";
        [SerializeField] private string _configsFolderPath = "Assets/Configs/";
        
        [Header("Settings")]
        [SerializeField] private bool _logEvents = true;
        
        private string _streamingAssetsConfigPath;
        
        public event Action<SimulationConfig> OnConfigSaved;
        public event Action<SimulationConfig> OnConfigLoaded;
        public event Action<string> OnConfigError;
        
        public string ConfigsFolderPath => _configsFolderPath;
        public bool AutoSaveOnQuit => _autoSaveOnQuit;
        
        private void Awake()
        {
            InitializePaths();
            EnsureConfigsFolderExists();
        }
        
        private void InitializePaths()
        {
            // Chemin dans StreamingAssets pour les configs JSON
            _streamingAssetsConfigPath = Path.Combine(Application.streamingAssetsPath, "Configs");
        }
        
        private void EnsureConfigsFolderExists()
        {
#if UNITY_EDITOR
            string folderPath = _configsFolderPath.TrimEnd('/');
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
                
                if (_logEvents)
                    Debug.Log($"[ConfigManager] Created configs folder: {_configsFolderPath}");
            }
#endif
            // S'assurer que le dossier StreamingAssets/Configs existe aussi
            if (!Directory.Exists(_streamingAssetsConfigPath))
            {
                Directory.CreateDirectory(_streamingAssetsConfigPath);
                if (_logEvents)
                    Debug.Log($"[ConfigManager] Created streaming assets configs folder: {_streamingAssetsConfigPath}");
            }
        }
        
        #region JSON Save/Load
        
        /// <summary>
        /// Sauvegarde la config dans un fichier JSON (dans StreamingAssets)
        /// </summary>
        public void SaveToJson(SimulationConfig config, string fileName)
        {
            if (config == null)
            {
                OnConfigError?.Invoke("Cannot save null config");
                return;
            }
            
            string fullPath = GetJsonPath(fileName);
            string directory = Path.GetDirectoryName(fullPath);
            
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            try
            {
                config.SaveToJson(fullPath);
                OnConfigSaved?.Invoke(config);
                
                if (_logEvents)
                    Debug.Log($"[ConfigManager] Config saved to JSON: {fullPath}");
            }
            catch (Exception e)
            {
                OnConfigError?.Invoke($"Failed to save config: {e.Message}");
                Debug.LogError($"[ConfigManager] {e.Message}");
            }
        }
        
        /// <summary>
        /// Charge une config depuis un fichier JSON
        /// </summary>
        public SimulationConfig LoadFromJson(string fileName)
        {
            string fullPath = GetJsonPath(fileName);
            
            if (!File.Exists(fullPath))
            {
                OnConfigError?.Invoke($"Config not found: {fullPath}");
                Debug.LogWarning($"[ConfigManager] Config not found: {fullPath}");
                return null;
            }
            
            try
            {
                var config = SimulationConfig.LoadFromJson(fullPath);
                if (config != null)
                {
                    OnConfigLoaded?.Invoke(config);
                    if (_logEvents)
                        Debug.Log($"[ConfigManager] Config loaded from JSON: {fileName}");
                }
                return config;
            }
            catch (Exception e)
            {
                OnConfigError?.Invoke($"Failed to load config: {e.Message}");
                Debug.LogError($"[ConfigManager] {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Sauvegarde la config courante avec un nom basé sur la date
        /// </summary>
        public void SaveCurrentConfigWithTimestamp(SimulationConfig config)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"config_{timestamp}";
            SaveToJson(config, fileName);
        }
        
        private string GetJsonPath(string fileName)
        {
            string cleanFileName = fileName;
            if (!cleanFileName.EndsWith(".json"))
                cleanFileName += ".json";
            
            return Path.Combine(_streamingAssetsConfigPath, cleanFileName).Replace('\\', '/');
        }
        
        #endregion
        
        #region ScriptableObject Save/Load
        
        /// <summary>
        /// Sauvegarde la config comme ScriptableObject par défaut
        /// </summary>
        public void SaveToScriptableObject(SimulationConfig source, SimulationConfig target)
        {
            if (source == null)
            {
                OnConfigError?.Invoke("Cannot save null source config");
                return;
            }
            
            if (target == null)
            {
                OnConfigError?.Invoke("Cannot save to null target config");
                return;
            }
            
            source.CopyTo(target);
            
#if UNITY_EDITOR
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();
            
            if (_logEvents)
                Debug.Log($"[ConfigManager] Saved to ScriptableObject: {AssetDatabase.GetAssetPath(target)}");
#endif
        }
        
        /// <summary>
        /// Charge la config depuis un ScriptableObject (utile pour l'éditeur)
        /// </summary>
        public SimulationConfig LoadFromScriptableObject(SimulationConfig source)
        {
            if (source == null) return null;
            
            var clone = source.Clone();
            OnConfigLoaded?.Invoke(clone);
            
            if (_logEvents)
                Debug.Log($"[ConfigManager] Loaded from ScriptableObject: {source.name}");
            
            return clone;
        }
        
        #endregion
        
        #region Queries
        
        /// <summary>
        /// Liste des configs JSON disponibles dans StreamingAssets
        /// </summary>
        public List<string> GetAvailableConfigs()
        {
            List<string> configs = new List<string>();
            
            if (!Directory.Exists(_streamingAssetsConfigPath))
            {
                return configs;
            }
            
            string[] files = Directory.GetFiles(_streamingAssetsConfigPath, "*.json");
            foreach (string file in files)
            {
                configs.Add(Path.GetFileNameWithoutExtension(file));
            }
            
            return configs;
        }
        
        /// <summary>
        /// Liste des configs JSON avec leurs dates de modification
        /// </summary>
        public List<ConfigInfo> GetAvailableConfigsWithInfo()
        {
            var configs = new List<ConfigInfo>();
            
            if (!Directory.Exists(_streamingAssetsConfigPath))
                return configs;
            
            string[] files = Directory.GetFiles(_streamingAssetsConfigPath, "*.json");
            foreach (string file in files)
            {
                var info = new ConfigInfo
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    FilePath = file,
                    LastModified = File.GetLastWriteTime(file)
                };
                configs.Add(info);
            }
            
            // Trier par date décroissante
            configs.Sort((a, b) => b.LastModified.CompareTo(a.LastModified));
            
            return configs;
        }
        
        /// <summary>
        /// Supprime un fichier de configuration
        /// </summary>
        public bool DeleteConfig(string fileName)
        {
            string fullPath = GetJsonPath(fileName);
            
            if (!File.Exists(fullPath))
            {
                Debug.LogWarning($"[ConfigManager] Config not found for deletion: {fullPath}");
                return false;
            }
            
            try
            {
                File.Delete(fullPath);
                if (_logEvents)
                    Debug.Log($"[ConfigManager] Deleted config: {fileName}");
                return true;
            }
            catch (Exception e)
            {
                OnConfigError?.Invoke($"Failed to delete config: {e.Message}");
                return false;
            }
        }
        
        #endregion
        
        #region Application Lifecycle
        
        private void OnApplicationQuit()
        {
            if (_autoSaveOnQuit && Application.isPlaying)
            {
                // Note: La config à sauvegarder doit être passée par le Supervisor
                // On ne fait que logger ici, la sauvegarde réelle est gérée par le Supervisor
                if (_logEvents)
                    Debug.Log("[ConfigManager] Auto-save requested on quit");
            }
        }
        
        #endregion
    }
    
    /// <summary>
    /// Information sur un fichier de configuration
    /// </summary>
    public struct ConfigInfo
    {
        public string Name;
        public string FilePath;
        public DateTime LastModified;
        
        public override string ToString()
        {
            return $"{Name} ({LastModified:yyyy-MM-dd HH:mm})";
        }
    }
}