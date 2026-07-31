using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotSNAP.Core
{
    /// <summary>
    /// Gère la création, destruction et gestion des environnements.
    /// </summary>
    public class EnvironmentManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameObject _environmentPrefab;
        [SerializeField] private Transform _environmentsParent;
        
        [Header("Settings")]
        [SerializeField] private bool _logEvents = true;
        
        // Events (C# classiques conservés pour compatibilité, mais peuvent être remplacés par EventBus)
        public event Action<int> OnEnvironmentCreated;
        public event Action OnEnvironmentsCleared;
        public event Action<int> OnEnvironmentDestroyed;
        
        // State
        private List<GameObject> _environmentInstances = new();
        private List<GameManager> _gameManagers = new();
        private float _currentSpacing;
        
        // Properties
        public IReadOnlyList<GameObject> Environments => _environmentInstances;
        public IReadOnlyList<GameManager> GameManagers => _gameManagers;
        public int EnvironmentCount => _environmentInstances.Count;
        public float CurrentSpacing => _currentSpacing;
        
        private void Awake()
        {
            if (_environmentsParent == null)
                _environmentsParent = new GameObject("Environments").transform;
        }

        #region Environment Creation
        
        /// <summary>
        /// Crée tous les environnements selon la configuration.
        /// </summary>
        public void CreateAllEnvironments(SimulationConfig config)
        {
            if (_environmentPrefab == null)
            {
                Debug.LogError("[EnvironmentManager] Environment prefab not assigned!");
                return;
            }
            
            if (config == null)
            {
                Debug.LogError("[EnvironmentManager] Config is null!");
                return;
            }
            
            ClearAllEnvironments();
            _currentSpacing = config.EnvironmentSpacing;
            
            for (int i = 0; i < config.EnvironmentCount; i++)
            {
                CreateEnvironment(i, config);
            }
            
            if (_logEvents)
                Debug.Log($"[EnvironmentManager] Created {config.EnvironmentCount} environments");
        }
        
        private void CreateEnvironment(int index, SimulationConfig config)
        {
            Vector3 position = new Vector3(index * config.EnvironmentSpacing, 0, 0);
            GameObject instance = Instantiate(_environmentPrefab, position, Quaternion.identity, _environmentsParent);
            instance.name = $"Environment_{index}";
            
            var gameManager = instance.GetComponent<GameManager>();
            if (gameManager != null)
            {
                gameManager.SetEnvironmentId(index);
                gameManager.ApplyConfig(config);
                
                // Plus besoin de gérer le préfixe ROS ici, EnvROS le fera lui-même.
                _gameManagers.Add(gameManager);
            }
            
            _environmentInstances.Add(instance);
            OnEnvironmentCreated?.Invoke(index);
        }
        
        #endregion
        
        #region Environment Destruction
        
        public void ClearAllEnvironments(bool immediate = false)
        {
            if (_environmentInstances.Count == 0) return;

            foreach (GameObject env in _environmentInstances)
            {
                if (env != null)
                {
                    if (immediate || !Application.isPlaying)
                        DestroyImmediate(env);
                    else
                        Destroy(env);
                }
            }

            _environmentInstances.Clear();
            _gameManagers.Clear();
            OnEnvironmentsCleared?.Invoke();

            if (_logEvents)
                Debug.Log("[EnvironmentManager] Cleared all environments");
        }

        public void ClearAllEnvironmentsImmediate()
        {
            if (_environmentInstances.Count == 0) return;
            
            foreach (var env in _environmentInstances)
            {
                if (env != null)
                    DestroyImmediate(env);
            }
            
            _environmentInstances.Clear();
            _gameManagers.Clear();
            
            if (_logEvents)
                Debug.Log("[EnvironmentManager] Immediate clear of all environments");
        }
        
        public void DestroyEnvironment(int index)
        {
            if (index < 0 || index >= _environmentInstances.Count) return;
            
            var env = _environmentInstances[index];
            if (env != null)
            {
                if (Application.isPlaying)
                    Destroy(env);
                else
                    DestroyImmediate(env);
            }
            
            _environmentInstances.RemoveAt(index);
            _gameManagers.RemoveAt(index);
            OnEnvironmentDestroyed?.Invoke(index);
            
            if (_logEvents)
                Debug.Log($"[EnvironmentManager] Destroyed environment {index}");
        }
        
        #endregion
        
        #region Environment Reset
        
        public void ResetAllEnvironments()
        {
            foreach (var gm in _gameManagers)
                gm?.EditorReset();
            
            if (_logEvents)
                Debug.Log("[EnvironmentManager] Reset all environments");
        }
        
        public void ResetEnvironment(int index)
        {
            var gm = GetGameManager(index);
            gm?.EditorReset();
            if (_logEvents && gm != null)
                Debug.Log($"[EnvironmentManager] Reset environment {index}");
        }
        
        #endregion
        
        #region Configuration
        
        public void ApplyConfigToAll(SimulationConfig config)
        {
            if (config == null) return;
            foreach (var gm in _gameManagers)
                gm?.ApplyConfig(config);
            
            if (_logEvents)
                Debug.Log($"[EnvironmentManager] Applied config to {_gameManagers.Count} environments");
        }
        
        public void ApplyConfigToEnvironment(int index, SimulationConfig config)
        {
            var gm = GetGameManager(index);
            if (gm != null && config != null)
            {
                gm.ApplyConfig(config);
                if (_logEvents)
                    Debug.Log($"[EnvironmentManager] Applied config to environment {index}");
            }
        }
        
        #endregion
        
        #region Queries
        
        public GameManager GetGameManager(int index)
        {
            if (index < 0 || index >= _gameManagers.Count)
                return null;
            return _gameManagers[index];
        }
        
        public List<GameManager> GetAllGameManagers() => new List<GameManager>(_gameManagers);
        
        public GameObject GetEnvironment(int index)
        {
            if (index < 0 || index >= _environmentInstances.Count)
                return null;
            return _environmentInstances[index];
        }
        
        public IEnumerator WaitForAllEnvironmentsReady()
        {
            bool allReady;
            do
            {
                allReady = true;
                foreach (var gm in _gameManagers)
                {
                    if (gm != null && !gm.IsInitialized)
                    {
                        allReady = false;
                        break;
                    }
                }
                yield return null;
            } while (!allReady);
            
            if (_logEvents)
                Debug.Log("[EnvironmentManager] All environments ready");
        }
        
        #endregion
        
        #region Rebuild
        
        public void RebuildAllEnvironments(SimulationConfig config)
        {
            CreateAllEnvironments(config);
        }
        
        public void RebuildEnvironment(int index, SimulationConfig config)
        {
            if (index < 0 || index >= _environmentInstances.Count) return;
            
            DestroyEnvironment(index);
            
            // Re-créer à la même position
            Vector3 position = new Vector3(index * config.EnvironmentSpacing, 0, 0);
            GameObject instance = Instantiate(_environmentPrefab, position, Quaternion.identity, _environmentsParent);
            instance.name = $"Environment_{index}";
            
            var gm = instance.GetComponent<GameManager>();
            if (gm != null)
            {
                gm.SetEnvironmentId(index);
                gm.ApplyConfig(config);
                // Plus de SetROSPrefix
                _gameManagers.Insert(index, gm);
            }
            _environmentInstances.Insert(index, instance);
            OnEnvironmentCreated?.Invoke(index);
            
            if (_logEvents)
                Debug.Log($"[EnvironmentManager] Rebuilt environment {index}");
        }
        
        #endregion
        
        #region Editor Utilities
        
#if UNITY_EDITOR
        [ContextMenu("Log Environment Status")]
        private void EditorLogStatus()
        {
            Debug.Log($"[EnvironmentManager] Status:\n" +
                      $"  Environments: {_environmentInstances.Count}\n" +
                      $"  GameManagers: {_gameManagers.Count}\n" +
                      $"  Current Spacing: {_currentSpacing}");
            // Le commentaire sur IsScenarioApplied a été supprimé car il n'existe plus
        }
#endif
        
        #endregion
    }
}