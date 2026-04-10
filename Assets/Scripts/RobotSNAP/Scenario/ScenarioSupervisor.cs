// Scripts/RobotSNAP/Core/Scenario/ScenarioSupervisor.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotSNAP.Core.Scenario
{
    public sealed class ScenarioSupervisor : MonoBehaviour
    {
        [Header("Core References")]
        [SerializeField] private ScenarioLoader _scenarioLoader;
        [SerializeField] private EnvironmentManager _environmentManager;

        [Header("Settings")]
        [SerializeField] private string _defaultScenarioName = "default";
        [SerializeField] private bool _autoApplyAfterLoad = true;
        [SerializeField] private bool _autoApplyOnEnvironmentCreated = true;
        [SerializeField] private float _applyDelay = 0.5f;
        [SerializeField] private bool _logEvents = true;

        [Header("Fallback")]
        [SerializeField] private ScenarioData _fallbackScenario;

        private string _currentScenarioId;
        private ScenarioData _currentScenarioData;
        private readonly Dictionary<int, ScenarioApplier> _scenarioAppliers = new();
        private bool _isApplying;
        private Coroutine _pendingApplyCoroutine;

        public string CurrentScenarioId => _currentScenarioId;
        public ScenarioData CurrentScenarioData => _currentScenarioData;
        public bool HasScenarioLoaded => _currentScenarioData != null;
        public bool HasDefaultScenario => !string.IsNullOrEmpty(_defaultScenarioName) || _fallbackScenario != null;
        public bool IsApplying => _isApplying;

        public event Action<ScenarioData> OnScenarioLoaded;
        public event Action<int, ScenarioData> OnScenarioAppliedToEnvironment;
        public event Action<ScenarioData> OnScenarioAppliedToAll;
        public event Action<string> OnScenarioError;
        public event Action<ScenarioData> OnScenarioApplying;
        public event Action<ScenarioData> OnScenarioApplied;

        private void Awake()
        {
            EnsureDependencies();
        }

        private void Start()
        {
            if (_autoApplyAfterLoad && HasDefaultScenario)
            {
                LoadDefaultScenario();
            }
        }

        private void OnDestroy()
        {
            CancelPendingApply();
            ClearAppliers();
        }

        private void EnsureDependencies()
        {
            if (_scenarioLoader == null)
            {
                _scenarioLoader = GetComponent<ScenarioLoader>();
                if (_scenarioLoader == null)
                {
                    _scenarioLoader = gameObject.AddComponent<ScenarioLoader>();
                    if (_logEvents) Debug.Log("[ScenarioSupervisor] Created ScenarioLoader");
                }
            }

            if (_environmentManager == null)
            {
                var supervisor = Supervisor.Instance;
                if (supervisor != null)
                {
                    var envManagerField = typeof(Supervisor).GetField("_environmentManager", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (envManagerField != null)
                    {
                        _environmentManager = envManagerField.GetValue(supervisor) as EnvironmentManager;
                    }
                }
                
                if (_environmentManager == null)
                {
                    _environmentManager = FindObjectOfType<EnvironmentManager>();
                }
            }

            if (_scenarioLoader != null)
            {
                _scenarioLoader.OnScenarioLoaded += OnScenarioLoadedInternal;
                _scenarioLoader.OnScenarioError += OnScenarioErrorInternal;
            }

            if (_environmentManager != null)
            {
                _environmentManager.OnEnvironmentCreated += OnEnvironmentCreated;
            }
        }

        private void OnScenarioLoadedInternal(ScenarioData scenario)
        {
            _currentScenarioData = scenario;
            OnScenarioLoaded?.Invoke(scenario);
            
            if (_logEvents)
            {
                Debug.Log($"[ScenarioSupervisor] Scenario loaded: {scenario.Name}");
            }
        }

        private void OnScenarioErrorInternal(string error)
        {
            OnScenarioError?.Invoke(error);
            Debug.LogError($"[ScenarioSupervisor] Error: {error}");
        }

        private void OnEnvironmentCreated(int environmentIndex)
        {
            if (_autoApplyOnEnvironmentCreated && _currentScenarioData != null)
            {
                ApplyScenarioToEnvironment(environmentIndex, _currentScenarioData);
            }
        }

        private void CancelPendingApply()
        {
            if (_pendingApplyCoroutine != null)
            {
                StopCoroutine(_pendingApplyCoroutine);
                _pendingApplyCoroutine = null;
            }
        }

        private void ClearAppliers()
        {
            foreach (var applier in _scenarioAppliers.Values)
            {
                if (applier != null && applier.gameObject != null)
                {
                    Destroy(applier);
                }
            }
            _scenarioAppliers.Clear();
        }

        private IEnumerator ApplyScenarioWithDelay(ScenarioData scenario)
        {
            _isApplying = true;
            
            if (_applyDelay > 0)
            {
                yield return new WaitForSeconds(_applyDelay);
            }
            
            ApplyScenarioToAllEnvironmentsInternal(scenario);
            
            _isApplying = false;
            _pendingApplyCoroutine = null;
        }

        private void ApplyScenarioToAllEnvironmentsInternal(ScenarioData scenario)
        {
            if (_environmentManager == null)
            {
                OnScenarioError?.Invoke("EnvironmentManager is null");
                return;
            }

            var gameManagers = _environmentManager.GetAllGameManagers();
            
            if (gameManagers == null || gameManagers.Count == 0)
            {
                if (_logEvents) Debug.LogWarning("[ScenarioSupervisor] No environments to apply scenario");
                return;
            }

            OnScenarioApplying?.Invoke(scenario);

            for (int i = 0; i < gameManagers.Count; i++)
            {
                ApplyScenarioToEnvironment(i, scenario);
            }

            OnScenarioAppliedToAll?.Invoke(scenario);
            OnScenarioApplied?.Invoke(scenario);

            if (_logEvents)
            {
                Debug.Log($"[ScenarioSupervisor] Scenario applied to {gameManagers.Count} environments: {scenario.Name}");
            }
        }

        #region Public API - Loading

        public bool LoadScenario(string scenarioName)
        {
            if (_scenarioLoader == null)
            {
                OnScenarioError?.Invoke("ScenarioLoader is null");
                return false;
            }

            var scenario = _scenarioLoader.LoadScenario(scenarioName);
            
            if (scenario != null)
            {
                _currentScenarioId = scenarioName;
                _currentScenarioData = scenario;
                
                if (_autoApplyAfterLoad)
                {
                    ApplyLoadedScenarioToAll();
                }
                
                return true;
            }
            
            return false;
        }

        public bool LoadDefaultScenario()
        {
            if (!string.IsNullOrEmpty(_defaultScenarioName))
            {
                if (LoadScenario(_defaultScenarioName))
                {
                    return true;
                }
            }
            
            if (_fallbackScenario != null)
            {
                _currentScenarioData = _fallbackScenario;
                _currentScenarioId = _fallbackScenario.Name;
                
                OnScenarioLoaded?.Invoke(_fallbackScenario);
                
                if (_autoApplyAfterLoad)
                {
                    ApplyLoadedScenarioToAll();
                }
                
                if (_logEvents)
                {
                    Debug.Log($"[ScenarioSupervisor] Using fallback scenario: {_fallbackScenario.Name}");
                }
                
                return true;
            }
            
            OnScenarioError?.Invoke("No default scenario available");
            return false;
        }

        #endregion

        #region Public API - Application

        public void ApplyLoadedScenarioToAll()
        {
            if (_currentScenarioData == null)
            {
                OnScenarioError?.Invoke("No scenario loaded");
                return;
            }
            
            CancelPendingApply();
            _pendingApplyCoroutine = StartCoroutine(ApplyScenarioWithDelay(_currentScenarioData));
        }

        public void ApplyScenarioToAll(ScenarioData scenario)
        {
            if (scenario == null)
            {
                OnScenarioError?.Invoke("Cannot apply null scenario");
                return;
            }
            
            _currentScenarioData = scenario;
            ApplyLoadedScenarioToAll();
        }

        public void ApplyScenarioToEnvironment(int environmentIndex, ScenarioData scenario)
        {
            if (scenario == null)
            {
                OnScenarioError?.Invoke("Cannot apply null scenario");
                return;
            }
            
            if (_environmentManager == null)
            {
                OnScenarioError?.Invoke("EnvironmentManager is null");
                return;
            }
            
            var gameManager = _environmentManager.GetGameManager(environmentIndex);
            if (gameManager == null)
            {
                OnScenarioError?.Invoke($"GameManager not found for environment {environmentIndex}");
                return;
            }
            
            if (!_scenarioAppliers.TryGetValue(environmentIndex, out var applier))
            {
                applier = gameManager.gameObject.AddComponent<ScenarioApplier>();
                applier.SetLoader(_scenarioLoader);
                _scenarioAppliers[environmentIndex] = applier;
                
                if (_logEvents)
                {
                    Debug.Log($"[ScenarioSupervisor] Created ScenarioApplier for environment {environmentIndex}");
                }
            }
            
            StartCoroutine(ApplyScenarioCoroutine(applier, gameManager, scenario, environmentIndex));
        }

        private IEnumerator ApplyScenarioCoroutine(ScenarioApplier applier, GameManager gameManager, ScenarioData scenario, int environmentIndex)
        {
            yield return StartCoroutine(applier.ApplyScenario(gameManager, scenario));
            
            OnScenarioAppliedToEnvironment?.Invoke(environmentIndex, scenario);
            
            if (_logEvents)
            {
                Debug.Log($"[ScenarioSupervisor] Scenario applied to environment {environmentIndex}: {scenario.Name}");
            }
        }

        public void ResetAndReapply()
        {
            if (_environmentManager == null)
            {
                OnScenarioError?.Invoke("EnvironmentManager is null");
                return;
            }
            
            _environmentManager.ResetAllEnvironments();
            StartCoroutine(ReapplyAfterReset());
        }

        private IEnumerator ReapplyAfterReset()
        {
            yield return null;
            ApplyLoadedScenarioToAll();
        }

        #endregion

        #region Public API - Queries

        public List<string> GetAvailableScenarios()
        {
            return _scenarioLoader?.GetAvailableScenarios() ?? new List<string>();
        }

        public ScenarioInfo GetScenarioInfo(string scenarioName)
        {
            return _scenarioLoader?.GetScenarioInfo(scenarioName);
        }

        public bool ScenarioExists(string scenarioName)
        {
            var scenarios = GetAvailableScenarios();
            // Utiliser Contains avec StringComparison
            foreach (var s in scenarios)
            {
                if (string.Equals(s, scenarioName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        #endregion

        #region Editor Utilities

        #if UNITY_EDITOR
        
        [ContextMenu("Load Default Scenario")]
        private void EditorLoadDefaultScenario() => LoadDefaultScenario();
        
        [ContextMenu("Apply to All Environments")]
        private void EditorApplyToAll() => ApplyLoadedScenarioToAll();
        
        [ContextMenu("Reset and Reapply")]
        private void EditorResetAndReapply() => ResetAndReapply();
        
        #endif

        #endregion
    }
}