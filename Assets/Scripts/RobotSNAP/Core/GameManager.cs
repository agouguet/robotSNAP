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
        
        public int EnvironmentId => _environmentId;
        public bool IsInitialized => _isInitialized;
        
        #region Public API
        
        /// <summary>
        /// Définit l'ID de l'environnement
        /// </summary>
        public void SetEnvironmentId(int id)
        {
            _environmentId = id;
        }
        
        /// <summary>
        /// Applique la configuration à ce GameManager
        /// </summary>
        public void ApplyConfig(SimulationConfig config)
        {
            if (config == null) return;
            
            _currentConfig = config;
            
            // Appliquer au SpawnCoordinator
            if (spawnCoordinator != null)
            {
                spawnCoordinator.MinHumans = config.minHumans;
                spawnCoordinator.MaxHumans = config.maxHumans;
                spawnCoordinator.MinAgentDistance = config.minAgentDistance;
                spawnCoordinator.MinPathLength = config.minPathLength;
                spawnCoordinator.MaxPathLength = config.maxPathLength;
            }
            
            // Appliquer au Dataset Creator
            Debug.Log(datasetCreator);
            if (datasetCreator != null && !string.IsNullOrEmpty(config.datasetPath))
            {
                Debug.Log(config.datasetPath);
                datasetCreator.FolderPath = config.datasetPath;
            }
            
            // Appliquer les paramètres ROS
            if (envROS != null && !string.IsNullOrEmpty(config.rosPrefix))
            {
                envROS.UpdatePrefix(config.rosPrefix);
            }
            
            Debug.Log($"[GameManager:{_environmentId}] Config applied");
        }
        
        /// <summary>
        /// Définit le préfixe ROS (pour multi-environnements)
        /// </summary>
        public void SetROSPrefix(string prefix)
        {
            envROS?.UpdatePrefix(prefix);
        }
        
        /// <summary>
        /// Reset l'environnement (appelé par l'éditeur ou Supervisor)
        /// </summary>
        [ContextMenu("Reset Environment")]
        public void EditorReset()
        {
            if (!_isInitialized)
            {
                Debug.LogWarning($"[GameManager:{_environmentId}] Not initialized yet");
                return;
            }
            
            StartCoroutine(ResetSequence());
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
            
            Debug.Log($"[GameManager:{_environmentId}] Cleaned up");
        }
        
        #endregion
        
        #region Initialization
        
        private IEnumerator InitializeGame()
        {
            Debug.Log($"[GameManager:{_environmentId}] Initializing...");
            
            // Initialize ROS
            if (envROS != null)
            {
                envROS.Initialize("");
                envROS.RegisterResetService(OnResetRequest);
                envROS.RegisterPausePlayService(OnPlayRequest);
                
                envROS.OnResetRequested += OnResetRequested;
                envROS.OnPlayStateChanged += OnPlayStateChanged;
            }
            
            // Build environment (dataset or procedural)
            if (environmentBuilder != null)
            {
                yield return StartCoroutine(environmentBuilder.BuildEnvironment());
            }
            
            // Build NavMeshes
            if (navMeshManager != null)
            {
                yield return StartCoroutine(navMeshManager.BuildNavMeshes());
            }
            
            // Prewarm human pool
            if (humanPool != null)
            {
                yield return StartCoroutine(humanPool.Prewarm(20));
            }
            
            // Spawn all agents (robot, humans, goal)
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
            
            // Update dataset path if provided
            if (!string.IsNullOrEmpty(request.dataset) && datasetCreator != null)
            {
                datasetCreator.FolderPath = request.dataset;
                if (_currentConfig != null)
                {
                    _currentConfig.datasetPath = request.dataset;
                }
            }
            
            // Trigger reset
            if (_isInitialized)
            {
                StartCoroutine(ResetSequence());
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
            EventBus.Instance.Publish(new ResetRequestEvent { dataset = request.dataset });
        }
        
        private void OnPlayStateChanged(bool isPlaying)
        {
            EventBus.Instance.Publish(new PlayStateChangedEvent { isPlaying = isPlaying });
        }
        
        #endregion
        
        #region Reset Sequence
        
        private IEnumerator ResetSequence()
        {
            Debug.Log($"[GameManager:{_environmentId}] Starting reset sequence...");
            
            // Désactiver robot et goal
            if (spawnCoordinator != null)
            {
                spawnCoordinator.SetActive(false);
            }
            
            // Reset environment builder
            if (environmentBuilder != null)
            {
                yield return StartCoroutine(environmentBuilder.Reset());
            }
            
            // Reset navmesh
            if (navMeshManager != null)
            {
                yield return StartCoroutine(navMeshManager.Reset());
            }
            
            // Attendre un frame
            yield return null;
            
            // Re-spawn tout
            if (spawnCoordinator != null)
            {
                yield return StartCoroutine(spawnCoordinator.SpawnAll());
            }
            
            Debug.Log($"[GameManager:{_environmentId}] Reset complete");
        }
        
        #endregion
    }
}