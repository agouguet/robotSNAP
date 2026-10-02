using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RobotSNAP.Environment;
using RobotSNAP.Core.Scenario;
using RobotSNAP.Agents;

namespace RobotSNAP.Core
{
    /// <summary>
    /// Gère un environnement unique (NavMesh, pool d'humains, etc.).
    /// L'initialisation et le reset sont déclenchés par ScenarioManager.
    /// Toute communication inter-composants se fait via l'EventBus (notifications uniquement).
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Header("Core Components")]
        [SerializeField] private EnvironmentBuilder _environmentBuilder;
        [SerializeField] private NavMeshManager _navMeshManager;
        [SerializeField] private HumanPoolManager _humanPool;

        // SUPPRIMÉ : [SerializeField] private EnvROS _envROS; // Plus besoin, ROS est géré par EnvROS lui-même via EventBus

        private int _environmentId;
        private SimulationConfig _currentConfig;
        private bool _isInitialized;
        private bool _isResetting = false;
        private Coroutine _currentResetCoroutine = null;
        private HumanManager _humanManager;

        // Properties (publiques pour ScenarioManager)
        public bool IsInitialized => _isInitialized;
        public EnvironmentBuilder EnvironmentBuilder => _environmentBuilder;
        public NavMeshManager NavMeshManager => _navMeshManager;
        public HumanPoolManager HumanPool => _humanPool;

        /// <summary>
        /// The crowd registry, and the entry point of an external driver (the Python API): it carries the stable
        /// ids of the active humans together with their positions and velocities, and receives their commanded
        /// velocities. Resolved from the hierarchy when the scene did not wire it, so a bridge never has to
        /// search for it itself.
        /// </summary>
        public HumanManager HumanManager
        {
            get
            {
                if (_humanManager == null)
                    _humanManager = GetComponentInChildren<HumanManager>(true);

                return _humanManager;
            }
        }

        #region Unity Lifecycle

        private void Start()
        {
            // L'initialisation est déclenchée par ScenarioManager
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

            Debug.Log($"[GameManager:{_environmentId}] Initialization complete");
        }

        public IEnumerator BuildMap(string mapName)
        {
            if (_environmentBuilder != null && !string.IsNullOrEmpty(mapName))
            {
                yield return StartCoroutine(_environmentBuilder.BuildEnvironment(mapName));
                Debug.Log($"[GameManager:{_environmentId}] Map '{mapName}' built.");

                // Initialize() built the NavMesh before the map existed, so it described the previous
                // environment: rebuild it on the walls that were just created. The walkable grid the humans
                // plan on is built by the environment builder itself, from the same image.
                if (_navMeshManager != null)
                {
                    Debug.Log($"[GameManager:{_environmentId}] Rebuilding NavMesh on the new map...");
                    yield return StartCoroutine(_navMeshManager.BuildNavMeshes());
                }
            }
            else
            {
                Debug.LogWarning($"[GameManager:{_environmentId}] No map to build (mapName: '{mapName}')");
            }
        }

        #endregion

        #region Public API - Configuration

        public void SetEnvironmentId(int id) => _environmentId = id;

        public void ApplyConfig(SimulationConfig config)
        {
            if (config == null) return;
            _currentConfig = config;

            // SUPPRIMÉ : gestion du RosPrefix -> EnvROS va chercher lui-même dans Supervisor.Instance.ActiveConfig
            // if (_envROS != null && !string.IsNullOrEmpty(config.RosPrefix))
            //     _envROS.UpdatePrefix(config.RosPrefix);

            Debug.Log($"[GameManager:{_environmentId}] Config applied");
        }

        // SUPPRIMÉ : public void SetROSPrefix(string prefix) => _envROS?.UpdatePrefix(prefix);

        #endregion

        #region Public API - Reset (appelé par ScenarioManager)

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
            
            Debug.Log($"[GameManager:{_environmentId}] Reset complete");
        }

        #endregion

        #region Public API - Agent Management

        /// <summary>
        /// Supprime tous les agents (robot + humains) de l'environnement, mais conserve la carte.
        ///
        /// Tous les robots, pas le premier trouvé : un scénario peut en aligner plusieurs, et
        /// <c>GetComponentInChildren&lt;Robot&gt;()</c> n'en renvoyait qu'un, ce qui laissait les autres debout
        /// après un arrêt. Le roster est vidé en même temps que les corps, sinon une ré-application dans la même
        /// frame - un stop suivi d'un start depuis Python - réutiliserait un robot déjà détruit par
        /// <see cref="SceneTeardown.Destroy"/>, qui diffère la destruction à la fin de la frame en mode Play.
        /// </summary>
        public void ClearAgents()
        {
            // 1. Retourner tous les humains au pool (les désactiver)
            if (_humanPool != null)
            {
                _humanPool.ReturnAllHumans();
                Debug.Log($"[GameManager:{_environmentId}] All humans returned to pool.");
            }

            // 2. Détruire tous les robots, et oublier leurs emplacements.
            RobotRoster roster = GetComponentInChildren<RobotRoster>(true);
            if (roster != null)
            {
                int count = roster.Count;
                roster.Clear();
                Debug.Log($"[GameManager:{_environmentId}] {count} robot(s) destroyed.");
            }
            else
            {
                // A hand-built environment with no roster keeps its robots, so they are cleared by search.
                Robot[] robots = GetComponentsInChildren<Robot>(true);
                for (int index = 0; index < robots.Length; index++)
                {
                    if (robots[index] != null)
                        SceneTeardown.Destroy(robots[index].gameObject);
                }

                if (robots.Length > 0)
                    Debug.Log($"[GameManager:{_environmentId}] {robots.Length} robot(s) destroyed.");
            }

            // 3. Optionnel : réinitialiser les flags de scénario (ex: _scenarioApplied = false) géré par ScenarioManager
        }

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
