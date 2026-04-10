// Scripts/RobotSNAP/Core/GameManager.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Simulation;
using RobotSNAP.ROS;
using RobotSNAP.Environment;
using RobotSNAP.Core.Scenario;
using RobotSNAP.Human;

namespace RobotSNAP.Core
{
    /// <summary>
    /// Orchestrateur principal - Coordonne l'initialisation d'un environnement
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Core Components")]
        [SerializeField] private EnvironmentBuilder _environmentBuilder;
        [SerializeField] private NavMeshManager _navMeshManager;
        [SerializeField] private SpawnCoordinator _spawnCoordinator;
        [SerializeField] private HumanPoolManager _humanPool;
        [SerializeField] private EnvROS _envROS;
        [SerializeField] private EnvironmentCreatorFromDataset _datasetCreator;

        [Header("Scenario")]
        [Tooltip("Auto-apply scenario after initialization")]
        [SerializeField] private bool _autoApplyScenario = true;

        #endregion

        #region Private Fields

        private int _environmentId;
        private SimulationConfig _currentConfig;
        private ScenarioData _currentScenario;
        private bool _isInitialized;
        private bool _isResetting = false;
        private Coroutine _currentResetCoroutine = null;
        private bool _isScenarioApplied = false;

        #endregion

        #region Properties

        public int EnvironmentId => _environmentId;
        public bool IsInitialized => _isInitialized;
        public bool IsScenarioApplied => _isScenarioApplied;
        public ScenarioData CurrentScenario => _currentScenario;
        public SimulationConfig CurrentConfig => _currentConfig;

        // Accessors for ScenarioApplier
        public SpawnCoordinator SpawnCoordinator => _spawnCoordinator;
        public NavMeshManager NavMeshManager => _navMeshManager;
        public EnvironmentBuilder EnvironmentBuilder => _environmentBuilder;
        public HumanPoolManager HumanPool => _humanPool;
        public EnvROS EnvROS => _envROS;
        public EnvironmentCreatorFromDataset DatasetCreator => _datasetCreator;

        #endregion

        #region Events

        public event Action<int> OnInitialized;
        public event Action<int> OnResetStarted;
        public event Action<int> OnResetCompleted;
        public event Action<int, ScenarioData> OnScenarioApplied;
        public event Action<int, string> OnError;

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            StartCoroutine(InitializeGame());
        }

        private void OnDestroy()
        {
            if (_envROS != null)
            {
                _envROS.OnResetRequested -= OnResetRequested;
                _envROS.OnPlayStateChanged -= OnPlayStateChanged;
            }
        }

        #endregion

        #region Initialization

        private IEnumerator InitializeGame()
        {
            Debug.Log($"[GameManager:{_environmentId}] Initializing...");
            
            // Setup ROS (inchangé)
            if (_envROS != null)
            {
                _envROS.Initialize("");
                _envROS.RegisterResetService(OnResetRequest);
                _envROS.RegisterPausePlayService(OnPlayRequest);
                _envROS.OnResetRequested += OnResetRequested;
                _envROS.OnPlayStateChanged += OnPlayStateChanged;
            }
            
            // 1. Build environment (charger la map depuis le dataset ou la map par défaut)
            if (_environmentBuilder != null)
            {
                // Si un scénario avec map est défini, charger cette map
                if (_currentScenario != null && !string.IsNullOrEmpty(_currentScenario.MapImage))
                {
                    yield return StartCoroutine(_environmentBuilder.BuildEnvironmentWithMap(_currentScenario.MapImage));
                }
                else
                {
                    yield return StartCoroutine(_environmentBuilder.BuildEnvironment());
                }
            }
            
            // 2. Build NavMeshes
            if (_navMeshManager != null)
            {
                yield return StartCoroutine(_navMeshManager.BuildNavMeshes());
            }
            
            // 3. Prewarm human pool
            if (_humanPool != null)
            {
                yield return StartCoroutine(_humanPool.Prewarm(20));
            }
            
            // 4. Initial spawn (positions par défaut)
            if (_spawnCoordinator != null)
            {
                yield return StartCoroutine(_spawnCoordinator.SpawnAll());
            }
            
            _isInitialized = true;
            OnInitialized?.Invoke(_environmentId);
            
            Debug.Log($"[GameManager:{_environmentId}] Initialization complete");
            
            // 5. Apply scenario if set (repositionne les agents selon le YAML)
            if (_autoApplyScenario && _currentScenario != null)
            {
                yield return StartCoroutine(ApplyScenarioInternal());
            }
        }

        #endregion

        #region Public API - Configuration

        public void SetEnvironmentId(int id)
        {
            _environmentId = id;
        }

        public void ApplyConfig(SimulationConfig config)
        {
            if (config == null) return;

            _currentConfig = config;

            // Apply to SpawnCoordinator
            if (_spawnCoordinator != null)
            {
                _spawnCoordinator.MinHumans = config.MinHumans;
                _spawnCoordinator.MaxHumans = config.MaxHumans;
                _spawnCoordinator.MinAgentDistance = config.MinAgentDistance;
                _spawnCoordinator.MinPathLength = config.MinPathLength;
                _spawnCoordinator.MaxPathLength = config.MaxPathLength;
            }

            // Apply to DatasetCreator
            if (_datasetCreator != null && !string.IsNullOrEmpty(config.DatasetPath))
            {
                _datasetCreator.FolderPath = config.DatasetPath;
            }

            // Apply ROS prefix
            if (_envROS != null && !string.IsNullOrEmpty(config.RosPrefix))
            {
                _envROS.UpdatePrefix(config.RosPrefix);
            }

            Debug.Log($"[GameManager:{_environmentId}] Config applied");
        }

        public void SetROSPrefix(string prefix)
        {
            _envROS?.UpdatePrefix(prefix);
        }

        #endregion

        #region Public API - Scenario Management

        /// <summary>
        /// Sets the current scenario to be applied
        /// </summary>
        public void SetScenario(ScenarioData scenario)
        {
            _currentScenario = scenario;
            _isScenarioApplied = false;
        }

        /// <summary>
        /// Applies the current scenario to the environment
        /// </summary>
        public void ApplyScenario()
        {
            if (_currentScenario == null)
            {
                Debug.LogWarning($"[GameManager:{_environmentId}] No scenario to apply");
                return;
            }

            if (_isResetting)
            {
                Debug.Log($"[GameManager:{_environmentId}] Cannot apply scenario during reset");
                return;
            }

            StartCoroutine(ApplyScenarioInternal());
        }

        /// <summary>
        /// Applies a specific scenario to the environment
        /// </summary>
        public void ApplyScenario(ScenarioData scenario)
        {
            _currentScenario = scenario;
            ApplyScenario();
        }

        /// <summary>
        /// Resets the environment and applies the current scenario
        /// </summary>
        public void ResetAndApplyScenario()
        {
            if (_currentScenario == null)
            {
                Debug.LogWarning($"[GameManager:{_environmentId}] No scenario to apply after reset");
                EditorReset();
                return;
            }

            StartCoroutine(ResetAndApplyCoroutine());
        }

        private IEnumerator ResetAndApplyCoroutine()
        {
            yield return StartCoroutine(ResetSequence());
            yield return StartCoroutine(ApplyScenarioInternal());
        }

        private IEnumerator ApplyScenarioInternal()
        {
            if (_currentScenario == null) yield break;

            Debug.Log($"[GameManager:{_environmentId}] Applying scenario: {_currentScenario.Name}");

            // Wait for environment to be ready
            yield return StartCoroutine(WaitForReady());

            // Find or create ScenarioApplier
            var applier = GetComponent<ScenarioApplier>();
            if (applier == null)
            {
                applier = gameObject.AddComponent<ScenarioApplier>();
            }

            // Apply the scenario
            yield return StartCoroutine(applier.ApplyScenario(this, _currentScenario));

            _isScenarioApplied = true;
            OnScenarioApplied?.Invoke(_environmentId, _currentScenario);

            Debug.Log($"[GameManager:{_environmentId}] Scenario applied: {_currentScenario.Name}");
        }

        private IEnumerator WaitForReady()
        {
            // Wait for initialization
            while (!_isInitialized)
            {
                yield return new WaitForSeconds(0.1f);
            }

            // Wait for any ongoing reset
            while (_isResetting)
            {
                yield return new WaitForSeconds(0.1f);
            }

            // Small delay for stability
            yield return new WaitForSeconds(0.1f);
        }

        #endregion

        #region Public API - Reset

        [ContextMenu("Reset Environment")]
        public void EditorReset()
        {
            if (!_isInitialized)
            {
                Debug.LogWarning($"[GameManager:{_environmentId}] Not initialized yet");
                return;
            }

            RequestReset();
        }

        /// <summary>
        /// Resets the environment without reapplying scenario
        /// </summary>
        public void ResetOnly()
        {
            if (!_isInitialized) return;
            RequestReset();
        }

        #endregion

        #region ROS Callbacks

        private ResetResponse OnResetRequest(ResetRequest request)
        {
            Debug.Log($"[GameManager:{_environmentId}] Reset requested with dataset: {request.dataset}");

            if (!string.IsNullOrEmpty(request.dataset) && _datasetCreator != null)
            {
                _datasetCreator.FolderPath = request.dataset;
                if (_currentConfig != null)
                {
                    _currentConfig.DatasetPath = request.dataset;
                }
            }

            if (_isInitialized && !_isResetting)
            {
                RequestReset();
            }

            return new ResetResponse { success = true };
        }

        private PausePlayResponse OnPlayRequest(PausePlayRequest request)
        {
            Debug.Log($"[GameManager:{_environmentId}] Play state changed: {request.play}");
            return new PausePlayResponse { success = true };
        }

        private void OnResetRequested(ResetRequest request)
        {
            Debug.Log($"[GameManager:{_environmentId}] Reset requested event received");
        }

        private void OnPlayStateChanged(bool isPlaying)
        {
            EventBus.Instance.Publish(new PlayStateChangedEvent { isPlaying = isPlaying });
        }

        #endregion

        #region Reset Sequence

        private void RequestReset()
        {
            if (_isResetting)
            {
                Debug.Log($"[GameManager:{_environmentId}] Reset already in progress, ignoring...");
                return;
            }

            if (_currentResetCoroutine != null)
            {
                StopCoroutine(_currentResetCoroutine);
            }

            _currentResetCoroutine = StartCoroutine(ResetSequence());
        }

        private IEnumerator ResetSequence()
        {
            if (_isResetting) yield break;

            _isResetting = true;
            _isScenarioApplied = false;

            OnResetStarted?.Invoke(_environmentId);
            Debug.Log($"[GameManager:{_environmentId}] Starting reset sequence...");

            // 1. Disable moving agents
            if (_spawnCoordinator != null)
            {
                _spawnCoordinator.SetActive(false);
            }

            // 2. Reset environment builder
            if (_environmentBuilder != null)
            {
                yield return StartCoroutine(_environmentBuilder.Reset());
            }

            // 3. Wait for environment to be fully instantiated
            yield return null;

            // 4. Rebuild NavMeshes
            if (_navMeshManager != null)
            {
                Debug.Log($"[GameManager:{_environmentId}] Rebuilding NavMesh...");
                yield return StartCoroutine(_navMeshManager.Reset());
                
                // Build specific surfaces
                var spawnSurface = _navMeshManager.GetSpawnSurface();
                if (spawnSurface != null)
                {
                    spawnSurface.BuildNavMesh();
                }
                
                var navigationSurface = _navMeshManager.GetNavigationSurface();
                if (navigationSurface != null)
                {
                    navigationSurface.BuildNavMesh();
                }
            }

            // 5. Respawn agents
            if (_spawnCoordinator != null)
            {
                yield return StartCoroutine(_spawnCoordinator.SpawnAll());
            }

            _isResetting = false;
            _currentResetCoroutine = null;

            OnResetCompleted?.Invoke(_environmentId);
            Debug.Log($"[GameManager:{_environmentId}] Reset complete");
        }

        #endregion

        #region Public API - Agent Management

        /// <summary>
        /// Gets all humans currently in the environment
        /// </summary>
        public List<HumanAvatar> GetAllHumans()
        {
            var humans = new List<HumanAvatar>();
            if (_humanPool != null)
            {
                // Convertir les GameObjects en HumanAvatar
                foreach (var humanGO in _humanPool.GetActiveHumans())
                {
                    var human = humanGO.GetComponent<HumanAvatar>();
                    if (human != null)
                        humans.Add(human);
                }
            }
            return humans;
        }

        /// <summary>
        /// Gets the robot in this environment
        /// </summary>
        public Robot GetRobot()
        {
            return GetComponentInChildren<Robot>();
        }

        /// <summary>
        /// Clears all agents from the environment
        /// </summary>
        public void ClearAllAgents()
        {
            if (_spawnCoordinator != null)
            {
                _spawnCoordinator.ClearAll();
            }
        }

        #endregion

        #region Public API - State Queries

        /// <summary>
        /// Returns true if the environment is ready for scenario application
        /// </summary>
        public bool IsReadyForScenario()
        {
            return _isInitialized && !_isResetting;
        }

        #endregion

        #region Editor Utilities

        #if UNITY_EDITOR

        [ContextMenu("Apply Current Scenario")]
        private void EditorApplyScenario()
        {
            if (_currentScenario != null)
            {
                ApplyScenario();
            }
            else
            {
                Debug.LogWarning("[GameManager] No scenario set");
            }
        }

        [ContextMenu("Reset and Apply Scenario")]
        private void EditorResetAndApply()
        {
            ResetAndApplyScenario();
        }

        [ContextMenu("Log Status")]
        private void EditorLogStatus()
        {
            Debug.Log($"[GameManager:{_environmentId}] Status:\n" +
                    $"  Initialized: {_isInitialized}\n" +
                    $"  Resetting: {_isResetting}\n" +
                    $"  Scenario Applied: {_isScenarioApplied}\n" +
                    $"  Scenario: {(_currentScenario?.Name ?? "none")}\n" +
                    $"  Humans: {(_humanPool?.ActiveCount ?? 0)}");
        }


        #endif

        #endregion
    }
}