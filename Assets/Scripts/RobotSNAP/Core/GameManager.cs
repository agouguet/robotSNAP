// Scripts/RobotSNAP/Core/GameManager.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Simulation;
using RobotSNAP.ROS;
using RobotSNAP.Environment;
using RobotSNAP.Core.Scenario;
using RobotSNAP.Human;

namespace RobotSNAP.Core
{
    /// <summary>
    /// Gère un environnement unique. Contient les références aux composants nécessaires.
    /// L'initialisation (construction de la carte, NavMesh, pool) est déclenchée par ScenarioManager.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Header("Core Components")]
        [SerializeField] private GridEnvironmentBuilder _gridBuilder;
        [SerializeField] private NavMeshManager _navMeshManager;
        // [SerializeField] private SpawnCoordinator _spawnCoordinator;
        [SerializeField] private HumanPoolManager _humanPool;
        [SerializeField] private EnvROS _envROS;

        private int _environmentId;
        private SimulationConfig _currentConfig;
        private bool _isInitialized;
        private bool _isResetting = false;
        private Coroutine _currentResetCoroutine = null;

        // Properties
        public int EnvironmentId => _environmentId;
        public bool IsInitialized => _isInitialized;
        public GridEnvironmentBuilder GridBuilder => _gridBuilder;
        public NavMeshManager NavMeshManager => _navMeshManager;
        // public SpawnCoordinator SpawnCoordinator => _spawnCoordinator;
        public HumanPoolManager HumanPool => _humanPool;
        public EnvROS EnvROS => _envROS;

        // Events
        public event Action<int> OnInitialized;
        public event Action<int> OnResetStarted;
        public event Action<int> OnResetCompleted;
        public event Action OnScenarioDurationReached;
        public event Action<int, string> OnError;

        #region Unity Lifecycle

        private void Start()
        {
            // L'initialisation est déclenchée par ScenarioManager
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

        #region Public API - Initialization (appelé par ScenarioManager)

        /// <summary>
        /// Initialise l'environnement : NavMesh, pool d'humains, spawn par défaut.
        /// La construction de la carte (GridEnvironmentBuilder) est déjà faite par ScenarioApplier.
        /// </summary>
        public IEnumerator Initialize()
        {
            Debug.Log($"[GameManager:{_environmentId}] Initializing...");

            // Setup ROS
            if (_envROS != null)
            {
                _envROS.Initialize("");
                _envROS.RegisterResetService(OnResetRequest);
                _envROS.RegisterPausePlayService(OnPlayRequest);
                _envROS.OnResetRequested += OnResetRequested;
                _envROS.OnPlayStateChanged += OnPlayStateChanged;
            }

            // 1. Générer les NavMeshes (la carte est déjà construite)
            if (_navMeshManager != null)
            {
                yield return StartCoroutine(_navMeshManager.BuildNavMeshes());
            }

            // 2. Préremplir le pool d'humains
            if (_humanPool != null)
            {
                yield return StartCoroutine(_humanPool.Prewarm(20));
            }

            // 3. Spawn initial des agents (positions par défaut, sera écrasé par le scénario)
            // if (_spawnCoordinator != null)
            // {
            //     yield return StartCoroutine(_spawnCoordinator.SpawnAll());
            // }

            _isInitialized = true;
            OnInitialized?.Invoke(_environmentId);
            Debug.Log($"[GameManager:{_environmentId}] Initialization complete");
        }

        #endregion

        #region Public API - Configuration

        public void SetEnvironmentId(int id) => _environmentId = id;

        public void ApplyConfig(SimulationConfig config)
        {
            if (config == null) return;
            _currentConfig = config;

            if (_envROS != null && !string.IsNullOrEmpty(config.RosPrefix))
                _envROS.UpdatePrefix(config.RosPrefix);

            Debug.Log($"[GameManager:{_environmentId}] Config applied");
        }

        public void NotifyScenarioDurationReached()
        {
            OnScenarioDurationReached?.Invoke();
        }

        public void SetROSPrefix(string prefix) => _envROS?.UpdatePrefix(prefix);

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

        public void ResetEnvironment() => EditorReset();

        #endregion

        #region ROS Callbacks

        private ResetResponse OnResetRequest(ResetRequest request)
        {
            Debug.Log($"[GameManager:{_environmentId}] Reset requested with dataset: {request.dataset}");
            if (_isInitialized && !_isResetting) RequestReset();
            return new ResetResponse { success = true };
        }

        private PausePlayResponse OnPlayRequest(PausePlayRequest request)
        {
            Debug.Log($"[GameManager:{_environmentId}] Play state changed: {request.play}");
            return new PausePlayResponse { success = true };
        }

        private void OnResetRequested(ResetRequest request) => Debug.Log($"[GameManager:{_environmentId}] Reset requested event received");
        private void OnPlayStateChanged(bool isPlaying) => EventBus.Instance.Publish(new PlayStateChangedEvent { isPlaying = isPlaying });

        #endregion

        #region Reset Sequence

        private void RequestReset()
        {
            if (_isResetting) return;
            if (_currentResetCoroutine != null) StopCoroutine(_currentResetCoroutine);
            _currentResetCoroutine = StartCoroutine(ResetSequence());
        }

        private IEnumerator ResetSequence()
        {
            if (_isResetting) yield break;
            _isResetting = true;

            OnResetStarted?.Invoke(_environmentId);
            Debug.Log($"[GameManager:{_environmentId}] Starting reset sequence...");

            // Désactiver les agents en mouvement
            // if (_spawnCoordinator != null) _spawnCoordinator.SetActive(false);

            // Réinitialiser le NavMesh (si nécessaire) – on ne reconstruit pas la carte
            if (_navMeshManager != null)
            {
                Debug.Log($"[GameManager:{_environmentId}] Rebuilding NavMesh...");
                yield return StartCoroutine(_navMeshManager.Reset());
                _navMeshManager.GetSpawnSurface()?.BuildNavMesh();
                _navMeshManager.GetNavigationSurface()?.BuildNavMesh();
            }

            // Respawnder les agents (positions par défaut)
            // if (_spawnCoordinator != null) yield return StartCoroutine(_spawnCoordinator.SpawnAll());

            _isResetting = false;
            _currentResetCoroutine = null;
            OnResetCompleted?.Invoke(_environmentId);
            Debug.Log($"[GameManager:{_environmentId}] Reset complete");
        }

        #endregion

        #region Public API - Agent Management

        public List<HumanAvatar> GetAllHumans()
        {
            var humans = new List<HumanAvatar>();
            if (_humanPool != null)
            {
                foreach (var go in _humanPool.GetActiveHumans())
                {
                    var h = go.GetComponent<HumanAvatar>();
                    if (h != null) humans.Add(h);
                }
            }
            return humans;
        }

        public Robot GetRobot() => GetComponentInChildren<Robot>();
        // public void ClearAllAgents() => _spawnCoordinator?.ClearAll();

        #endregion

        #region Editor Utilities

#if UNITY_EDITOR
        [ContextMenu("Log Status")]
        private void EditorLogStatus()
        {
            Debug.Log($"[GameManager:{_environmentId}] Status: Init={_isInitialized}, Resetting={_isResetting}");
        }
#endif

        #endregion
    }
}