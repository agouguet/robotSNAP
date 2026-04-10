// Scripts/RobotSNAP/Core/EnvironmentManager.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RobotSNAP.Core.Scenario;

namespace RobotSNAP.Core
{
    /// <summary>
    /// Gère la création, destruction et gestion des environnements
    /// </summary>
    public class EnvironmentManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameObject _environmentPrefab;
        [SerializeField] private Transform _environmentsParent;
        
        [Header("Settings")]
        [SerializeField] private bool _logEvents = true;
        [SerializeField] private bool _autoApplyScenarioOnCreate = true;
        
        // Events
        public event Action<int> OnEnvironmentCreated;
        public event Action OnEnvironmentsCleared;
        public event Action<int> OnEnvironmentDestroyed;
        public event Action<int, ScenarioData> OnScenarioAppliedToEnvironment;
        
        // State
        private List<GameObject> _environmentInstances = new();
        private List<GameManager> _gameManagers = new();
        private ScenarioData _pendingScenario;
        
        // Properties
        public IReadOnlyList<GameObject> Environments => _environmentInstances;
        public IReadOnlyList<GameManager> GameManagers => _gameManagers;
        public int EnvironmentCount => _environmentInstances.Count;
        public bool HasPendingScenario => _pendingScenario != null;
        
        private void Awake()
        {
            if (_environmentsParent == null)
                _environmentsParent = transform;
        }
        
        #region Environment Creation
        
        /// <summary>
        /// Crée tous les environnements
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
                
                if (config.EnvironmentCount > 1)
                {
                    gameManager.SetROSPrefix($"env_{index}");
                }
                
                // Appliquer le scénario en attente si demandé
                if (_autoApplyScenarioOnCreate && _pendingScenario != null)
                {
                    gameManager.SetScenario(_pendingScenario);
                    gameManager.ApplyScenario();
                }
                
                _gameManagers.Add(gameManager);
            }
            
            _environmentInstances.Add(instance);
            OnEnvironmentCreated?.Invoke(index);
        }
        
        #endregion
        
        #region Environment Destruction
        
        /// <summary>
        /// Supprime tous les environnements
        /// </summary>
        public void ClearAllEnvironments()
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
            
            OnEnvironmentsCleared?.Invoke();
            
            if (_logEvents)
                Debug.Log("[EnvironmentManager] Cleared all environments");
        }
        
        /// <summary>
        /// Supprime un environnement spécifique
        /// </summary>
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
        
        /// <summary>
        /// Réinitialise tous les environnements
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
            
            if (_logEvents)
                Debug.Log("[EnvironmentManager] Reset all environments");
        }
        
        /// <summary>
        /// Réinitialise un environnement spécifique
        /// </summary>
        public void ResetEnvironment(int index)
        {
            var gameManager = GetGameManager(index);
            if (gameManager != null)
            {
                gameManager.EditorReset();
                
                if (_logEvents)
                    Debug.Log($"[EnvironmentManager] Reset environment {index}");
            }
        }
        
        /// <summary>
        /// Réinitialise tous les environnements et réapplique le scénario
        /// </summary>
        public void ResetAllAndReapplyScenario()
        {
            foreach (var gameManager in _gameManagers)
            {
                if (gameManager != null && gameManager.CurrentScenario != null)
                {
                    gameManager.ResetAndApplyScenario();
                }
                else if (gameManager != null && _pendingScenario != null)
                {
                    gameManager.SetScenario(_pendingScenario);
                    gameManager.ResetAndApplyScenario();
                }
                else
                {
                    gameManager?.EditorReset();
                }
            }
            
            if (_logEvents)
                Debug.Log("[EnvironmentManager] Reset all environments and reapplied scenario");
        }
        
        #endregion
        
        #region Configuration
        
        /// <summary>
        /// Applique une config à tous les environnements
        /// </summary>
        public void ApplyConfigToAll(SimulationConfig config)
        {
            if (config == null) return;
            
            foreach (var gameManager in _gameManagers)
            {
                if (gameManager != null)
                {
                    gameManager.ApplyConfig(config);
                }
            }
            
            if (_logEvents)
                Debug.Log($"[EnvironmentManager] Applied config to {_gameManagers.Count} environments");
        }
        
        /// <summary>
        /// Applique une config à un environnement spécifique
        /// </summary>
        public void ApplyConfigToEnvironment(int index, SimulationConfig config)
        {
            var gameManager = GetGameManager(index);
            if (gameManager != null && config != null)
            {
                gameManager.ApplyConfig(config);
                
                if (_logEvents)
                    Debug.Log($"[EnvironmentManager] Applied config to environment {index}");
            }
        }
        
        #endregion
        
        #region Scenario Management
        
        /// <summary>
        /// Définit le scénario à appliquer aux futurs environnements
        /// </summary>
        public void SetPendingScenario(ScenarioData scenario)
        {
            _pendingScenario = scenario;
            
            if (_logEvents)
                Debug.Log($"[EnvironmentManager] Pending scenario set: {scenario?.Name ?? "null"}");
        }
        
        /// <summary>
        /// Applique un scénario à tous les environnements existants
        /// </summary>
        public void ApplyScenarioToAll(ScenarioData scenario)
        {
            if (scenario == null) return;
            
            foreach (var gameManager in _gameManagers)
            {
                if (gameManager != null)
                {
                    gameManager.SetScenario(scenario);
                    gameManager.ApplyScenario();
                    OnScenarioAppliedToEnvironment?.Invoke(gameManager.EnvironmentId, scenario);
                }
            }
            
            if (_logEvents)
                Debug.Log($"[EnvironmentManager] Applied scenario to {_gameManagers.Count} environments");
        }
        
        /// <summary>
        /// Applique le scénario en attente à tous les environnements
        /// </summary>
        public void ApplyPendingScenarioToAll()
        {
            if (_pendingScenario != null)
            {
                ApplyScenarioToAll(_pendingScenario);
            }
        }
        
        /// <summary>
        /// Applique un scénario à un environnement spécifique
        /// </summary>
        public void ApplyScenarioToEnvironment(int index, ScenarioData scenario)
        {
            var gameManager = GetGameManager(index);
            if (gameManager != null && scenario != null)
            {
                gameManager.SetScenario(scenario);
                gameManager.ApplyScenario();
                OnScenarioAppliedToEnvironment?.Invoke(index, scenario);
                
                if (_logEvents)
                    Debug.Log($"[EnvironmentManager] Applied scenario to environment {index}");
            }
        }
        
        #endregion
        
        #region Queries
        
        /// <summary>
        /// Obtient un GameManager par index
        /// </summary>
        public GameManager GetGameManager(int index)
        {
            if (index < 0 || index >= _gameManagers.Count)
                return null;
            return _gameManagers[index];
        }
        
        /// <summary>
        /// Obtient tous les GameManagers
        /// </summary>
        public List<GameManager> GetAllGameManagers()
        {
            return new List<GameManager>(_gameManagers);
        }
        
        /// <summary>
        /// Obtient l'instance GameObject d'un environnement
        /// </summary>
        public GameObject GetEnvironment(int index)
        {
            if (index < 0 || index >= _environmentInstances.Count)
                return null;
            return _environmentInstances[index];
        }
        
        /// <summary>
        /// Vérifie si un environnement est prêt
        /// </summary>
        public bool IsEnvironmentReady(int index)
        {
            var gameManager = GetGameManager(index);
            return gameManager != null && gameManager.IsInitialized && !gameManager.IsScenarioApplied;
        }
        
        /// <summary>
        /// Attend que tous les environnements soient prêts
        /// </summary>
        public IEnumerator WaitForAllEnvironmentsReady()
        {
            bool allReady = false;
            
            while (!allReady)
            {
                allReady = true;
                foreach (var gameManager in _gameManagers)
                {
                    if (gameManager != null && !gameManager.IsInitialized)
                    {
                        allReady = false;
                        break;
                    }
                }
                yield return null;
            }
            
            if (_logEvents)
                Debug.Log("[EnvironmentManager] All environments ready");
        }
        
        #endregion
        
        #region Rebuild
        
        /// <summary>
        /// Reconstruit tous les environnements
        /// </summary>
        public void RebuildAllEnvironments(SimulationConfig config)
        {
            CreateAllEnvironments(config);
        }
        
        /// <summary>
        /// Reconstruit un environnement spécifique
        /// </summary>
        public void RebuildEnvironment(int index, SimulationConfig config)
        {
            if (index < 0 || index >= _environmentInstances.Count) return;
            
            // Sauvegarder le scénario si existant
            var oldGameManager = _gameManagers[index];
            ScenarioData existingScenario = oldGameManager?.CurrentScenario;
            
            // Détruire l'ancien
            DestroyEnvironment(index);
            
            // Créer un nouvel environnement à la même position
            Vector3 position = new Vector3(index * config.EnvironmentSpacing, 0, 0);
            GameObject instance = Instantiate(_environmentPrefab, position, Quaternion.identity, _environmentsParent);
            instance.name = $"Environment_{index}";
            
            var gameManager = instance.GetComponent<GameManager>();
            if (gameManager != null)
            {
                gameManager.SetEnvironmentId(index);
                gameManager.ApplyConfig(config);
                
                if (config.EnvironmentCount > 1)
                {
                    gameManager.SetROSPrefix($"env_{index}");
                }
                
                // Réappliquer le scénario si existant
                if (existingScenario != null)
                {
                    gameManager.SetScenario(existingScenario);
                    gameManager.ApplyScenario();
                }
                else if (_pendingScenario != null && _autoApplyScenarioOnCreate)
                {
                    gameManager.SetScenario(_pendingScenario);
                    gameManager.ApplyScenario();
                }
                
                // Insérer à la bonne position
                _gameManagers.Insert(index, gameManager);
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
                      $"  Pending Scenario: {(_pendingScenario?.Name ?? "none")}\n" +
                      $"  Auto Apply Scenario: {_autoApplyScenarioOnCreate}");
            
            for (int i = 0; i < _gameManagers.Count; i++)
            {
                var gm = _gameManagers[i];
                if (gm != null)
                {
                    Debug.Log($"  Environment {i}: Initialized={gm.IsInitialized}, " +
                              $"Scenario={gm.CurrentScenario?.Name ?? "none"}, " +
                              $"ScenarioApplied={gm.IsScenarioApplied}");
                }
            }
        }
        
        #endif
        
        #endregion
    }
}