using UnityEngine;
using UnityEngine.UI;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using TMPro;

namespace RobotSNAP.UI
{
    /// <summary>
    /// Gestionnaire de dropdown pour les datasets
    /// </summary>
    public class DatasetDropdown : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TMP_Dropdown dropdown;
        
        [Header("Settings")]
        [SerializeField] private string folderPathRelative = "Dataset";
        [SerializeField] private bool autoLoadOnStart = true;
        
        [Header("Debug")]
        [SerializeField] private bool logEvents = true;
        
        private List<string> _datasetPaths = new List<string>();
        private List<string> _displayNames = new List<string>();
        private string _currentSelectedPath;
        private int _pendingSelectionIndex = -1;
        
        public event Action<int, string> OnDatasetSelected;
        public TMP_Dropdown Dropdown => dropdown;
        public List<string> DatasetPaths => _datasetPaths;
        public string CurrentSelectedPath => _currentSelectedPath;
        public int PendingSelectionIndex => _pendingSelectionIndex;
        public string PendingSelectionPath => _pendingSelectionIndex >= 0 && _pendingSelectionIndex < _datasetPaths.Count 
            ? _datasetPaths[_pendingSelectionIndex] : null;
        public int DatasetCount => _datasetPaths.Count;
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            if (dropdown == null)
                dropdown = GetComponent<TMP_Dropdown>();
        }
        
        private void Start()
        {
            if (autoLoadOnStart)
            {
                RefreshDatasetList();
            }
        }
        
        private void OnDestroy()
        {
            // Nettoyer l'événement
            if (dropdown != null)
                dropdown.onValueChanged.RemoveListener(OnDropdownValueChanged);
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Rafraîchit la liste des datasets
        /// </summary>
        public void RefreshDatasetList()
        {
            if (dropdown == null)
            {
                Debug.LogError("[DatasetDropdown] Dropdown reference is missing!");
                return;
            }
            
            // Nettoyer l'ancien événement
            dropdown.onValueChanged.RemoveListener(OnDropdownValueChanged);
            
            dropdown.ClearOptions();
            _datasetPaths.Clear();
            _displayNames.Clear();
            _pendingSelectionIndex = -1;
            
            string rootPath = GetRootPath();
            
            if (!Directory.Exists(rootPath))
            {
                Debug.LogWarning($"[DatasetDropdown] Directory not found: {rootPath}");
                dropdown.AddOptions(new List<string> { "No datasets found" });
                dropdown.interactable = false;
                return;
            }
            
            _datasetPaths = FindMatchingFolders(rootPath);
            
            if (_datasetPaths.Count == 0)
            {
                dropdown.AddOptions(new List<string> { "No datasets found" });
                dropdown.interactable = false;
                
                if (logEvents)
                    Debug.LogWarning($"[DatasetDropdown] No valid datasets found in: {rootPath}");
                return;
            }
            
            // Créer les noms d'affichage
            _displayNames = _datasetPaths
                .Select(fullPath => GetRelativePathFromRoot(fullPath, rootPath))
                .Select(path => path.Replace("\\", "/"))
                .ToList();
            
            dropdown.AddOptions(_displayNames);
            dropdown.interactable = true;
            
            // Ajouter l'événement pour détecter les changements
            dropdown.onValueChanged.AddListener(OnDropdownValueChanged);
            
            // Sélectionner le premier par défaut
            if (_datasetPaths.Count > 0 && string.IsNullOrEmpty(_currentSelectedPath))
            {
                _pendingSelectionIndex = 0;
                dropdown.value = 0;
                _currentSelectedPath = _datasetPaths[0];
            }
            
            if (logEvents)
            {
                Debug.Log($"[DatasetDropdown] Loaded {_datasetPaths.Count} datasets from: {rootPath}");
                foreach (var name in _displayNames)
                {
                    Debug.Log($"  - {name}");
                }
            }
        }
        
        /// <summary>
        /// Callback appelé quand l'utilisateur change la sélection dans le dropdown
        /// </summary>
        private void OnDropdownValueChanged(int index)
        {
            if (index < 0 || index >= _datasetPaths.Count)
            {
                Debug.LogWarning($"[DatasetDropdown] Invalid index: {index}");
                return;
            }
            
            _pendingSelectionIndex = index;
            
            if (logEvents)
            {
                Debug.Log($"[DatasetDropdown] Pending selection changed to: {_displayNames[index]}");
            }
        }
        
        /// <summary>
        /// Applique la sélection en attente
        /// </summary>
        public void ApplySelection()
        {
            if (_pendingSelectionIndex >= 0 && _pendingSelectionIndex < _datasetPaths.Count)
            {
                _currentSelectedPath = _datasetPaths[_pendingSelectionIndex];
                string relativePath = GetRelativePathFromStreamingAssets(_currentSelectedPath);
                
                OnDatasetSelected?.Invoke(_pendingSelectionIndex, relativePath);
                
                if (logEvents)
                {
                    Debug.Log($"[DatasetDropdown] Applied: {_displayNames[_pendingSelectionIndex]} -> {relativePath}");
                }
            }
            else
            {
                Debug.LogWarning("[DatasetDropdown] No pending selection to apply");
            }
        }
        
        /// <summary>
        /// Retourne le chemin du dataset en attente
        /// </summary>
        public string GetPendingPath()
        {
            if (_pendingSelectionIndex >= 0 && _pendingSelectionIndex < _datasetPaths.Count)
            {
                string fullPath = _datasetPaths[_pendingSelectionIndex];
                string relativePath = GetRelativePathFromStreamingAssets(fullPath);
                
                if (logEvents)
                {
                    Debug.Log($"[DatasetDropdown] GetPendingPath: {relativePath}");
                }
                
                return relativePath;
            }
            
            Debug.LogWarning($"[DatasetDropdown] No pending selection. Index: {_pendingSelectionIndex}, Count: {_datasetPaths.Count}");
            return null;
        }
        
        /// <summary>
        /// Définit le chemin racine des datasets
        /// </summary>
        public void SetRootPath(string path)
        {
            folderPathRelative = path;
            RefreshDatasetList();
        }
        
        /// <summary>
        /// Définit la référence du dropdown
        /// </summary>
        public void SetDropdown(TMP_Dropdown newDropdown)
        {
            if (dropdown != null)
                dropdown.onValueChanged.RemoveListener(OnDropdownValueChanged);
            
            dropdown = newDropdown;
            
            if (dropdown != null)
            {
                dropdown.onValueChanged.AddListener(OnDropdownValueChanged);
            }
            
            if (autoLoadOnStart)
            {
                RefreshDatasetList();
            }
        }
        
        #endregion
        
        #region Private Methods
        
        private string GetRootPath()
        {
            return Path.Combine(Application.streamingAssetsPath, folderPathRelative.Replace("Assets/", ""));
        }
        
        private string GetRelativePathFromRoot(string fullPath, string rootPath)
        {
            if (!fullPath.StartsWith(rootPath))
                return fullPath;
            
            string relative = fullPath.Substring(rootPath.Length);
            return relative.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        
        private string GetRelativePathFromStreamingAssets(string fullPath)
        {
            string streamingPath = Application.streamingAssetsPath;
            if (!fullPath.StartsWith(streamingPath))
                return fullPath;
            
            string relative = fullPath.Substring(streamingPath.Length);
            return relative.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        
        /// <summary>
        /// Recherche récursive des dossiers contenant /png et /json
        /// </summary>
        private List<string> FindMatchingFolders(string root)
        {
            List<string> results = new List<string>();
            
            try
            {
                foreach (string dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
                {
                    string pngPath = Path.Combine(dir, "png");
                    string jsonPath = Path.Combine(dir, "json");
                    
                    if (Directory.Exists(pngPath) && Directory.Exists(jsonPath))
                    {
                        results.Add(dir);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[DatasetDropdown] Error scanning directories: {e.Message}");
            }
            
            return results;
        }
        
        #endregion
        
        #region Editor Utilities
        
        [ContextMenu("Refresh Dataset List")]
        private void EditorRefresh()
        {
            RefreshDatasetList();
        }
        
        [ContextMenu("Log Current Selection")]
        private void EditorLogSelection()
        {
            Debug.Log($"[DatasetDropdown] Current selection: {_currentSelectedPath}");
            Debug.Log($"[DatasetDropdown] Pending selection index: {_pendingSelectionIndex}");
            Debug.Log($"[DatasetDropdown] Pending selection path: {PendingSelectionPath}");
        }
        
        [ContextMenu("Apply Selection")]
        private void EditorApplySelection()
        {
            ApplySelection();
        }
        
        #endregion
    }
}