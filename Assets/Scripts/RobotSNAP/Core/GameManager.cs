using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Simulation;
using RobotSNAP.ROS;
using RobotSNAP.Environment;

namespace RobotSNAP.Core
{
    /// <summary>
    /// Orchestrateur principal - Coordonne l'initialisation d'un environnement
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Header("Core Components")]
        [SerializeField] private EnvironmentBuilder environmentBuilder;
        [SerializeField] private NavMeshManager navMeshManager;
        [SerializeField] private SpawnCoordinator spawnCoordinator;
        [SerializeField] private HumanPoolManager humanPool;
        [SerializeField] private EnvROS envROS;
        [SerializeField] private EnvironmentCreatorFromDataset datasetCreator;
        
        private int _environmentId;
        private SimulationConfig _currentConfig;
        private bool _isInitialized;
        private bool _isResetting = false;  // Flag pour éviter les doubles reset
        private Coroutine _currentResetCoroutine = null;
        
        public int EnvironmentId => _environmentId;
        public bool IsInitialized => _isInitialized;
        
        #region Public API
        
        public void SetEnvironmentId(int id)
        {
            _environmentId = id;
        }
        
        public void ApplyConfig(SimulationConfig config)
        {
            if (config == null) return;
            
            _currentConfig = config;
            
            if (spawnCoordinator != null)
            {
                spawnCoordinator.MinHumans = config.minHumans;
                spawnCoordinator.MaxHumans = config.maxHumans;
                spawnCoordinator.MinAgentDistance = config.minAgentDistance;
                spawnCoordinator.MinPathLength = config.minPathLength;
                spawnCoordinator.MaxPathLength = config.maxPathLength;
            }
            
            if (datasetCreator != null && !string.IsNullOrEmpty(config.datasetPath))
            {
                datasetCreator.FolderPath = config.datasetPath;
            }
            
            if (envROS != null && !string.IsNullOrEmpty(config.rosPrefix))
            {
                envROS.UpdatePrefix(config.rosPrefix);
            }
            
            Debug.Log($"[GameManager:{_environmentId}] Config applied");
        }
        
        public void SetROSPrefix(string prefix)
        {
            envROS?.UpdatePrefix(prefix);
        }
        
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
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Start()
        {
            StartCoroutine(InitializeGame());
        }
        
        private void OnDestroy()
        {
            if (envROS != null)
            {
                envROS.OnResetRequested -= OnResetRequested;
                envROS.OnPlayStateChanged -= OnPlayStateChanged;
            }
        }
        
        #endregion
        
        #region Initialization
        
        private IEnumerator InitializeGame()
        {
            Debug.Log($"[GameManager:{_environmentId}] Initializing...");
            
            if (envROS != null)
            {
                envROS.Initialize("");
                envROS.RegisterResetService(OnResetRequest);
                envROS.RegisterPausePlayService(OnPlayRequest);
                envROS.OnResetRequested += OnResetRequested;
                envROS.OnPlayStateChanged += OnPlayStateChanged;
            }
            
            if (environmentBuilder != null)
            {
                yield return StartCoroutine(environmentBuilder.BuildEnvironment());
            }
            
            if (navMeshManager != null)
            {
                yield return StartCoroutine(navMeshManager.BuildNavMeshes());
            }
            
            if (humanPool != null)
            {
                yield return StartCoroutine(humanPool.Prewarm(20));
            }
            
            if (spawnCoordinator != null)
            {
                yield return StartCoroutine(spawnCoordinator.SpawnAll());
            }
            
            _isInitialized = true;
            Debug.Log($"[GameManager:{_environmentId}] Initialization complete");
        }
        
        #endregion
        
        #region ROS Callbacks
        
        private ResetResponse OnResetRequest(ResetRequest request)
        {
            Debug.Log($"[GameManager:{_environmentId}] Reset requested with dataset: {request.dataset}");
            
            if (!string.IsNullOrEmpty(request.dataset) && datasetCreator != null)
            {
                datasetCreator.FolderPath = request.dataset;
                if (_currentConfig != null)
                {
                    _currentConfig.datasetPath = request.dataset;
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
            // Ne pas déclencher de reset ici, juste propager l'événement
            // L'événement est déjà traité par OnResetRequest
            Debug.Log($"[GameManager:{_environmentId}] Reset requested event received");
        }
        
        private void OnPlayStateChanged(bool isPlaying)
        {
            EventBus.Instance.Publish(new PlayStateChangedEvent { isPlaying = isPlaying });
        }
        
        #endregion
        
        #region Reset Sequence (CORRIGÉ - sans double appel)
        
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
            
            Debug.Log($"[GameManager:{_environmentId}] Starting reset sequence...");
            
            // 1. Désactiver tout ce qui bouge
            if (spawnCoordinator != null)
            {
                spawnCoordinator.SetActive(false);
            }
            
            // 2. Reset l'environnement builder (détruit l'ancien)
            if (environmentBuilder != null)
            {
                yield return StartCoroutine(environmentBuilder.Reset());
            }
            
            // 3. Attendre que le nouvel environnement soit complètement instancié
            yield return null;
            
            // 4. Maintenant reconstruire le NavMesh
            if (navMeshManager != null)
            {
                Debug.Log($"[GameManager:{_environmentId}] Rebuilding NavMesh...");
                yield return StartCoroutine(navMeshManager.Reset());
            }


            navMeshManager.GetSpawnSurface().BuildNavMesh();
            navMeshManager.GetNavigationSurface().BuildNavMesh();
            
            // 6. Re-spawn tout
            if (spawnCoordinator != null)
            {
                yield return StartCoroutine(spawnCoordinator.SpawnAll());
            }
            
            _isResetting = false;
            _currentResetCoroutine = null;
            
            Debug.Log($"[GameManager:{_environmentId}] Reset complete");
        }
        
        #endregion
    }
}