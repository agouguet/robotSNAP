// Scripts/RobotSNAP/Core/ConfigManager.cs
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RobotSNAP.Core
{
    /// <summary>
    /// Crée et garde les dossiers où vivent les fichiers de configuration.
    /// </summary>
    public class ConfigManager : MonoBehaviour
    {
        [Header("Config Persistence")]
        [SerializeField] private bool _autoSaveOnQuit = false;
        [SerializeField] private string _configsFolderPath = "Assets/Configs/";
        
        [Header("Settings")]
        [SerializeField] private bool _logEvents = true;
        
        private string _streamingAssetsConfigPath;

        private void Awake()
        {
            InitializePaths();
            EnsureConfigsFolderExists();
        }
        
        private void InitializePaths()
        {
            // Les données utilisateur doivent rester inscriptibles dans un build.
            _streamingAssetsConfigPath = ConfigPersistence.ConfigDirectoryPath;
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
}
