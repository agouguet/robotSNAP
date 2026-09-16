// Scripts/RobotSNAP/Scenario/ScenarioApplier.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
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
        /// <summary>
        /// How far a drawn point may be pulled to reach navigable ground. A zone is sampled rather than
        /// authored, so a draw can land deep inside a wall and the search reaches further than the one an
        /// authored anchor gets.
        /// </summary>
        private const float DrawnPointSearchRadius = 5f;

        [Header("References")]
        [SerializeField] private ScenarioLoader _loader;

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

        /// <summary>
        /// Random spawn anchors already drawn for the scenario being applied, so a single draw serves every
        /// agent of the route that shares it: the whole formation then appears around one point. Keyed by the
        /// route, which is the walking unit.
        /// </summary>
        private readonly Dictionary<HumanScenarioConfig, Vector3> _randomAnchors = new();

        /// <summary>
        /// Anchors already drawn for the independent agents of one route, so each new draw can keep its
        /// distance from them. Keyed by the human config, which is one route.
        /// </summary>
        private readonly Dictionary<HumanScenarioConfig, List<Vector3>> _independentAnchors = new();

        /// <summary>
        /// Arrival points already drawn for one random goal of an independent route, so the agents of that route
        /// do not all converge on the same pixel. Keyed by the goal definition, which is one objective.
        /// </summary>
        private readonly Dictionary<GoalConfig, List<Vector3>> _independentGoals = new();

        /// <summary>How many draws an independent agent gets to find a spot clear of the agents already placed.</summary>
        private const int IndependentDrawAttempts = 12;

        /// <summary>
        /// Spread of the walking speed of one independent agent around the speed its route authored. Pedestrians
        /// do not share a pace, and a crowd of agents all walking at exactly 1.2 m/s reads as a marching block.
        /// </summary>
        private const float CrowdSpeedSpread = 0.15f;

        /// <summary>Average delay between two entries of a crowd, in seconds.</summary>
        private const float CrowdEntryGap = 0.6f;

        /// <summary>Longest wait of a held agent, so a huge crowd still starts within a few seconds.</summary>
        private const float CrowdEntryWindow = 6f;

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
            // 1. The map is built by ScenarioManager, through GameManager.BuildMap, before this coroutine runs:
            //    the floor, the walls and the walkable grid all come from that single build.

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

            // 4b. Report which navigation the agents will use. The environment builder already built it, from
            // the same image as the floor and the walls, before anything could spawn.
            LogNavigationSource();

            // 5. Setup the robot (position, rotation, goal, behavior, speed)
            yield return StartCoroutine(SetupRobot());

            // 6. Setup humans via the pool
            yield return StartCoroutine(SetupHumans());

            if (_logEvents) Debug.Log($"[ScenarioApplier] Scenario application complete: {_currentScenario.Name}");
        }

        // ==========================================
        //          NAVIGATION SOURCE
        // ==========================================

        /// <summary>
        /// The map of a scenario is built in one place, <see cref="EnvironmentBuilder.BuildEnvironment"/>: it
        /// creates the floor, the walls and the walkable grid from the very same occupancy image. A map that is
        /// a prefab or an additive scene brings its own geometry and leaves no grid, and the agents then follow
        /// the scene NavMesh.
        /// </summary>
        private void LogNavigationSource()
        {
            if (!_logEvents)
                return;

            if (ScenarioNavigation.IsAvailable)
            {
                Debug.Log($"[ScenarioApplier] Walkable grid ready: " +
                          $"{ScenarioNavigation.Walkable.Width}×{ScenarioNavigation.Walkable.Height} cells, " +
                          $"clearance {ScenarioNavigation.DefaultAgentRadius} m.");
                return;
            }

            Debug.Log("[ScenarioApplier] No walkable grid for this environment: " +
                      "navigation falls back on the scene NavMesh.");
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
                var humans = FindObjectsByType<HumanAgent>();
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
            //
            // One route is one walking unit. A route that carries several agents and asks for a shape walks as
            // a small group, and the route itself is that group: nothing has to declare a shared id any more.
            // A route in scatter formation is the opposite case — every agent draws its own start inside the
            // spawn area and its own arrival inside every goal area, and walks there on its own. That is what
            // turns one entry into a crowd crossing the map instead of one block moving down the middle.
            int humanIndex = 0;
            var groups = new Dictionary<string, HumanGroup>();
            var groupsWithRoute = new HashSet<string>();
            // One draw per spawn unit: a whole formation appears around a single random point, so the anchors
            // are resolved once for this scenario and then reused by every agent sharing them.
            _randomAnchors.Clear();
            _independentAnchors.Clear();
            _independentGoals.Clear();
            foreach (var config in _currentScenario.Humans)
            {
                // The route id keys the implicit group of this route, so a formation is never shared with
                // another route by accident. A scatter route has no group at all.
                string groupId = WalksAsAFormation(config) ? config.Id.Trim() : null;
                for (int i = 0; i < config.Count && humanIndex < allHumans.Count; i++)
                {
                    var human = allHumans[humanIndex];
                    if (human != null)
                    {
                        ConfigureHuman(human, config, i, config.Count, poolManager);
                        if (groupId != null)
                        {
                            if (!groups.TryGetValue(groupId, out HumanGroup group))
                            {
                                SpawnPlanner.GroupLayout layout = SpawnPlanner.ResolveLayout(config);
                                group = new HumanGroup(
                                    groupId,
                                    layout.Spacing,
                                    layout.HasFormation ? layout.Formation : null,
                                    layout.Parameter);
                                groups[groupId] = group;
                            }

                            // The group walks one shared route: the first agent of the route defines it, and
                            // every member holds a slot around its reference point instead of walking alone.
                            if (groupsWithRoute.Add(groupId))
                            {
                                group.SetRoute(
                                    PlanFormationRoute(human, groupId),
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
                Debug.Log($"[ScenarioApplier] Configured {humanIndex} humans across " +
                          $"{groups.Count} formation route(s)");
        }

        /// <summary>
        /// Shared route of a formation, planned on the walkable grid of the scenario with the very planner the
        /// editor drew it with: the group walks around walls instead of through them, and it walks exactly the
        /// polyline the author validated. One route is one formation, so <paramref name="routeId"/> only names
        /// the route in the warnings.
        /// </summary>
        private List<Vector2> PlanFormationRoute(HumanAgent human, string routeId)
        {
            var authored = new List<Vector2>(human.RoutePoints.Count);
            foreach (Vector3 point in human.RoutePoints)
                authored.Add(new Vector2(point.x, point.z));

            List<Vector2> route = ScenarioNavigation.PlanRoute(authored, out int fallbackSegments);
            if (fallbackSegments > 0)
            {
                Debug.LogWarning(
                    $"[ScenarioApplier] Route '{routeId}': {fallbackSegments} leg(s) of the route could not be " +
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
            bool independent = IsIndependentRoute(config);
            float arrivalSpacing = independent ? IndependentSpacing(config, total) : 0f;

            // A crowd is not a block: every independent agent gets its own walking speed and its own entry
            // delay, so the flow spreads out instead of stepping off the line as a single rank. Agents that
            // walk in formation keep exactly the speed the scenario authored, or they would pull the shape
            // apart.
            float speedFactor = independent ? CrowdSpeedVariation() : 1f;
            float entryHold = independent ? CrowdEntryHold(total) : 0f;

            // The hold has to be set before the route, or the agent would start walking on the frame it is
            // configured and only stop later.
            human.SetStartHold(entryHold);
            if (IsSpatialGoal(config.Goal))
            {
                // A random goal is resolved per agent: the members of a route each draw their own destination
                // inside the zone, at the moment they are configured.
                goals.Add(ResolveSpatialGoalPosition(config.Goal, independent, arrivalSpacing));
                if (config.Goals != null)
                    goals.AddRange(config.Goals.Where(IsSpatialGoal)
                        .Select(goal => ResolveSpatialGoalPosition(goal, independent, arrivalSpacing)));
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
            human.ApplyScenarioMovement(config.Speed * speedFactor, config.MovementController?.Type);

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
        /// Spawn policy, evaluated per agent of a human config.
        ///
        /// <para><b>Formation route</b> — everybody takes the same anchor, snapped onto walkable ground. The
        /// agents of a route that walks as a group are not spread here either:
        /// <see cref="HumanGroup.PlaceMembersAtSpawn"/> lays them out on their slots once the whole group
        /// exists, so a group always appears as one compact bloc. The other agents of a route that stays on
        /// formation slots are placed around the same anchor, each projection bounded to half a spacing so
        /// nobody is pushed into the next room by it.</para>
        ///
        /// <para><b>Random spawn</b> — a point drawn inside the zone, on navigable ground. The draw feeds the
        /// anchor, so a formation appears around one random point instead of scattering its members over one
        /// draw each.</para>
        ///
        /// <para><b>Independent agents</b> — the route asks for no shape, so there is no shared anchor to place
        /// anyone around: every agent draws its own point in the zone, kept apart from the agents this route has
        /// already placed. That is what turns one entry into a crowd crossing the map.</para>
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

            // A scatter route asks for no shape at all: every agent draws its own point in the area, kept clear
            // of the agents this route has already placed. That is what turns one entry into a crowd.
            if (GroupFormation.IsScatter(spawn.Formation))
                return SpawnPlanner.PlaceAnchor(DrawIndependentAnchor(config, spawn));

            // Everybody else shares one anchor: the whole formation appears around a single point, which is
            // either a member of a group laid out once the group exists, or one agent of this entry below.
            Vector3 anchor = SpawnPlanner.PlaceAnchor(ResolveSharedAnchor(config, spawn));
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

        /// <summary>
        /// One independent agent's own spawn point. The draw is kept away from the agents this route already
        /// placed, so a crowd does not appear on top of itself; when the zone is too tight to honour the
        /// requested spacing, the last draw is kept rather than refusing to place the agent.
        /// </summary>
        private Vector3 DrawIndependentAnchor(HumanScenarioConfig config, SpawnConfig spawn)
        {
            if (!_independentAnchors.TryGetValue(config, out List<Vector3> placed))
            {
                placed = new List<Vector3>();
                _independentAnchors[config] = placed;
            }

            float minDistance = IndependentSpacing(config, Mathf.Max(1, config.Count));
            Vector3 drawn = DrawSpawnAnchor(spawn);
            for (int attempt = 1; attempt < IndependentDrawAttempts; attempt++)
            {
                if (IsClearOf(drawn, placed, minDistance))
                    break;
                drawn = DrawSpawnAnchor(spawn);
            }

            placed.Add(drawn);
            return drawn;
        }

        private static bool IsClearOf(Vector3 candidate, List<Vector3> placed, float minDistance)
        {
            float squared = minDistance * minDistance;
            foreach (Vector3 other in placed)
            {
                float dx = candidate.x - other.x;
                float dz = candidate.z - other.z;
                if (dx * dx + dz * dz < squared)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Anchor of a spawn definition, drawn once and remembered for the whole formation that shares it: the
        /// agents one entry places on formation slots around it. A random zone is therefore sampled once, not
        /// once per agent, and the members keep their relative positions around that point.
        /// </summary>
        private Vector3 ResolveSharedAnchor(HumanScenarioConfig config, SpawnConfig spawn)
        {
            if (!IsRandomSpawn(spawn))
                return ResolveSpawnAnchor(spawn);

            if (_randomAnchors.TryGetValue(config, out Vector3 drawn))
                return drawn;

            drawn = DrawSpawnAnchor(spawn);
            _randomAnchors[config] = drawn;
            return drawn;
        }

        /// <summary>
        /// Minimum distance kept between the independent agents of one route. The authored spacing is honoured
        /// when the area can hold it; below that, the area wins, because a crowd that cannot stay three metres
        /// apart would otherwise stack every agent on the last accepted draw instead of filling its zone.
        /// </summary>
        private float IndependentSpacing(HumanScenarioConfig config, int count)
        {
            float requested = SpawnPlanner.ClampSpacing(
                config.Spawn?.Spacing ?? SpawnPlanner.DefaultSpacing,
                config.Spawn?.Formation);

            if (count <= 1)
                return requested;

            Bounds bounds = ResolveBoundsFromSpawn(config.Spawn);
            float area = Mathf.Max(0f, bounds.size.x) * Mathf.Max(0f, bounds.size.z);
            if (area <= 0.01f)
                return requested;

            // A square lattice holding every agent of the route: the tighter of the two rules keeps the crowd
            // apart without ever refusing to place the last agents.
            float packing = Mathf.Sqrt(area / count);
            return Mathf.Max(SpawnPlanner.MinSpacing, Mathf.Min(requested, packing));
        }

        /// <summary>
        /// Walking speed of one independent agent, as a factor of the speed its route authored. Drawn from
        /// <c>UnityEngine.Random</c>, which the simulation seeded, so a run stays reproducible.
        /// </summary>
        private static float CrowdSpeedVariation() =>
            1f + UnityEngine.Random.Range(-CrowdSpeedSpread, CrowdSpeedSpread);

        /// <summary>
        /// Entry delay of one independent agent. A crowd enters over a short window instead of stepping off the
        /// line as one rank, and the window follows the size of the crowd so the wait stays bounded however many
        /// agents the route carries.
        /// </summary>
        private static float CrowdEntryHold(int count) =>
            UnityEngine.Random.Range(0f, Mathf.Min(CrowdEntryWindow, Mathf.Max(1f, count * CrowdEntryGap)));

        private static bool IsRandomSpawn(SpawnConfig spawn)
        {
            return spawn != null && string.Equals(spawn.Type, "random", StringComparison.OrdinalIgnoreCase);
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

        /// <summary>
        /// Random anchor of a spawn definition: a point of its zone — the referenced one, or the inline one —
        /// kept on navigable ground by <see cref="RandomPlacement"/>. Draws come from
        /// <c>UnityEngine.Random</c>, which the simulation already seeded, so a run stays reproducible.
        /// </summary>
        private Vector3 DrawSpawnAnchor(SpawnConfig spawn)
        {
            Bounds bounds = ResolveBoundsFromSpawn(spawn);
            float height = bounds.center.y;
            Vector2 point = RandomPlacement.SampleWalkable(
                bounds,
                DrawUniformPoint,
                position => IsNavigable(position, height),
                position => ProjectToNavigable(position, height));
            return new Vector3(point.x, bounds.center.y, point.y);
        }

        /// <summary>Zone of a random spawn: the referenced zone when it is one, the inline one otherwise.</summary>
        private Bounds ResolveBoundsFromSpawn(SpawnConfig spawn)
        {
            if (!string.IsNullOrEmpty(spawn.Reference))
            {
                Bounds referenced = ResolveBounds(spawn.Reference);
                if (referenced.size != Vector3.zero)
                    return referenced;
            }

            if (spawn.Zone != null && spawn.Zone.IsBounds)
                return spawn.Zone.ToBounds();

            // No zone anywhere: the authored anchor becomes a degenerate zone, so the random spawn degrades to
            // the point spawn it really describes instead of jumping to the origin.
            return new Bounds(ResolveSpawnAnchor(spawn), Vector3.zero);
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
        /// Spatial position of one goal, for the agent being configured. A point stays where it is authored; a
        /// random goal draws its own point inside its zone, kept on navigable ground.
        ///
        /// Resolving here, per agent, is what makes a random goal a real destination for each member of a
        /// route instead of one frozen point shared by the whole scenario.
        /// </summary>
        private Vector3 ResolveSpatialGoalPosition(GoalConfig goal, bool keepApart = false, float minDistance = 0f)
        {
            if (goal == null)
                return Vector3.zero;

            if (!IsRandomGoal(goal))
                return ResolveGoalPosition(goal);

            Bounds bounds = ResolveBoundsFromGoal(goal);
            float height = bounds.center.y;
            if (keepApart)
                return DrawIndependentGoal(goal, bounds, height, minDistance);

            Vector2 point = RandomPlacement.SampleWalkable(
                bounds,
                DrawUniformPoint,
                position => IsNavigable(position, height),
                position => ProjectToNavigable(position, height));
            return new Vector3(point.x, bounds.center.y, point.y);
        }

        /// <summary>
        /// An arrival point of an independent route, drawn away from the arrivals already given to the other
        /// agents of the same objective. Everybody crossing the map towards the same area should still end on a
        /// spot of their own; when the area cannot hold them apart, the last draw is kept rather than left empty.
        /// </summary>
        private Vector3 DrawIndependentGoal(GoalConfig goal, Bounds bounds, float height, float minDistance)
        {
            if (!_independentGoals.TryGetValue(goal, out List<Vector3> placed))
            {
                placed = new List<Vector3>();
                _independentGoals[goal] = placed;
            }

            Vector3 drawn = DrawGoalPoint(bounds, height);
            for (int attempt = 1; attempt < IndependentDrawAttempts; attempt++)
            {
                if (IsClearOf(drawn, placed, minDistance))
                    break;
                drawn = DrawGoalPoint(bounds, height);
            }

            placed.Add(drawn);
            return drawn;
        }

        private Vector3 DrawGoalPoint(Bounds bounds, float height)
        {
            Vector2 point = RandomPlacement.SampleWalkable(
                bounds,
                DrawUniformPoint,
                position => IsNavigable(position, height),
                position => ProjectToNavigable(position, height));
            return new Vector3(point.x, height, point.y);
        }

        /// <summary>A spatial goal owns a position: a point (empty type included), or a random zone.</summary>
        private static bool IsSpatialGoal(GoalConfig goal)
        {
            return goal != null && (string.IsNullOrWhiteSpace(goal.Type) ||
                                    string.Equals(goal.Type, "point", StringComparison.OrdinalIgnoreCase) ||
                                    IsRandomGoal(goal));
        }

        private static bool IsRandomGoal(GoalConfig goal)
        {
            return goal != null && string.Equals(goal.Type, "random", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when the agents of this route are on their own: a formation that spreads them instead of
        /// holding a shape. Crowds are authored this way, and every agent then draws its own start and its own
        /// arrival. No group id is involved: the route itself is the walking unit.
        /// </summary>
        private static bool IsIndependentRoute(HumanScenarioConfig config)
        {
            return config != null &&
                   config.Spawn != null &&
                   GroupFormation.IsScatter(config.Spawn.Formation);
        }

        /// <summary>
        /// True when the agents of this route walk together, as one small group holding a shape.
        ///
        /// A route of one agent walks alone by definition, and a route in scatter formation is a crowd whose
        /// agents each own their start and their arrival. Everything else — several agents and a shape — walks
        /// as a group keyed on the route itself, so no group id ever has to be written by hand. A route whose
        /// goal is not spatial (wander, follow, stay) is left out as well: it has no polyline to walk in
        /// formation, so its agents keep their own goal.
        /// </summary>
        private static bool WalksAsAFormation(HumanScenarioConfig config)
        {
            return config != null &&
                   !string.IsNullOrWhiteSpace(config.Id) &&
                   config.Count > 1 &&
                   IsSpatialGoal(config.Goal) &&
                   !GroupFormation.IsScatter(config.Spawn?.Formation);
        }

        /// <summary>
        /// Resolves a Bounds from a GoalConfig (for random goals).
        /// Supports: 'ref' (legacy) or direct 'zone'; a goal carrying neither falls back on the point it
        /// already resolves to, which <see cref="RandomPlacement"/> then projects onto navigable ground.
        /// </summary>
        private Bounds ResolveBoundsFromGoal(GoalConfig goal)
        {
            if (!string.IsNullOrEmpty(goal.Reference))
            {
                Bounds referenced = ResolveBounds(goal.Reference);
                if (referenced.size != Vector3.zero)
                    return referenced;
            }

            if (goal.Zone != null && goal.Zone.IsBounds)
                return goal.Zone.ToBounds();

            return new Bounds(ResolveGoalPosition(goal), Vector3.zero);
        }

        /// <summary>Uniform draw inside the XZ footprint of a zone, at the height of its centre.</summary>
        private static Vector2 DrawUniformPoint(Bounds bounds) => new Vector2(
            UnityEngine.Random.Range(bounds.min.x, bounds.max.x),
            UnityEngine.Random.Range(bounds.min.z, bounds.max.z));

        /// <summary>
        /// True when an agent may stand on that world point: the walkable grid of the scenario answers first,
        /// the scene NavMesh second, the way <see cref="SpawnPlacement"/> already arbitrates them. An
        /// environment with neither refuses every draw, and the projection then returns it untouched.
        /// <paramref name="height"/> is the height of the zone, which only the NavMesh cares about.
        /// </summary>
        private static bool IsNavigable(Vector2 point, float height)
        {
            if (ScenarioNavigation.IsAvailable)
                return ScenarioNavigation.IsWalkable(point);

            return NavMesh.SamplePosition(
                new Vector3(point.x, height, point.y), out _, 0.1f, NavMesh.AllAreas);
        }

        /// <summary>
        /// Nearest navigable point of a drawn position: walkable grid first, scene NavMesh second, and the
        /// drawn point itself when neither can answer. <paramref name="height"/> is the height of the zone.
        /// </summary>
        private static Vector2 ProjectToNavigable(Vector2 point, float height)
        {
            Vector3 projected = SpawnPlacement.SnapToNavMesh(
                new Vector3(point.x, height, point.y),
                SpawnPlacement.AnchorSearchRadius,
                DrawnPointSearchRadius);
            return new Vector2(projected.x, projected.z);
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
