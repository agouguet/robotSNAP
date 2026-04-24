using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections;

namespace RobotSNAP.UI
{
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
        private string _targetPath; // Stocke le chemin cible pour application ultérieure
        
        public event Action<int, string> OnDatasetSelected;
        public TMP_Dropdown Dropdown => dropdown;
        public List<string> DatasetPaths => _datasetPaths;
        public string CurrentSelectedPath => _currentSelectedPath;
        public int PendingSelectionIndex => _pendingSelectionIndex;
        public string PendingSelectionPath => _pendingSelectionIndex >= 0 && _pendingSelectionIndex < _datasetPaths.Count 
            ? _datasetPaths[_pendingSelectionIndex] : null;
        public int DatasetCount => _datasetPaths.Count;
        
        private void Awake()
        {
            if (dropdown == null)
                dropdown = GetComponent<TMP_Dropdown>();
        }
        
        private void Start()
        {
            if (autoLoadOnStart)
                RefreshDatasetList();
        }
        
        private void OnEnable()
        {
            StartCoroutine(ApplyTargetSelectionDelayed());
        }
        
        private void OnDestroy()
        {
            if (dropdown != null)
                dropdown.onValueChanged.RemoveListener(OnDropdownValueChanged);
        }
        
        private IEnumerator ApplyTargetSelectionDelayed()
        {
            yield return null; // attendre un frame pour que l'UI soit prête
            if (string.IsNullOrEmpty(_targetPath) || _datasetPaths.Count == 0)
                yield break;
            
            string normalizedTarget = _targetPath.Replace('\\', '/').Trim('/');
            for (int i = 0; i < _datasetPaths.Count; i++)
            {
                string candidate = _datasetPaths[i];
                string relative = GetRelativePathFromStreamingAssets(candidate);
                if (relative.Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase) ||
                    candidate.EndsWith(normalizedTarget, StringComparison.OrdinalIgnoreCase))
                {
                    if (dropdown.value != i)
                    {
                        dropdown.SetValueWithoutNotify(i);
                        dropdown.RefreshShownValue();
                        if (dropdown.captionText != null && i < _displayNames.Count)
                            dropdown.captionText.text = _displayNames[i];
                    }
                    _pendingSelectionIndex = i;
                    _currentSelectedPath = candidate;
                    if (logEvents)
                        Debug.Log($"[DatasetDropdown] Applied target selection: {relative}");
                    break;
                }
            }
        }
        
        public void RefreshDatasetList()
        {
            if (dropdown == null)
            {
                Debug.LogError("[DatasetDropdown] Dropdown reference is missing!");
                return;
            }
            
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
            
            _displayNames = _datasetPaths
                .Select(fullPath => GetRelativePathFromRoot(fullPath, rootPath))
                .Select(path => path.Replace("\\", "/"))
                .ToList();
            
            dropdown.AddOptions(_displayNames);
            dropdown.interactable = true;
            dropdown.onValueChanged.AddListener(OnDropdownValueChanged);
            
            // Par défaut, sélectionner le premier élément (mais sera écrasé par ApplyTargetSelectionDelayed)
            if (_datasetPaths.Count > 0)
            {
                _pendingSelectionIndex = 0;
                dropdown.SetValueWithoutNotify(0);
                dropdown.RefreshShownValue();
                _currentSelectedPath = _datasetPaths[0];
            }
            
            if (logEvents)
            {
                Debug.Log($"[DatasetDropdown] Loaded {_datasetPaths.Count} datasets from: {rootPath}");
                foreach (var name in _displayNames)
                    Debug.Log($"  - {name}");
            }
        }
        
        private void OnDropdownValueChanged(int index)
        {
            if (index < 0 || index >= _datasetPaths.Count) return;
            _pendingSelectionIndex = index;
            if (logEvents)
                Debug.Log($"[DatasetDropdown] Pending selection changed to: {_displayNames[index]}");
        }
        
        public void ApplySelection()
        {
            if (_pendingSelectionIndex >= 0 && _pendingSelectionIndex < _datasetPaths.Count)
            {
                _currentSelectedPath = _datasetPaths[_pendingSelectionIndex];
                string relativePath = GetRelativePathFromStreamingAssets(_currentSelectedPath);
                OnDatasetSelected?.Invoke(_pendingSelectionIndex, relativePath);
                if (logEvents)
                    Debug.Log($"[DatasetDropdown] Applied: {_displayNames[_pendingSelectionIndex]} -> {relativePath}");
            }
            else
                Debug.LogWarning("[DatasetDropdown] No pending selection to apply");
        }
        
        public string GetPendingPath()
        {
            if (_pendingSelectionIndex >= 0 && _pendingSelectionIndex < _datasetPaths.Count)
            {
                string fullPath = _datasetPaths[_pendingSelectionIndex];
                string relativePath = GetRelativePathFromStreamingAssets(fullPath);
                if (logEvents)
                    Debug.Log($"[DatasetDropdown] GetPendingPath: {relativePath}");
                return relativePath;
            }
            Debug.LogWarning($"[DatasetDropdown] No pending selection. Index: {_pendingSelectionIndex}, Count: {_datasetPaths.Count}");
            return null;
        }
        
        public void SetSelectedPath(string datasetPath, bool applyImmediately = true)
        {
            if (string.IsNullOrEmpty(datasetPath) || _datasetPaths.Count == 0) return;

            string normalizedTarget = datasetPath.Replace('\\', '/').Trim('/').ToLowerInvariant();

            for (int i = 0; i < _datasetPaths.Count; i++)
            {
                string candidate = _datasetPaths[i];
                string relative = GetRelativePathFromStreamingAssets(candidate).Replace('\\', '/').ToLowerInvariant();
                if (relative == normalizedTarget || candidate.Replace('\\', '/').ToLowerInvariant().EndsWith(normalizedTarget))
                {
                    _pendingSelectionIndex = i;
                    _targetPath = datasetPath;
                    if (applyImmediately)
                    {
                        dropdown.SetValueWithoutNotify(i);
                        dropdown.RefreshShownValue();
                        if (dropdown.captionText != null && i < _displayNames.Count)
                            dropdown.captionText.text = _displayNames[i];
                        _currentSelectedPath = candidate;
                    }
                    return;
                }
            }
            Debug.LogWarning($"[DatasetDropdown] Path not found: {datasetPath}");
        }
        
        public void SetRootPath(string path)
        {
            folderPathRelative = path;
            RefreshDatasetList();
        }
        
        public void SetDropdown(TMP_Dropdown newDropdown)
        {
            if (dropdown != null)
                dropdown.onValueChanged.RemoveListener(OnDropdownValueChanged);
            dropdown = newDropdown;
            if (dropdown != null)
                dropdown.onValueChanged.AddListener(OnDropdownValueChanged);
            if (autoLoadOnStart)
                RefreshDatasetList();
        }
        
        private string GetRootPath() => Path.Combine(Application.streamingAssetsPath, folderPathRelative.Replace("Assets/", ""));
        
        private string GetRelativePathFromRoot(string fullPath, string rootPath) =>
            fullPath.StartsWith(rootPath) ? fullPath.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : fullPath;
        
        private string GetRelativePathFromStreamingAssets(string fullPath)
        {
            string streamingPath = Application.streamingAssetsPath;
            return fullPath.StartsWith(streamingPath) ? fullPath.Substring(streamingPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : fullPath;
        }
        
        private List<string> FindMatchingFolders(string root)
        {
            List<string> results = new List<string>();
            try
            {
                foreach (string dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
                {
                    if (Directory.Exists(Path.Combine(dir, "png")) && Directory.Exists(Path.Combine(dir, "json")))
                        results.Add(dir);
                }
            }
            catch (Exception e) { Debug.LogError($"[DatasetDropdown] Error scanning directories: {e.Message}"); }
            return results;
        }
        
        [ContextMenu("Refresh Dataset List")] private void EditorRefresh() => RefreshDatasetList();
        [ContextMenu("Log Current Selection")] private void EditorLogSelection() => Debug.Log($"[DatasetDropdown] Current: {_currentSelectedPath}, Pending: {_pendingSelectionIndex}, Target: {_targetPath}");
        [ContextMenu("Apply Selection")] private void EditorApplySelection() => ApplySelection();
    }
}