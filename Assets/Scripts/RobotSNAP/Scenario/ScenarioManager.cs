// Scripts/RobotSNAP/Scenario/ScenarioManager.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RobotSNAP.Environment;

namespace RobotSNAP.Core.Scenario
{
    /// <summary>
    /// Gère le cycle de vie des environnements à partir d’un scénario YAML.
    /// Charge un scénario, instancie le(s) GameManager (environnements) et applique le scénario.
    /// </summary>
    public sealed class ScenarioManager : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Prefab de l’environnement (contenant GameManager, etc.)")]
        [SerializeField] private GameObject _environmentPrefab;

        [Tooltip("Chargeur de scénarios YAML")]
        [SerializeField] private ScenarioLoader _scenarioLoader;

        [Header("Settings")]
        [Tooltip("Nom du scénario par défaut (sans extension)")]
        [SerializeField] private string _defaultScenarioName = "default";

        [Tooltip("Applique automatiquement le scénario après chargement")]
        [SerializeField] private bool _autoApplyAfterLoad = true;

        [Tooltip("Délai avant application (secondes)")]
        [SerializeField] private float _applyDelay = 0.5f;

        [Tooltip("Active les logs")]
        [SerializeField] private bool _logEvents = true;

        [Tooltip("Met la simulation en pause après le chargement/application du scénario")]
        [SerializeField] private bool _startPaused = true;

        [Header("Fallback")]
        [Tooltip("Scénario de secours si aucun fichier YAML n'est trouvé")]
        [SerializeField] private ScenarioData _fallbackScenario;

        // État
        private string _currentScenarioId;
        private ScenarioData _currentScenarioData;
        private List<GameManager> _gameManagers = new();
        [SerializeField] private bool _isLoading;
        private Coroutine _loadCoroutine;

        // Propriétés
        public string CurrentScenarioId => _currentScenarioId;
        public ScenarioData CurrentScenarioData => _currentScenarioData;
        public IReadOnlyList<GameManager> Environments => _gameManagers;
        public bool HasScenarioLoaded => _currentScenarioData != null;
        public bool IsLoading => _isLoading;

        // Événements
        public event Action<ScenarioData> OnScenarioLoaded;
        public event Action<ScenarioData> OnScenarioApplied;
        public event Action<string> OnScenarioError;

        private void Awake()
        {
            EnsureDependencies();
        }

        private void Start()
        {
            // Le Supervisor peut appeler LoadDefaultScenario() après son initialisation
            LoadDefaultScenario();
        }

        private void OnDestroy()
        {
            CancelLoad();
            ClearEnvironments();
        }

        private void EnsureDependencies()
        {
            if (_scenarioLoader == null)
            {
                _scenarioLoader = GetComponent<ScenarioLoader>();
                if (_scenarioLoader == null)
                    _scenarioLoader = gameObject.AddComponent<ScenarioLoader>();
            }

            if (_environmentPrefab == null)
                Debug.LogError("[ScenarioManager] EnvironmentPrefab is not assigned!");
        }

        private void CancelLoad()
        {
            if (_loadCoroutine != null)
            {
                StopCoroutine(_loadCoroutine);
                _loadCoroutine = null;
            }
            _isLoading = false;
        }

        private void ClearEnvironments()
        {
            foreach (var gm in _gameManagers)
                if (gm != null) Destroy(gm.gameObject);
            _gameManagers.Clear();
        }

        private IEnumerator LoadScenarioCoroutine(string scenarioName)
        {
            _isLoading = true;
            if (_applyDelay > 0)
                yield return new WaitForSeconds(_applyDelay);

            var scenario = _scenarioLoader.LoadScenario(scenarioName);
            if (scenario == null)
            {
                OnScenarioErrorInternal($"Failed to load scenario: {scenarioName}");
                _isLoading = false;
                yield break;
            }

            _currentScenarioId = scenarioName;
            _currentScenarioData = scenario;
            OnScenarioLoadedInternal(scenario);

            // Reconstruire les environnements
            ClearEnvironments();

            int envCount = Supervisor.Instance?.ActiveConfig?.EnvironmentCount ?? 1;
            for (int i = 0; i < envCount; i++)
            {
                Debug.Log($"Creating environment {i + 1}/{envCount} for scenario '{scenarioName}'");
                var gm = CreateEnvironment(i);
                if (gm != null)
                    _gameManagers.Add(gm);
            }

            // Initialiser tous les GameManagers (NavMesh, pool, spawn initial)
            foreach (var gm in _gameManagers)
            {
                if (gm != null)
                    yield return StartCoroutine(gm.Initialize());
            }

            if (_autoApplyAfterLoad)
                yield return StartCoroutine(ApplyScenarioToAllCoroutine());

            _isLoading = false;
            _loadCoroutine = null;
        }

        private GameManager CreateEnvironment(int index)
        {
            if (_environmentPrefab == null) return null;
            Vector3 position = new Vector3(index * 20f, 0, 0);
            GameObject instance = Instantiate(_environmentPrefab, position, Quaternion.identity, transform);
            instance.name = $"Environment_{index}";
            var gm = instance.GetComponent<GameManager>();
            if (gm == null)
            {
                Debug.LogError("[ScenarioManager] Environment prefab missing GameManager component");
                Destroy(instance);
                return null;
            }
            gm.SetEnvironmentId(index);
            // Appliquer la config globale (Supervisor)
            var config = Supervisor.Instance?.ActiveConfig;
            if (config != null) gm.ApplyConfig(config);
            return gm;
        }

        private IEnumerator ApplyScenarioToAllCoroutine()
        {
            if (_currentScenarioData == null) yield break;

            foreach (var gm in _gameManagers)
            {
                if (gm == null) continue;
                var applier = gm.GetComponent<ScenarioApplier>();
                if (applier == null)
                    applier = gm.gameObject.AddComponent<ScenarioApplier>();
                applier.SetLoader(_scenarioLoader);
                yield return StartCoroutine(applier.ApplyScenario(gm, _currentScenarioData));
            }

            OnScenarioApplied?.Invoke(_currentScenarioData);
            if (_logEvents) Debug.Log($"[ScenarioManager] Scenario applied to {_gameManagers.Count} environments: {_currentScenarioData.Name}");

            // NOUVEAU : mettre en pause si demandé
            if (_startPaused && Supervisor.Instance != null)
            {
                Supervisor.Instance.Pause();
                if (_logEvents) Debug.Log("[ScenarioManager] Simulation paused after scenario application");
            }
        }

        #region Event Wrappers

        private void OnScenarioLoadedInternal(ScenarioData scenario)
        {
            OnScenarioLoaded?.Invoke(scenario);
            if (_logEvents) Debug.Log($"[ScenarioManager] Scenario loaded: {scenario.Name}");
        }

        private void OnScenarioErrorInternal(string error)
        {
            OnScenarioError?.Invoke(error);
            Debug.LogError($"[ScenarioManager] {error}");
        }

        #endregion

        #region Public API

        /// <summary>
        /// Charge un scénario par son nom (sans extension). Reconstruit les environnements et applique le scénario.
        /// </summary>
        public void LoadScenario(string scenarioName)
        {
            if (_isLoading)
            {
                OnScenarioErrorInternal("Already loading a scenario");
                return;
            }
            CancelLoad();
            Debug.Log($"[ScenarioManager] Loading scenario: {scenarioName}");
            _loadCoroutine = StartCoroutine(LoadScenarioCoroutine(scenarioName));
        }

        /// <summary>
        /// Charge le scénario par défaut (depuis la SimulationConfig ou le fallback).
        /// </summary>
        public void LoadDefaultScenario()
        {
            var config = Supervisor.Instance?.ActiveConfig;
            if (config != null && !string.IsNullOrEmpty(config.DefaultScenario))
            {
                LoadScenario(config.DefaultScenario);
                return;
            }

            if (_fallbackScenario != null)
            {
                _currentScenarioData = _fallbackScenario;
                _currentScenarioId = _fallbackScenario.Name;
                OnScenarioLoadedInternal(_fallbackScenario);
                ClearEnvironments();
                // Créer un seul environnement par défaut
                var gm = CreateEnvironment(0);
                if (gm != null) _gameManagers.Add(gm);
                if (_autoApplyAfterLoad)
                    StartCoroutine(ApplyScenarioToAllCoroutine());
                return;
            }

            OnScenarioErrorInternal("No default scenario available");
        }

        /// <summary>
        /// Applique le scénario courant à tous les environnements existants (sans recréer les envs).
        /// </summary>
        public void ApplyCurrentScenarioToAll()
        {
            if (_currentScenarioData == null)
            {
                OnScenarioErrorInternal("No scenario loaded");
                return;
            }
            CancelLoad();
            _loadCoroutine = StartCoroutine(ApplyScenarioToAllCoroutine());
        }

        /// <summary>
        /// Réinitialise les agents dans tous les environnements et réapplique le scénario.
        /// </summary>
        public void ResetAndReapply()
        {
            foreach (var gm in _gameManagers)
                gm?.EditorReset();   // reset des positions des agents
            ApplyCurrentScenarioToAll();
        }

        /// <summary>
        /// Liste des scénarios disponibles (fichiers .yaml).
        /// </summary>
        public List<string> GetAvailableScenarios()
        {
            return _scenarioLoader?.GetAvailableScenarios() ?? new List<string>();
        }

        /// <summary>
        /// Informations d’un scénario (sans tout charger).
        /// </summary>
        public ScenarioInfo GetScenarioInfo(string scenarioName)
        {
            return _scenarioLoader?.GetScenarioInfo(scenarioName);
        }

        #endregion
    }
}