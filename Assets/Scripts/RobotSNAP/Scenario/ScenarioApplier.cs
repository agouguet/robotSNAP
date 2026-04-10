// Scripts/RobotSNAP/Core/Scenario/ScenarioApplier.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RobotSNAP.Core;
using RobotSNAP.Environment;
using RobotSNAP.Human;

namespace RobotSNAP.Core.Scenario
{
    /// <summary>
    /// Applique un scénario YAML à un GameManager spécifique
    /// Gère le positionnement du robot, la configuration des humains,
    /// les objectifs, les formations et les comportements spéciaux
    /// </summary>
    public sealed class ScenarioApplier : MonoBehaviour
    {
        #region Serialized Fields

        [Header("References")]
        [Tooltip("Reference to the ScenarioLoader for resolving points")]
        [SerializeField] private ScenarioLoader _loader;
        
        [Header("Settings")]
        [Tooltip("Destroy existing humans before applying scenario")]
        [SerializeField] private bool _clearExistingHumans = true;
        
        [Tooltip("Reset robot position before applying")]
        [SerializeField] private bool _resetRobotPosition = true;
        
        [Tooltip("Enable debug logging")]
        [SerializeField] private bool _logEvents = true;

        #endregion

        #region Private Fields

        private GameManager _gameManager;
        private ScenarioData _currentScenario;
        private readonly List<Coroutine> _activeCoroutines = new();
        private readonly Dictionary<string, HumanAvatar> _spawnedHumans = new();
        private int _humanCounter;

        #endregion

        #region Properties

        /// <summary>
        /// Current scenario being applied
        /// </summary>
        public ScenarioData CurrentScenario => _currentScenario;
        
        /// <summary>
        /// Whether a scenario is currently being applied
        /// </summary>
        public bool IsApplying { get; private set; }

        #endregion

        #region Events

        /// <summary>
        /// Triggered when scenario application starts
        /// </summary>
        public event Action<ScenarioData> OnApplicationStarted;
        
        /// <summary>
        /// Triggered when scenario application completes
        /// </summary>
        public event Action<ScenarioData> OnApplicationCompleted;
        
        /// <summary>
        /// Triggered when an error occurs during application
        /// </summary>
        public event Action<string> OnApplicationError;

        #endregion

        #region Public Methods

        /// <summary>
        /// Sets the ScenarioLoader reference
        /// </summary>
        public void SetLoader(ScenarioLoader loader)
        {
            _loader = loader;
        }

        /// <summary>
        /// Applies a scenario to the specified GameManager
        /// </summary>
        /// <param name="gameManager">Target GameManager</param>
        /// <param name="scenario">Scenario to apply</param>
        public IEnumerator ApplyScenario(GameManager gameManager, ScenarioData scenario)
        {
            if (gameManager == null)
            {
                OnApplicationError?.Invoke("GameManager is null");
                yield break;
            }
            
            if (scenario == null)
            {
                OnApplicationError?.Invoke("Scenario is null");
                yield break;
            }
            
            if (_loader == null)
            {
                OnApplicationError?.Invoke("ScenarioLoader is not set");
                yield break;
            }
            
            // Validate scenario
            if (!scenario.IsValid(out string validationError))
            {
                OnApplicationError?.Invoke($"Invalid scenario: {validationError}");
                yield break;
            }
            
            _gameManager = gameManager;
            _currentScenario = scenario;
            IsApplying = true;
            
            OnApplicationStarted?.Invoke(scenario);
            
            if (_logEvents)
            {
                Debug.Log($"[ScenarioApplier] Applying scenario: {scenario.Name} to GameManager");
            }
            
            // Execute application steps
            yield return StartCoroutine(ApplyScenarioCoroutine());
            
            IsApplying = false;
            OnApplicationCompleted?.Invoke(scenario);
            
            if (_logEvents)
            {
                Debug.Log($"[ScenarioApplier] Successfully applied scenario: {scenario.Name}");
            }
        }

        /// <summary>
        /// Stops all active behaviours (wander, follow, etc.)
        /// </summary>
        public void StopAllBehaviours()
        {
            foreach (var coroutine in _activeCoroutines)
            {
                if (coroutine != null)
                {
                    StopCoroutine(coroutine);
                }
            }
            _activeCoroutines.Clear();
        }

        /// <summary>
        /// Clears all spawned humans tracked by this applier
        /// </summary>
        public void ClearTrackedHumans()
        {
            _spawnedHumans.Clear();
            _humanCounter = 0;
        }

        #endregion

        #region Private Methods - Main Coroutine

        private IEnumerator ApplyScenarioCoroutine()
        {
            // 1. Clean up existing agents if needed
            if (_clearExistingHumans)
            {
                yield return StartCoroutine(ClearExistingHumans());
            }
            
            // 2. Wait for GameManager to be ready
            yield return StartCoroutine(WaitForGameManagerReady());
            
            // 3. Apply simulation configuration
            ApplySimulationConfig();
            
            // 4. Setup robot
            yield return StartCoroutine(SetupRobot());
            
            // 5. Setup humans
            yield return StartCoroutine(SetupHumans());
            
            // 6. Apply map if specified (optional)
            ApplyMapIfNeeded();
            
            if (_logEvents)
            {
                Debug.Log($"[ScenarioApplier] Scenario application complete: {_currentScenario.Name}");
            }
        }

        private IEnumerator ClearExistingHumans()
        {
            // Access SpawnCoordinator to clear humans
            var spawnCoordinator = GetSpawnCoordinator();
            if (spawnCoordinator != null)
            {
                // SpawnCoordinator should have a method to clear all humans
                // If not, we'll just clear our tracking
                ClearTrackedHumans();
            }
            
            yield return null;
        }

        private IEnumerator WaitForGameManagerReady()
        {
            // Wait for GameManager to be initialized
            // Use reflection or public property if available
            var isInitializedProperty = _gameManager.GetType().GetProperty("IsInitialized");
            if (isInitializedProperty != null)
            {
                while (!(bool)isInitializedProperty.GetValue(_gameManager))
                {
                    yield return new WaitForSeconds(0.1f);
                }
            }
            
            // Wait for SpawnCoordinator to be ready
            var spawnCoordinator = GetSpawnCoordinator();
            if (spawnCoordinator != null)
            {
                // Small delay to ensure spawn coordinator is ready
                yield return new WaitForSeconds(0.2f);
            }
            
            yield return null;
        }

        #endregion

        #region Private Methods - Simulation Config

        private void ApplySimulationConfig()
        {
            if (_currentScenario.Simulation == null) return;
            
            var simConfig = _currentScenario.Simulation;
            
            // Apply random seed
            int seed = simConfig.RandomSeed == -1 
                ? System.Environment.TickCount 
                : simConfig.RandomSeed;
            UnityEngine.Random.InitState(seed);
            
            // Apply time scale
            Time.timeScale = simConfig.TimeScale;
            
            // Configure SpawnCoordinator if needed
            var spawnCoordinator = GetSpawnCoordinator();
            if (spawnCoordinator != null)
            {
                // Set min/max humans if properties exist
                SetPropertyIfExists(spawnCoordinator, "MinHumans", simConfig.MinHumans);
                SetPropertyIfExists(spawnCoordinator, "MaxHumans", simConfig.MaxHumans);
            }
            
            // Schedule end of simulation if duration is set
            if (simConfig.Duration > 0)
            {
                _gameManager.Invoke(nameof(GameManager.EditorReset), simConfig.Duration);
            }
            
            if (_logEvents)
            {
                Debug.Log($"[ScenarioApplier] Simulation config: seed={seed}, duration={simConfig.Duration}s, timeScale={simConfig.TimeScale}");
            }
        }

        private void SetPropertyIfExists(object target, string propertyName, object value)
        {
            var prop = target.GetType().GetProperty(propertyName);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(target, value);
            }
        }

        #endregion

        #region Private Methods - Robot Setup

        private IEnumerator SetupRobot()
        {
            var robot = FindRobot();
            if (robot == null)
            {
                OnApplicationError?.Invoke("Robot not found in scene");
                yield break;
            }
            
            var robotConfig = _currentScenario.Robot;
            if (robotConfig == null)
            {
                OnApplicationError?.Invoke("Robot configuration is missing");
                yield break;
            }
            
            // Set start position
            if (_resetRobotPosition)
            {
                Vector3 startPos = ResolvePosition(robotConfig.StartRef);
                robot.transform.position = startPos;
                
                if (_logEvents)
                {
                    Debug.Log($"[ScenarioApplier] Robot position set to: {startPos}");
                }
            }
            
            // Set goal
            Vector3 goalPos = ResolvePosition(robotConfig.GoalRef);
            SetRobotGoal(robot, goalPos);
            
            // Set behavior
            SetRobotBehavior(robot, robotConfig.Behavior);
            
            // Set speed
            SetRobotSpeed(robot, robotConfig.Speed);
            
            yield return null;
        }

        private GameObject FindRobot()
        {
            // Try to find Robot component in GameManager
            var robotComponent = _gameManager.GetComponentInChildren<Robot>();
            if (robotComponent != null)
            {
                return robotComponent.gameObject;
            }
            
            // Fallback: find by tag
            var robotByTag = GameObject.FindGameObjectWithTag("Robot");
            if (robotByTag != null)
            {
                return robotByTag;
            }
            
            // Fallback: find by name
            var robotByName = GameObject.Find("Robot");
            if (robotByName != null)
            {
                return robotByName;
            }
            
            return null;
        }

        private void SetRobotGoal(GameObject robot, Vector3 goal)
        {
            var robotComponent = robot.GetComponent<Robot>();
            if (robotComponent != null)
            {
                robotComponent.SetGoal(goal);
                if (_logEvents) Debug.Log($"[ScenarioApplier] Robot goal set to: {goal}");
            }
        }

        private void SetRobotBehavior(GameObject robot, string behavior)
        {
            var robotComponent = robot.GetComponent<Robot>();
            if (robotComponent != null)
            {
                robotComponent.SetBehavior(behavior);
                if (_logEvents) Debug.Log($"[ScenarioApplier] Robot behavior set to: {behavior}");
            }
        }

        private void SetRobotSpeed(GameObject robot, float speed)
        {
            var robotComponent = robot.GetComponent<Robot>();
            if (robotComponent != null)
            {
                robotComponent.SetSpeed(speed);
                if (_logEvents) Debug.Log($"[ScenarioApplier] Robot speed set to: {speed}");
            }
        }

        #endregion

        #region Private Methods - Humans Setup

        private IEnumerator SetupHumans()
        {
            if (_currentScenario.Humans == null || _currentScenario.Humans.Count == 0)
            {
                if (_logEvents) Debug.Log("[ScenarioApplier] No humans to spawn");
                yield break;
            }
            
            var spawnCoordinator = GetSpawnCoordinator();
            if (spawnCoordinator == null)
            {
                OnApplicationError?.Invoke("SpawnCoordinator not found, cannot spawn humans");
                yield break;
            }
            
            // Calculate total humans to spawn
            int totalHumans = 0;
            foreach (var config in _currentScenario.Humans)
            {
                totalHumans += config.Count;
            }
            
            if (totalHumans == 0)
            {
                yield break;
            }
            
            if (_logEvents)
            {
                Debug.Log($"[ScenarioApplier] Spawning {totalHumans} humans across {_currentScenario.Humans.Count} groups");
            }
            
            // Trigger spawn through SpawnCoordinator
            // Note: This assumes SpawnCoordinator has a SpawnAll method
            var spawnMethod = spawnCoordinator.GetType().GetMethod("SpawnAll");
            if (spawnMethod != null)
            {
                yield return StartCoroutine((IEnumerator)spawnMethod.Invoke(spawnCoordinator, null));
            }
            
            // Wait a frame for humans to be spawned
            yield return null;
            
            // Find all humans and configure them
            var allHumans = FindObjectsOfType<HumanAvatar>();
            
            if (allHumans.Length == 0)
            {
                Debug.LogWarning("[ScenarioApplier] No humans found after spawn");
                yield break;
            }
            
            // Configure humans according to scenario
            int humanIndex = 0;
            foreach (var config in _currentScenario.Humans)
            {
                for (int i = 0; i < config.Count && humanIndex < allHumans.Length; i++)
                {
                    var human = allHumans[humanIndex];
                    if (human != null)
                    {
                        ConfigureHuman(human, config, i, config.Count);
                        string instanceId = $"{config.Id}_{_humanCounter++}";
                        _spawnedHumans[instanceId] = human;
                    }
                    humanIndex++;
                }
            }
            
            if (_logEvents)
            {
                Debug.Log($"[ScenarioApplier] Configured {humanIndex} humans");
            }
        }

        private void ConfigureHuman(HumanAvatar human, HumanScenarioConfig config, int index, int total)
        {
            // Set spawn position (if not already set by SpawnCoordinator)
            if (config.Spawn != null)
            {
                Vector3 spawnPos = ResolveSpawnPosition(config.Spawn, index, total);
                if (spawnPos != Vector3.zero)
                {
                    human.transform.position = spawnPos;
                }
            }
            
            // Set goal
            if (config.Goal != null)
            {
                ResolveAndSetGoal(human, config.Goal);
            }
            
            // Set speed
            human.SetSpeed(config.Speed);
            
            // Set behavior
            if (!string.IsNullOrEmpty(config.Behavior))
            {
                human.SetBehavior(config.Behavior);
            }
            
            // Set color
            if (config.Color != null && config.Color.Length >= 3)
            {
                SetHumanColor(human, new Color(config.Color[0], config.Color[1], config.Color[2]));
            }
            
            // Set personality (for SFM)
            if (config.Personality != null)
            {
                SetHumanPersonality(human, config.Personality);
            }
            
            if (_logEvents)
            {
                Debug.Log($"[ScenarioApplier] Configured human {config.Id}: pos={human.transform.position}, speed={config.Speed}");
            }
        }

        private Vector3 ResolveSpawnPosition(SpawnConfig spawn, int index, int total)
        {
            if (spawn == null) return Vector3.zero;
            
            switch (spawn.Type?.ToLower())
            {
                case "point":
                    return ResolvePosition(spawn.Reference);
                    
                case "random":
                    Bounds bounds = ResolveBounds(spawn.Reference);
                    return new Vector3(
                        UnityEngine.Random.Range(bounds.min.x, bounds.max.x),
                        bounds.center.y,
                        UnityEngine.Random.Range(bounds.min.z, bounds.max.z)
                    );
                    
                case "formation":
                    Vector3 center = GetFormationCenter(spawn);
                    if (spawn.Formation == "line")
                    {
                        float offset = (index - (total - 1) / 2f) * spawn.Spacing;
                        return center + new Vector3(offset, 0, 0);
                    }
                    else if (spawn.Formation == "circle")
                    {
                        float angle = (index / (float)total) * Mathf.PI * 2;
                        return center + new Vector3(Mathf.Cos(angle) * spawn.Spacing, 0, Mathf.Sin(angle) * spawn.Spacing);
                    }
                    break;
            }
            
            return Vector3.zero;
        }

        private Vector3 GetFormationCenter(SpawnConfig spawn)
        {
            if (spawn.RelativeTo == "robot")
            {
                var robot = FindRobot();
                if (robot != null) return robot.transform.position;
            }
            else if (!string.IsNullOrEmpty(spawn.RelativeTo))
            {
                return ResolvePosition(spawn.RelativeTo);
            }
            
            return Vector3.zero;
        }

        private void ResolveAndSetGoal(HumanAvatar human, GoalConfig goal)
        {
            if (goal == null) return;
            
            switch (goal.Type?.ToLower())
            {
                case "point":
                    Vector3 targetPos = ResolvePosition(goal.Reference);
                    human.SetGoal(targetPos);
                    if (_logEvents) Debug.Log($"[ScenarioApplier] Human goal set to point: {targetPos}");
                    break;
                    
                case "wander":
                    Coroutine wanderCoroutine = StartCoroutine(WanderRoutine(human, goal.Radius));
                    _activeCoroutines.Add(wanderCoroutine);
                    if (_logEvents) Debug.Log($"[ScenarioApplier] Human set to wander mode (radius={goal.Radius})");
                    break;
                    
                case "follow":
                    if (goal.Target == "robot")
                    {
                        Coroutine followCoroutine = StartCoroutine(FollowRobotRoutine(human));
                        _activeCoroutines.Add(followCoroutine);
                        if (_logEvents) Debug.Log("[ScenarioApplier] Human set to follow robot");
                    }
                    break;
                    
                case "stay":
                    human.SetGoal(human.transform.position);
                    if (_logEvents) Debug.Log("[ScenarioApplier] Human set to stay");
                    break;
            }
        }

        #endregion

        #region Private Methods - Behaviour Routines

        private IEnumerator WanderRoutine(HumanAvatar human, float radius)
        {
            while (human != null && human.gameObject.activeSelf)
            {
                Vector3 randomOffset = UnityEngine.Random.insideUnitSphere * radius;
                randomOffset.y = 0;
                Vector3 newGoal = human.transform.position + randomOffset;
                human.SetGoal(newGoal);
                yield return new WaitForSeconds(UnityEngine.Random.Range(3f, 7f));
            }
        }

        private IEnumerator FollowRobotRoutine(HumanAvatar human)
        {
            while (human != null && human.gameObject.activeSelf)
            {
                var robot = FindRobot();
                if (robot != null)
                {
                    human.SetGoal(robot.transform.position);
                }
                yield return new WaitForSeconds(0.3f);
            }
        }

        #endregion

        #region Private Methods - Helpers

        private Vector3 ResolvePosition(string reference)
        {
            if (string.IsNullOrEmpty(reference))
                return Vector3.zero;
            
            return _loader?.GetPosition(_currentScenario, reference) ?? Vector3.zero;
        }

        private Bounds ResolveBounds(string reference)
        {
            if (string.IsNullOrEmpty(reference))
                return new Bounds();
            
            return _loader?.GetBounds(_currentScenario, reference) ?? new Bounds();
        }

        private void SetHumanColor(HumanAvatar human, Color color)
        {
            var renderer = human.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = color;
            }
        }

        private void SetHumanPersonality(HumanAvatar human, PersonalityConfig personality)
        {
            var movement = human.GetComponent<HumanMovement>();
            if (movement != null)
            {
                // Try to get SFM controller
                // var sfmController = movement.GetController<SFMController>();
                // if (sfmController != null)
                // {
                //     sfmController.SetAssertiveness(personality.Assertiveness);
                //     sfmController.SetPersonalSpace(personality.PersonalSpace);
                // }
            }
        }

        private SpawnCoordinator GetSpawnCoordinator()
        {
            if (_gameManager == null) return null;
            
            // Try to get SpawnCoordinator component
            var spawnCoordinator = _gameManager.GetComponentInChildren<SpawnCoordinator>();
            if (spawnCoordinator != null) return spawnCoordinator;
            
            // Try to find by name
            return GameObject.FindObjectOfType<SpawnCoordinator>();
        }

        private void ApplyMapIfNeeded()
        {
            if (string.IsNullOrEmpty(_currentScenario.MapImage)) return;
            
            // Load map texture
            var mapTexture = _loader?.LoadMap(_currentScenario.MapImage);
            if (mapTexture != null)
            {
                // Pass to EnvironmentCreator if available
                var envCreator = _gameManager?.GetComponentInChildren<EnvironmentCreatorFromDataset>();
                if (envCreator != null)
                {
                    // Assuming EnvironmentCreatorFromDataset has a LoadFromTexture method
                    var method = envCreator.GetType().GetMethod("LoadFromTexture");
                    method?.Invoke(envCreator, new object[] { mapTexture });
                    
                    if (_logEvents)
                    {
                        Debug.Log($"[ScenarioApplier] Map loaded: {_currentScenario.MapImage}");
                    }
                }
            }
        }

        #endregion

        #region Editor Utilities

        #if UNITY_EDITOR
        
        [ContextMenu("Test Apply with Fallback")]
        private void EditorTestApplyFallback()
        {
            if (_loader == null)
            {
                Debug.LogError("[ScenarioApplier] No loader set");
                return;
            }
            
            var fallback = _loader.CreateMinimalScenario();
            var gameManager = FindObjectOfType<GameManager>();
            
            if (gameManager != null)
            {
                StartCoroutine(ApplyScenario(gameManager, fallback));
            }
        }
        
        #endif

        #endregion
    }
}