// Scripts/RobotSNAP/Scenario/ScenarioApplier.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RobotSNAP.Core;
using RobotSNAP.Environment;
using RobotSNAP.Agents;

namespace RobotSNAP.Core.Scenario
{
    /// <summary>
    /// Applies a YAML scenario to a GameManager.
    /// Handles map building (via EnvironmentBuilder), robot positioning (position + yaw),
    /// human configuration (via HumanPoolManager), goals, formations, and behaviors.
    /// </summary>
    public sealed class ScenarioApplier : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ScenarioLoader _loader;
        [SerializeField] private EnvironmentBuilder _environmentBuilder;

        [Header("Prefabs")]
        [SerializeField] private GameObject _robotPrefab; 

        [Header("Settings")]
        [SerializeField] private bool _clearExistingHumans = true;
        [SerializeField] private bool _resetRobotPosition = true;
        [SerializeField] private bool _logEvents = true;

        private GameManager _gameManager;
        private ScenarioData _currentScenario;
        private readonly List<Coroutine> _activeCoroutines = new();
        private readonly Dictionary<string, HumanAgent> _spawnedHumans = new();
        private int _humanCounter;

        public ScenarioData CurrentScenario => _currentScenario;
        public bool IsApplying { get; private set; }

        public event Action<ScenarioData> OnApplicationStarted;
        public event Action<ScenarioData> OnApplicationCompleted;
        public event Action<string> OnApplicationError;

        public void SetLoader(ScenarioLoader loader) => _loader = loader;

        // ==========================================
        //          PUBLIC API
        // ==========================================

        /// <summary>
        /// Applies the given scenario to the specified GameManager.
        /// </summary>
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
            if (!scenario.IsValid(out string validationError))
            {
                OnApplicationError?.Invoke($"Invalid scenario: {validationError}");
                yield break;
            }

            _gameManager = gameManager;
            _currentScenario = scenario;
            IsApplying = true;

            OnApplicationStarted?.Invoke(scenario);
            if (_logEvents) Debug.Log($"[ScenarioApplier] Applying scenario: {scenario.Name}");

            yield return StartCoroutine(ApplyScenarioCoroutine());

            IsApplying = false;
            OnApplicationCompleted?.Invoke(scenario);
            if (_logEvents) Debug.Log($"[ScenarioApplier] Successfully applied scenario: {scenario.Name}");
        }

        /// <summary>
        /// Stops all active behavior coroutines (e.g., wander, follow).
        /// </summary>
        public void StopAllBehaviours()
        {
            foreach (var coroutine in _activeCoroutines)
                if (coroutine != null) StopCoroutine(coroutine);
            _activeCoroutines.Clear();
        }

        /// <summary>
        /// Clears the internal tracking of spawned humans.
        /// </summary>
        public void ClearTrackedHumans()
        {
            _spawnedHumans.Clear();
            _humanCounter = 0;
        }

        // ==========================================
        //          APPLICATION COROUTINE
        // ==========================================

        private IEnumerator ApplyScenarioCoroutine()
        {
            // 1. Build the map (if needed). Currently disabled in favor of external build.
            // yield return StartCoroutine(BuildMapFromScenario());

            // 2. Clear existing humans (return them to the pool)
            if (_clearExistingHumans)
            {
                yield return StartCoroutine(ClearExistingHumans());
                ClearTrackedHumans();
            }

            // 3. Wait until the GameManager is ready (NavMesh, pool, etc.)
            yield return StartCoroutine(WaitForGameManagerReady());

            // 4. Apply simulation configuration (seed, time scale, duration)
            ApplySimulationConfig();

            // 5. Setup the robot (position, rotation, goal, behavior, speed)
            yield return StartCoroutine(SetupRobot());

            // 6. Setup humans via the pool
            yield return StartCoroutine(SetupHumans());

            if (_logEvents) Debug.Log($"[ScenarioApplier] Scenario application complete: {_currentScenario.Name}");
        }

        // ==========================================
        //          MAP BUILDING (OPTIONAL)
        // ==========================================

        private IEnumerator BuildMapFromScenario()
        {
            if (string.IsNullOrEmpty(_currentScenario.MapImage))
            {
                if (_logEvents) Debug.Log("[ScenarioApplier] No map specified in scenario, skipping map build.");
                yield break;
            }

            if (_environmentBuilder == null)
            {
                OnApplicationError?.Invoke("GridEnvironmentBuilder not assigned, cannot build map.");
                yield break;
            }

            if (_loader.LoadMapData(_currentScenario.MapImage, out Texture2D texture, out Bounds bounds))
            {
                if (texture == null)
                {
                    OnApplicationError?.Invoke($"Failed to load map texture: {_currentScenario.MapImage}");
                    yield break;
                }
                _environmentBuilder.BuildFromTexture(texture, bounds);
            }
            yield return null;
        }

        // ==========================================
        //          CLEANUP & WAITING
        // ==========================================

        private IEnumerator ClearExistingHumans()
        {
            var poolManager = _gameManager?.HumanPool;
            if (poolManager != null)
            {
                poolManager.ReturnAllHumans();
                if (_logEvents) Debug.Log("[ScenarioApplier] Cleared all humans via pool.");
            }
            else
            {
                var humans = FindObjectsOfType<HumanAgent>();
                foreach (var h in humans)
                    Destroy(h.gameObject);
                if (_logEvents) Debug.Log($"[ScenarioApplier] Destroyed {humans.Length} humans directly.");
            }
            yield return null;
        }

        private IEnumerator WaitForGameManagerReady()
        {
            while (!_gameManager.IsInitialized)
                yield return null;
            yield return new WaitForSeconds(0.1f);
        }

        // ==========================================
        //          CONFIGURATION
        // ==========================================

        private void ApplySimulationConfig()
        {
            var globalConfig = Supervisor.Instance?.ActiveConfig;
            if (globalConfig == null) return;

            int seed = globalConfig.RandomSeed == -1 ? System.Environment.TickCount : globalConfig.RandomSeed;
            UnityEngine.Random.InitState(seed);
            Time.timeScale = globalConfig.TimeScale;

            float duration = _currentScenario.Duration;
            if (duration > 0)
            {
                StartCoroutine(HandleSimulationDuration(duration));
            }

            if (_logEvents)
                Debug.Log($"[ScenarioApplier] Simulation config: seed={seed}, duration={duration}s, timeScale={globalConfig.TimeScale}");
        }

        private IEnumerator HandleSimulationDuration(float duration)
        {
            yield return new WaitForSeconds(duration);
            _gameManager?.NotifyScenarioDurationReached();
            Supervisor.Instance?.Pause();
            if (_logEvents) Debug.Log($"[ScenarioApplier] Duration {duration}s reached. Simulation paused.");
        }

        // ==========================================
        //          ROBOT SETUP
        // ==========================================

        private IEnumerator SetupRobot()
        {
            var robot = FindRobot();
            if (robot == null)
            {
                if (_robotPrefab == null)
                {
                    OnApplicationError?.Invoke("Robot prefab not assigned and no robot found in scene.");
                    yield break;
                }
                robot = Instantiate(_robotPrefab);
                robot.transform.parent = _gameManager.transform;
                robot.tag = "Robot";
                robot.name = "Robot";
                if (_logEvents) Debug.Log("[ScenarioApplier] Created new robot from prefab.");
            }

            var robotConfig = _currentScenario.Robot;
            if (robotConfig == null)
            {
                OnApplicationError?.Invoke("Robot config missing");
                yield break;
            }

            if (_resetRobotPosition)
            {
                // Resolve position AND rotation (yaw) from the start reference
                var (startPos, startRot) = _loader.GetPositionAndRotation(_currentScenario, robotConfig.StartRef);
                var robotComponent = robot.GetComponent<Robot>();
                if (robotComponent != null)
                    robotComponent.Reset();

                if (robotComponent != null)
                {
                    robotComponent.SetBaseLinkPose(startPos, startRot);
                }
                else
                {
                    // Fallback
                    robot.transform.position = startPos;
                    robot.transform.rotation = startRot;
                }
                if (_logEvents) 
                    Debug.Log($"[ScenarioApplier] Robot position set to {startPos}, rotation yaw: {startRot.eulerAngles.y}");
            }

            // Set goal (position only), behavior, and speed
            Vector3 goalPos = ResolvePosition(robotConfig.GoalRef);
            SetRobotGoal(robot, goalPos);
            SetRobotBehavior(robot, robotConfig.Behavior);
            SetRobotSpeed(robot, robotConfig.Speed);

            yield return null;
        }

        private GameObject FindRobot()
        {
            var robotComp = _gameManager.GetComponentInChildren<Robot>();
            if (robotComp != null) return robotComp.gameObject;
            return null;
            // return GameObject.FindGameObjectWithTag("Robot");
        }

        private void SetRobotGoal(GameObject robot, Vector3 goal)
        {
            var comp = robot.GetComponent<Robot>();
            comp?.SetGoal(goal);
        }

        private void SetRobotBehavior(GameObject robot, string behavior)
        {
            var comp = robot.GetComponent<Robot>();
            comp?.SetBehavior(behavior);
        }

        private void SetRobotSpeed(GameObject robot, float speed)
        {
            var comp = robot.GetComponent<Robot>();
            comp?.SetSpeed(speed);
        }

        // ==========================================
        //          HUMAN SETUP
        // ==========================================

        private IEnumerator SetupHumans()
        {
            Debug.Log($"[ScenarioApplier] Setting up humans for scenario: {_currentScenario.Name}");
            if (_currentScenario.Humans == null || _currentScenario.Humans.Count == 0)
            {
                if (_logEvents) Debug.Log("[ScenarioApplier] No humans to spawn");
                yield break;
            }

            var poolManager = _gameManager?.HumanPool;
            if (poolManager == null)
            {
                OnApplicationError?.Invoke("HumanPoolManager not found in GameManager");
                yield break;
            }

            // Calculate total number of humans
            int totalHumans = 0;
            foreach (var config in _currentScenario.Humans)
                totalHumans += config.Count;

            if (totalHumans == 0) yield break;

            // Get instances from the pool
            List<HumanAgent> allHumans = new List<HumanAgent>();
            for (int i = 0; i < totalHumans; i++)
            {
                GameObject humanGO = poolManager.GetHuman();
                if (humanGO != null)
                {
                    humanGO.SetActive(true);
                    var human = humanGO.GetComponent<HumanAgent>();
                    if (human != null)
                        allHumans.Add(human);
                }
            }

            yield return null; // let Unity stabilize

            // Configure each human according to its YAML definition
            int humanIndex = 0;
            foreach (var config in _currentScenario.Humans)
            {
                for (int i = 0; i < config.Count && humanIndex < allHumans.Count; i++)
                {
                    var human = allHumans[humanIndex];
                    if (human != null)
                    {
                        ConfigureHuman(human, config, i, config.Count);
                        _spawnedHumans[$"{config.Id}_{_humanCounter++}"] = human;
                    }
                    humanIndex++;
                }
            }

            var robot = FindRobot();
            if (robot != null)
            {
                var robotComp = robot.GetComponent<Robot>();
                if (robotComp != null){
                    robotComp.GetAgentDetector().SetAgentPool(poolManager.GetPoolParent().gameObject);
                }

            }

            if (_logEvents) Debug.Log($"[ScenarioApplier] Configured {humanIndex} humans");
        }

        // ==========================================
        //          INDIVIDUAL HUMAN CONFIGURATION
        // ==========================================

        private void ConfigureHuman(HumanAgent human, HumanScenarioConfig config, int index, int total)
        {
            // --- Spawn Position ---
            if (config.Spawn != null)
            {
                Vector3 spawnPos = ResolveSpawnPosition(config.Spawn, index, total);
                if (spawnPos != Vector3.zero)
                    human.transform.position = spawnPos;
            }

            // --- Goal ---
            if (config.Goal != null)
                ResolveAndSetGoal(human, config.Goal);

            // --- Basic Parameters ---
            human.SetSpeed(config.Speed);
            if (!string.IsNullOrEmpty(config.Behavior))
                human.SetBehavior(config.Behavior);

            // --- Color ---
            if (config.Color != null && config.Color.Length >= 3)
                SetHumanColor(human, new Color(config.Color[0], config.Color[1], config.Color[2]));

            // --- Personality (SFM parameters) ---
            if (config.Personality != null)
                SetHumanPersonality(human, config.Personality);

            // --- Movement Controller (SFM, ONNX, Hybrid) ---
            if (config.MovementController != null)
                SetHumanMovementController(human, config.MovementController);
        }

        // ==========================================
        //          FLEXIBLE RESOLUTION HELPERS
        // ==========================================

        /// <summary>
        /// Resolves a spawn position from a SpawnConfig.
        /// Supports: 'ref' (legacy), direct 'position', or 'zone'.
        /// Handles 'point', 'random', and 'formation' types.
        /// </summary>
        private Vector3 ResolveSpawnPosition(SpawnConfig spawn, int index, int total)
        {
            // PRIORITY 1: Use a named reference (legacy/backward compatible)
            if (!string.IsNullOrEmpty(spawn.Reference))
            {
                switch (spawn.Type?.ToLower())
                {
                    case "point":
                        return ResolvePosition(spawn.Reference);

                    case "random":
                        Bounds refBounds = ResolveBounds(spawn.Reference);
                        return new Vector3(
                            UnityEngine.Random.Range(refBounds.min.x, refBounds.max.x),
                            refBounds.center.y,
                            UnityEngine.Random.Range(refBounds.min.z, refBounds.max.z)
                        );

                    case "formation":
                        Vector3 center = GetFormationCenter(spawn);
                        if (spawn.Formation == "line")
                        {
                            float offset = (index - (total - 1) / 2f) * spawn.Spacing;
                            return center + new Vector3(offset, 0, 0);
                        }
                        if (spawn.Formation == "circle")
                        {
                            float angle = (index / (float)total) * Mathf.PI * 2;
                            return center + new Vector3(Mathf.Cos(angle) * spawn.Spacing, 0, Mathf.Sin(angle) * spawn.Spacing);
                        }
                        break;
                }
                return Vector3.zero;
            }

            // PRIORITY 2: Direct position
            if (spawn.Position != null)
                return spawn.Position.ToVector3();

            // PRIORITY 3: Zone definition (center + size)
            if (spawn.Zone != null && spawn.Zone.IsBounds)
            {
                switch (spawn.Type?.ToLower())
                {
                    case "point":
                        return spawn.Zone.Center.ToVector3();

                    case "random":
                        Bounds zoneBounds = spawn.Zone.ToBounds();
                        return new Vector3(
                            UnityEngine.Random.Range(zoneBounds.min.x, zoneBounds.max.x),
                            zoneBounds.center.y,
                            UnityEngine.Random.Range(zoneBounds.min.z, zoneBounds.max.z)
                        );

                    case "formation":
                        // Use the zone center as the formation anchor
                        Vector3 formationBase = spawn.Zone.Center.ToVector3();
                        // If relative_to is provided, we could offset, but for simplicity we use the center.
                        // For a full implementation, we would check spawn.RelativeTo here.
                        return formationBase;

                    default:
                        return spawn.Zone.Center.ToVector3();
                }
            }

            return Vector3.zero;
        }

        /// <summary>
        /// Resolves a goal position from a GoalConfig.
        /// Supports: 'ref' (legacy), direct 'position', or 'zone' (takes the center).
        /// </summary>
        private Vector3 ResolveGoalPosition(GoalConfig goal)
        {
            if (!string.IsNullOrEmpty(goal.Reference))
                return _loader?.GetPosition(_currentScenario, goal.Reference) ?? Vector3.zero;

            if (goal.Position != null)
                return goal.Position.ToVector3();

            if (goal.Zone != null && goal.Zone.IsBounds)
                return goal.Zone.Center.ToVector3();

            return Vector3.zero;
        }

        /// <summary>
        /// Resolves a Bounds from a GoalConfig (for random goals).
        /// Supports: 'ref' (legacy) or direct 'zone'.
        /// </summary>
        private Bounds ResolveBoundsFromGoal(GoalConfig goal)
        {
            if (!string.IsNullOrEmpty(goal.Reference))
                return _loader?.GetBounds(_currentScenario, goal.Reference) ?? new Bounds();

            if (goal.Zone != null && goal.Zone.IsBounds)
                return goal.Zone.ToBounds();

            return new Bounds();
        }

        // ==========================================
        //          GOAL RESOLUTION & BEHAVIORS
        // ==========================================

        private void ResolveAndSetGoal(HumanAgent human, GoalConfig goal)
        {
            switch (goal.Type?.ToLower())
            {
                case "point":
                    human.SetGoal(ResolveGoalPosition(goal));
                    break;

                case "random":
                    Bounds bounds = ResolveBoundsFromGoal(goal);
                    Vector3 randomGoal = new Vector3(
                        UnityEngine.Random.Range(bounds.min.x, bounds.max.x),
                        bounds.center.y,
                        UnityEngine.Random.Range(bounds.min.z, bounds.max.z)
                    );
                    human.SetGoal(randomGoal);
                    break;

                case "wander":
                    _activeCoroutines.Add(StartCoroutine(WanderRoutine(human, goal.Radius)));
                    break;

                case "follow":
                    if (goal.Target == "robot")
                        _activeCoroutines.Add(StartCoroutine(FollowRobotRoutine(human)));
                    break;

                case "stay":
                    human.SetGoal(human.transform.position);
                    break;

                default:
                    if (_logEvents) Debug.LogWarning($"[ScenarioApplier] Unknown goal type: {goal.Type}");
                    break;
            }
        }

        // ==========================================
        //          BEHAVIOR COROUTINES
        // ==========================================

        private IEnumerator WanderRoutine(HumanAgent human, float radius)
        {
            while (human != null && human.gameObject.activeSelf)
            {
                Vector3 randomOffset = UnityEngine.Random.insideUnitSphere * radius;
                randomOffset.y = 0;
                human.SetGoal(human.transform.position + randomOffset);
                yield return new WaitForSeconds(UnityEngine.Random.Range(3f, 7f));
            }
        }

        private IEnumerator FollowRobotRoutine(HumanAgent human)
        {
            while (human != null && human.gameObject.activeSelf)
            {
                var robot = FindRobot();
                if (robot != null)
                    human.SetGoal(robot.transform.position);
                yield return new WaitForSeconds(0.3f);
            }
        }

        // ==========================================
        //          UTILITY METHODS (RESOLVE, COLOR, PERSONALITY)
        // ==========================================

        private Vector3 ResolvePosition(string reference) =>
            _loader?.GetPosition(_currentScenario, reference) ?? Vector3.zero;

        private Bounds ResolveBounds(string reference) =>
            _loader?.GetBounds(_currentScenario, reference) ?? new Bounds();

        private Vector3 GetFormationCenter(SpawnConfig spawn)
        {
            if (spawn.RelativeTo == "robot")
            {
                var robot = FindRobot();
                if (robot != null) return robot.transform.position;
            }
            else if (!string.IsNullOrEmpty(spawn.RelativeTo))
                return ResolvePosition(spawn.RelativeTo);
            return Vector3.zero;
        }

        private void SetHumanColor(HumanAgent human, Color color)
        {
            var renderer = human.GetComponentInChildren<Renderer>();
            if (renderer != null) renderer.material.color = color;
        }

        private void SetHumanPersonality(HumanAgent human, PersonalityConfig personality)
        {
            human.SetAssertiveness(personality.Assertiveness);
            human.SetPersonalSpace(personality.PersonalSpace);
            human.SetReactionTime(personality.ReactionTime);
        }

        private void SetHumanMovementController(HumanAgent human, MovementControllerConfig movementConfig)
        {
            var movement = human.GetComponent<HumanMovement>();
            if (movement == null) return;

            int controllerType = 0;
            switch (movementConfig.Type?.ToLower())
            {
                case "sfm": controllerType = 0; break;
                case "onnx": controllerType = 1; break;
                case "hybrid": controllerType = 2; break;
                default: controllerType = 0; break;
            }
            movement.SetControllerType(controllerType);

            // Log advanced SFM parameters (not yet applied)
            if (movementConfig.SFMParameters != null && _logEvents)
            {
                Debug.Log($"[ScenarioApplier] SFM advanced parameters provided but not yet applied to {human.name}. " +
                          $"Values: ForceStrength={movementConfig.SFMParameters.ForceStrength}, " +
                          $"SocialForce={movementConfig.SFMParameters.SocialForce}, etc.");
            }
        }
    }
}