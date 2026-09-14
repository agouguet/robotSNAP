// Scripts/RobotSNAP/Scenario/ScenarioApplier.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
            // The scenario is over: its walkable grid must not outlive it, or the next scenario would plan on
            // the previous map until its own grid is built.
            ScenarioNavigation.Clear();
        }

        /// <summary>
        /// Clears the internal tracking of spawned humans.
        /// </summary>
        public void ClearTrackedHumans()
        {
            _spawnedHumans.Clear();
            _humanCounter = 0;
        }

        /// <summary>Drops the walkable grid of the scenario that was just unloaded.</summary>
        public void ClearNavigationGrid() => ScenarioNavigation.Clear();

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

            // 4b. Build the walkable grid of the scenario. The editor plans and validates its trajectories on
            // that same grid, so loading it before anything spawns is what makes the simulation walk the routes
            // the author drew — and what lets the spawn policy pull a formation out of a wall.
            yield return StartCoroutine(LoadNavigationGrid());

            // 5. Setup the robot (position, rotation, goal, behavior, speed)
            yield return StartCoroutine(SetupRobot());

            // 6. Setup humans via the pool
            yield return StartCoroutine(SetupHumans());

            if (_logEvents) Debug.Log($"[ScenarioApplier] Scenario application complete: {_currentScenario.Name}");
        }

        // ==========================================
        //          NAVIGATION GRID
        // ==========================================

        /// <summary>
        /// Loads the occupancy image of the scenario and builds the walkable grid the agents plan on.
        /// The image is only sampled: the texture is released right away, so a large map does not stay in
        /// memory for the whole simulation.
        /// </summary>
        private IEnumerator LoadNavigationGrid()
        {
            ScenarioNavigation.Clear();

            string map = _currentScenario?.MapImage;
            if (string.IsNullOrEmpty(map))
            {
                if (_logEvents)
                    Debug.Log("[ScenarioApplier] Scenario has no occupancy map: navigation falls back on the scene NavMesh.");
                yield break;
            }

            if (_loader == null)
            {
                Debug.LogWarning("[ScenarioApplier] No scenario loader: the walkable grid cannot be built.");
                yield break;
            }

            if (!_loader.LoadMapData(map, out Texture2D texture, out Bounds bounds) || texture == null)
            {
                Debug.LogWarning($"[ScenarioApplier] Occupancy image '{map}' could not be loaded: " +
                                 "navigation falls back on the scene NavMesh.");
                yield break;
            }

            try
            {
                ScenarioNavigation.BuildFrom(texture, bounds);
            }
            finally
            {
                Destroy(texture);
            }

            if (_logEvents)
                Debug.Log($"[ScenarioApplier] Navigation grid '{map}' built: " +
                          $"{ScenarioNavigation.Walkable.Width}×{ScenarioNavigation.Walkable.Height} cells " +
                          $"({ScenarioNavigation.Resolution} max), clearance {ScenarioNavigation.DefaultAgentRadius} m.");

            yield return null;
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
                // Same image, same sampling, same planner as the editor: the runtime then walks the very
                // trajectories the scenario author validated on the map.
                ScenarioNavigation.BuildFrom(texture, bounds);
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
                var humans = FindObjectsByType<HumanAgent>(FindObjectsSortMode.None);
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

            int seed = SimulationRuntimeSettings.Apply(globalConfig, System.Environment.TickCount);

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

            // Set the ordered route while keeping legacy single-goal scenarios valid.
            var robotRoute = new List<Vector3>();
            if (robotConfig.WaypointRefs != null)
            {
                foreach (string waypointRef in robotConfig.WaypointRefs)
                {
                    if (!string.IsNullOrWhiteSpace(waypointRef))
                        robotRoute.Add(ResolvePosition(waypointRef));
                }
            }
            robotRoute.Add(ResolvePosition(robotConfig.GoalRef));
            SetRobotGoals(robot, robotRoute);
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

        private void SetRobotGoals(GameObject robot, IEnumerable<Vector3> goals)
        {
            var comp = robot.GetComponent<Robot>();
            comp?.SetGoals(goals);
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

            // Configure each human according to its YAML definition.
            // Humans sharing a group id are collected so they can walk together afterwards.
            int humanIndex = 0;
            var groups = new Dictionary<string, HumanGroup>();
            var groupsWithRoute = new HashSet<string>();
            Dictionary<string, SpawnPlanner.GroupLayout> groupLayouts =
                SpawnPlanner.ResolveGroupLayouts(_currentScenario.Humans);
            foreach (var config in _currentScenario.Humans)
            {
                for (int i = 0; i < config.Count && humanIndex < allHumans.Count; i++)
                {
                    var human = allHumans[humanIndex];
                    if (human != null)
                    {
                        ConfigureHuman(human, config, i, config.Count, poolManager);
                        if (!string.IsNullOrWhiteSpace(config.Group))
                        {
                            string groupId = config.Group.Trim();
                            if (!groups.TryGetValue(groupId, out HumanGroup group))
                            {
                                SpawnPlanner.GroupLayout layout = groupLayouts.TryGetValue(
                                    groupId,
                                    out SpawnPlanner.GroupLayout found)
                                    ? found
                                    : new SpawnPlanner.GroupLayout(null, SpawnPlanner.DefaultSpacing, hasFormation: false);
                                group = new HumanGroup(
                                    groupId,
                                    layout.Spacing,
                                    layout.HasFormation ? layout.Formation : null,
                                    layout.Parameter);
                                groups[groupId] = group;
                            }

                            // The group walks one shared route: the first entry of the group defines it, and
                            // every member holds a slot around its reference point instead of walking alone.
                            if (groupsWithRoute.Add(groupId))
                            {
                                group.SetRoute(
                                    PlanGroupRoute(human, groupId),
                                    config.Speed,
                                    HumanEndBehaviorParser.Parse(config.EndBehavior));
                            }

                            group.Add(human);
                        }
                        _spawnedHumans[$"{config.Id}_{_humanCounter++}"] = human;
                    }
                    humanIndex++;
                }
            }

            // Lay every group out around its leader, facing the direction it is about to walk.
            foreach (HumanGroup group in groups.Values)
                group.PlaceMembersAtSpawn();

            // The anchor being on walkable ground does not mean the formation is: check every member.
            VerifySpawnedFormation(allHumans);

            var robot = FindRobot();
            if (robot != null)
            {
                var robotComp = robot.GetComponent<Robot>();
                if (robotComp != null){
                    robotComp.GetAgentDetector().SetAgentPool(poolManager.GetPoolParent().gameObject);
                }

            }

            if (_logEvents)
                Debug.Log($"[ScenarioApplier] Configured {humanIndex} humans across {groups.Count} group(s)");
        }

        /// <summary>
        /// Shared route of a group, planned on the walkable grid of the scenario with the very planner the
        /// editor drew it with: the group walks around walls instead of through them, and it walks exactly the
        /// polyline the author validated.
        /// </summary>
        private List<Vector2> PlanGroupRoute(HumanAgent human, string groupId)
        {
            var authored = new List<Vector2>(human.RoutePoints.Count);
            foreach (Vector3 point in human.RoutePoints)
                authored.Add(new Vector2(point.x, point.z));

            List<Vector2> route = ScenarioNavigation.PlanRoute(authored, out int fallbackSegments);
            if (fallbackSegments > 0)
            {
                Debug.LogWarning(
                    $"[ScenarioApplier] Group '{groupId}': {fallbackSegments} leg(s) of the route could not be " +
                    "planned on the walkable grid and are walked in a straight line. " +
                    "Check the route points and the occupancy image of the scenario.");
            }

            return route.Count > 0 ? route : authored;
        }

        /// <summary>
        /// Checks every spawned agent, not just the formation anchor: a five-agent wedge laid out around a
        /// valid anchor can put three of them inside the wall next to it, especially on a map with no NavMesh
        /// for the projection to fall back on. Anybody still off walkable ground is pulled back onto it and
        /// reported, instead of silently walking into a wall.
        /// </summary>
        private void VerifySpawnedFormation(List<HumanAgent> humans)
        {
            if (!ScenarioNavigation.IsAvailable || humans == null)
                return;

            int offWalkable = 0;
            string firstOffender = string.Empty;
            foreach (HumanAgent human in humans)
            {
                if (human == null)
                    continue;

                Vector2 position = human.Position2D;
                if (ScenarioNavigation.IsWalkable(position))
                    continue;

                offWalkable++;
                if (firstOffender.Length == 0)
                    firstOffender = $"({position.x:0.##}, {position.y:0.##})";

                if (ScenarioNavigation.TryProjectToWalkable(
                        position,
                        ScenarioNavigation.SnapRadius,
                        out Vector2 walkable))
                {
                    human.transform.position = new Vector3(
                        walkable.x,
                        human.transform.position.y,
                        walkable.y);
                }
            }

            if (offWalkable == 0)
            {
                if (_logEvents)
                    Debug.Log($"[ScenarioApplier] Spawn check: {humans.Count} agent(s), every formation on walkable ground.");
                return;
            }

            Debug.LogWarning(
                $"[ScenarioApplier] Spawn check: {offWalkable} of {humans.Count} agent(s) spawn off walkable ground, " +
                $"first at {firstOffender}. They were moved to the nearest walkable cell: move the start of the route " +
                "or tighten the formation in the scenario editor.");
        }

        // ==========================================
        //          INDIVIDUAL HUMAN CONFIGURATION
        // ==========================================

        private void ConfigureHuman(
            HumanAgent human,
            HumanScenarioConfig config,
            int index,
            int total,
            HumanPoolManager poolManager)
        {
            human.SetPoolManager(poolManager);
            human.SetEndBehavior(HumanEndBehaviorParser.Parse(config.EndBehavior));

            // --- Ordered route: the spawn point first, then every goal until the last one ---
            var goals = new List<Vector3>();
            if (IsPointGoal(config.Goal))
            {
                goals.Add(ResolveGoalPosition(config.Goal));
                if (config.Goals != null)
                    goals.AddRange(config.Goals.Where(IsPointGoal).Select(ResolveGoalPosition));
            }

            // --- Spawn Position ---
            Vector3 spawnPosition = human.transform.position;
            if (config.Spawn != null)
            {
                spawnPosition = ResolveSpawnPosition(config, index, total, goals);
                human.transform.position = spawnPosition;
            }

            if (goals.Count > 0)
            {
                var route = new List<Vector3>(goals.Count + 1);
                if (config.Spawn != null)
                    route.Add(spawnPosition);
                route.AddRange(goals);
                human.SetGoals(route);

                // Face the first objective from the very first frame, instead of the prefab's own direction.
                human.FaceTowards(new Vector2(
                    goals[0].x - spawnPosition.x,
                    goals[0].z - spawnPosition.z));
            }
            else if (config.Goal != null)
            {
                ResolveAndSetGoal(human, config.Goal);
            }

            // --- Basic Parameters ---
            // Speed and controller both live in the HumanConfig the controllers read, so they are applied
            // together: writing BaseAgent.desiredSpeed alone left the editor's Speed value without effect.
            human.ApplyScenarioMovement(config.Speed, config.MovementController?.Type);

            // --- Color ---
            if (config.Color != null && config.Color.Length >= 3)
                SetHumanColor(human, new Color(config.Color[0], config.Color[1], config.Color[2]));

            // --- Personality (SFM parameters) ---
            if (config.Personality != null)
                SetHumanPersonality(human, config.Personality);

        }

        // ==========================================
        //          FLEXIBLE RESOLUTION HELPERS
        // ==========================================

        /// <summary>
        /// Spawn policy, evaluated per human config.
        ///
        /// <para><b>Grouped route</b> — every member takes the same anchor, snapped onto the NavMesh.
        /// The cluster is not spread here: <see cref="HumanGroup.PlaceMembersAtSpawn"/> lays the members
        /// out on their formation slots around the leader once the whole group exists, so a group always
        /// appears as one compact bloc.</para>
        ///
        /// <para><b>Ungrouped route with several agents</b> — the anchor is snapped onto the NavMesh and
        /// the agents are placed on formation slots around it, each projection bounded to half a spacing
        /// so nobody is pushed into the next room by the NavMesh sampling.</para>
        ///
        /// <para><b>Random spawn</b> — a random point inside the zone, snapped onto the NavMesh so agents
        /// never start inside a wall.</para>
        /// </summary>
        private Vector3 ResolveSpawnPosition(
            HumanScenarioConfig config,
            int index,
            int total,
            IList<Vector3> goals)
        {
            SpawnConfig spawn = config.Spawn;
            if (spawn == null)
                return Vector3.zero;

            // Group members share the leader's anchor; the formation is applied after the whole group is built.
            if (!string.IsNullOrWhiteSpace(config.Group))
                return SpawnPlanner.PlaceAnchor(ResolveSpawnAnchor(spawn));

            if (string.Equals(spawn.Type, "random", StringComparison.OrdinalIgnoreCase))
                // A random point sits anywhere in its zone: allow a wider search to reach walkable ground.
                return SpawnPlacement.SnapToNavMesh(ResolveRandomSpawn(spawn), 1.5f, 5f);

            Vector3 anchor = SpawnPlanner.PlaceAnchor(ResolveSpawnAnchor(spawn));
            if (total <= 1)
                return anchor;

            float heading = SpawnPlanner.HeadingTowards(new Vector2(anchor.x, anchor.z), goals);
            Vector2 offset = SpawnPlanner.SlotOffset(
                index,
                total,
                spawn.Spacing,
                spawn.Formation,
                heading,
                spawn.FormationParameter);
            return SpawnPlanner.PlaceSlot(anchor, offset, spawn.Spacing);
        }

        /// <summary>Single anchor of a spawn definition: named reference, direct position or zone center.</summary>
        private Vector3 ResolveSpawnAnchor(SpawnConfig spawn)
        {
            if (!string.IsNullOrEmpty(spawn.Reference))
                return ResolvePosition(spawn.Reference);
            if (spawn.Position != null)
                return spawn.Position.ToVector3();
            if (spawn.Zone != null && spawn.Zone.IsBounds)
                return spawn.Zone.Center.ToVector3();
            return Vector3.zero;
        }

        private Vector3 ResolveRandomSpawn(SpawnConfig spawn)
        {
            if (!string.IsNullOrEmpty(spawn.Reference))
            {
                Bounds referenceBounds = ResolveBounds(spawn.Reference);
                if (referenceBounds.size != Vector3.zero)
                    return RandomPointIn(referenceBounds);
            }

            if (spawn.Zone != null && spawn.Zone.IsBounds)
                return RandomPointIn(spawn.Zone.ToBounds());

            return ResolveSpawnAnchor(spawn);
        }

        private static Vector3 RandomPointIn(Bounds bounds) => new Vector3(
            UnityEngine.Random.Range(bounds.min.x, bounds.max.x),
            bounds.center.y,
            UnityEngine.Random.Range(bounds.min.z, bounds.max.z));

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

        private static bool IsPointGoal(GoalConfig goal)
        {
            return goal != null && (string.IsNullOrWhiteSpace(goal.Type) ||
                                    string.Equals(goal.Type, "point", StringComparison.OrdinalIgnoreCase));
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
                    var goalPos = ResolveGoalPosition(goal);
                    human.SetGoal(goalPos);
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

    }
}
