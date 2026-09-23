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
        private RobotRoster _roster;
        private readonly List<Coroutine> _activeCoroutines = new();

        /// <summary>
        /// Entries the loaded scenario still owes, one routine per departure that has not happened yet. Kept
        /// apart from <see cref="_activeCoroutines"/>, which holds the behaviours of the scenario: stopping an
        /// entry must not stop a wander or a follow.
        /// </summary>
        private readonly List<Coroutine> _pendingEntries = new();

        /// <summary>
        /// Identity of the scenario the pending entries belong to, bumped whenever they are dropped. A routine
        /// that outlives the stop of its coroutine would otherwise switch on an agent of a scenario that has
        /// been replaced, and hand the pool back an instance it does not own any more.
        /// </summary>
        private int _entryGeneration;

        private readonly Dictionary<string, HumanAgent> _spawnedHumans = new();
        private int _humanCounter;
        private readonly List<Robot> _liveRobots = new();

        /// <summary>
        /// The registry of the robots of this environment, created on first use so an environment prefab
        /// authored before multiple robots existed gains one as soon as a scenario is applied.
        /// </summary>
        private RobotRoster Roster
        {
            get
            {
                if (_roster == null)
                {
                    _roster = GetComponent<RobotRoster>();
                    if (_roster == null)
                        _roster = gameObject.AddComponent<RobotRoster>();
                }

                return _roster;
            }
        }

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
            // The scenario is over: an entry it still owed would appear on a map that is no longer the one the
            // agents were authored for.
            CancelPendingEntries();
            // The scenario is over: its walkable grid must not outlive it, or the next scenario would plan on
            // the previous map until its own grid is built.
            ScenarioNavigation.Clear();
        }

        /// <summary>
        /// Clears the internal tracking of spawned humans.
        /// </summary>
        public void ClearTrackedHumans()
        {
            // The entries still owed are part of what was tracked: forgetting the record without dropping them
            // would leave a routine switching on a pooled instance on its own.
            CancelPendingEntries();
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

            // 5. Setup the robots (position, rotation, route, speed), one per entry of the scenario
            yield return StartCoroutine(SetupRobots());

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

        /// <summary>
        /// Builds and places every robot the scenario asks for. The single <c>robot</c> section of a scenario
        /// written before several robots existed is the one-entry list of the same call, so the legacy path
        /// is not a special case any more: it is the general one with one robot in it.
        /// </summary>
        private IEnumerator SetupRobots()
        {
            List<RobotScenarioConfig> robots = _currentScenario.NormalizedRobots();
            if (robots.Count == 0)
            {
                OnApplicationError?.Invoke("Robot config missing");
                yield break;
            }

            if (_robotPrefab == null && Roster.PrefabFor(RobotProfiles.DefaultId) == null)
            {
                OnApplicationError?.Invoke("Robot prefab not assigned and no robot found in scene.");
                yield break;
            }

            // The roster parents the robots under itself: it lives on the environment prefab, so they are
            // torn down with the environment a scenario change rebuilds.
            yield return StartCoroutine(
                Roster.Sync(robots, _loader, _currentScenario, _robotPrefab, _resetRobotPosition, _logEvents));
        }

        /// <summary>
        /// The robot a human follows or spawns next to, and the one the interface calls "the robot".
        ///
        /// A scenario with several robots still has to answer that question for a crowd that was authored
        /// against a single one, and the answer is the primary robot - the same one a client reaches without
        /// naming anybody.
        /// </summary>
        private GameObject FindRobot()
        {
            Robot primary = Roster.Primary;
            if (primary != null)
                return primary.gameObject;

            // Fallback for a scene that carries a robot nobody registered: a hand-placed one, or a test.
            var robotComp = _gameManager != null ? _gameManager.GetComponentInChildren<Robot>() : null;
            return robotComp != null ? robotComp.gameObject : null;
        }

        // ==========================================
        //          HUMAN SETUP
        // ==========================================

        /// <summary>
        /// One departure of a scenario: the agents that enter the simulation together, and the delay drawn for
        /// them. A route that walks as a formation is one departure, so its members keep their shape as they
        /// appear; a route that scatters is one departure per agent, so the crowd feeds in as a flow instead of
        /// arriving as a wave.
        /// </summary>
        private sealed class Departure
        {
            public HumanScenarioConfig Config { get; set; }
            public string GroupId { get; set; }
            /// <summary>Position of the first member inside its route, which its slot and spacing depend on.</summary>
            public int FirstIndex { get; set; }
            /// <summary>Agents of the route this departure carries.</summary>
            public int Count { get; set; }
            /// <summary>Seconds before it enters the simulation; zero means now.</summary>
            public float Delay { get; set; }
            /// <summary>The scenario whose entry it is, so a routine of a replaced scenario does nothing.</summary>
            public int Generation { get; set; }
            public List<HumanAgent> Members { get; } = new();
        }

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

            // Whatever the scenario before this one still owed is dropped here, before this one reserves an
            // instance: an entry of a scenario that is being replaced belongs to a map that is going away.
            CancelPendingEntries();

            // The departures are decided before anything is activated, so the delays are drawn in scenario order
            // and a seeded run stays reproducible.
            List<Departure> departures = BuildDepartures(totalHumans);

            // Get instances from the pool. A unit that leaves later is switched off here, where the pool left it:
            // an agent that is not due yet must be neither visible nor simulated, and waiting for its delay in an
            // active instance would only hide it from the eye, not from the simulation.
            foreach (Departure departure in departures)
            {
                for (int i = 0; i < departure.Count; i++)
                {
                    GameObject humanGO = poolManager.GetHuman();
                    if (humanGO == null)
                        continue;

                    humanGO.SetActive(departure.Delay <= 0f);
                    var human = humanGO.GetComponent<HumanAgent>();
                    if (human != null)
                        departure.Members.Add(human);
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
            var groups = new Dictionary<string, HumanGroup>();
            var groupsWithRoute = new HashSet<string>();
            // One draw per spawn unit: a whole formation appears around a single random point, so the anchors
            // are resolved once for this scenario and then reused by every agent sharing them.
            _randomAnchors.Clear();
            _independentAnchors.Clear();
            _independentGoals.Clear();
            // The agents that are already in the scenario, so their spawn is checked as the one batch it used to
            // be; an agent that enters later is checked the moment it does.
            var presentHumans = new List<HumanAgent>();
            int heldAgents = 0;
            int heldDepartures = 0;
            foreach (Departure departure in departures)
            {
                // A departure that is not due yet is configured by the routine that releases it: a route written
                // into an agent whose prefab is still asleep would be dropped by the controllers that wake up
                // afterwards, and the agent would stand there instead of walking.
                if (departure.Delay > 0f)
                {
                    departure.Generation = _entryGeneration;
                    heldAgents += departure.Members.Count;
                    heldDepartures++;
                    _pendingEntries.Add(StartCoroutine(
                        ReleaseDeparture(departure, poolManager, groups, groupsWithRoute)));
                    continue;
                }

                ConfigureDeparture(departure, poolManager, groups, groupsWithRoute);
                presentHumans.AddRange(departure.Members);
            }

            // The anchor being on walkable ground does not mean the formation is: check every member.
            if (presentHumans.Count > 0)
                VerifySpawnedFormation(presentHumans);

            // Every robot watches the same crowd: the detector of a robot is what its own stream publishes,
            // so a second robot has its own view of the pedestrians, not a copy of the first one's.
            _liveRobots.Clear();
            Roster.FillRobots(_liveRobots);
            foreach (Robot robot in _liveRobots)
            {
                if (robot == null)
                    continue;

                AgentDetector detector = robot.GetAgentDetector();
                if (detector != null)
                    detector.SetAgentPool(poolManager.GetPoolParent().gameObject);
            }

            if (_logEvents)
            {
                string held = heldDepartures > 0
                    ? $"; {heldAgents} agent(s) in {heldDepartures} departure(s) still to enter over their window"
                    : string.Empty;
                Debug.Log($"[ScenarioApplier] Configured {presentHumans.Count} humans across " +
                          $"{groups.Count} formation route(s){held}");
            }
        }

        /// <summary>
        /// Splits the routes of the scenario into departures, in spawn order, and draws the delay of each one.
        ///
        /// A spawn unit is the route itself when it walks as a formation: one draw, and the members leave
        /// together with their shape intact. A route that scatters is a unit per agent, and each one draws its
        /// own delay, which is what turns the crowd into a flow. The draws happen here, before anything is
        /// activated, so the sequence the seeded simulation produces does not depend on when a departure fires;
        /// a window of zero draws nothing at all and leaves every delay at zero, which keeps the draws and the
        /// activation order of a scenario written before the feature exactly as they were.
        /// </summary>
        private List<Departure> BuildDepartures(int totalHumans)
        {
            var departures = new List<Departure>();
            int humanIndex = 0;
            foreach (var config in _currentScenario.Humans)
            {
                int count = Mathf.Min(config.Count, totalHumans - humanIndex);
                if (count <= 0)
                    continue;

                if (WalksAsAFormation(config))
                {
                    // The route id keys the implicit group of this route, so a formation is never shared with
                    // another route by accident. A scatter route has no group at all.
                    departures.Add(new Departure
                    {
                        Config = config,
                        GroupId = config.Id.Trim(),
                        FirstIndex = 0,
                        Count = count,
                        Delay = DrawDepartureDelay(config.SpawnWindow)
                    });
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        departures.Add(new Departure
                        {
                            Config = config,
                            GroupId = null,
                            FirstIndex = i,
                            Count = 1,
                            Delay = DrawDepartureDelay(config.SpawnWindow)
                        });
                    }
                }

                humanIndex += count;
            }

            return departures;
        }

        /// <summary>
        /// Delay of one spawn unit, drawn from <c>UnityEngine.Random</c> so the simulation seed makes a run
        /// reproducible, like every other draw of the spawn does. A window of zero — the default — draws
        /// nothing: the delay stays zero without consuming the sequence the other draws read.
        /// </summary>
        private static float DrawDepartureDelay(float spawnWindow) =>
            spawnWindow > 0f ? UnityEngine.Random.Range(0f, spawnWindow) : 0f;

        /// <summary>
        /// Releases one departure at the end of its window, then configures it.
        ///
        /// The agents come back together — a formation appears in shape instead of one member at a time — and
        /// are configured one frame later, the way the agents that leave straight away are, because a route
        /// written before the prefab has woken up is dropped by the controllers that initialise in Awake.
        ///
        /// The generation is checked before anything is touched: a routine that outlives the replacement of its
        /// scenario must not switch on an instance the pool has since handed to somebody else.
        /// </summary>
        private IEnumerator ReleaseDeparture(
            Departure departure,
            HumanPoolManager poolManager,
            Dictionary<string, HumanGroup> groups,
            HashSet<string> groupsWithRoute)
        {
            yield return WaitSimulationSeconds(departure.Delay);

            if (!StillOurs(departure, poolManager))
                yield break;

            foreach (HumanAgent human in departure.Members)
            {
                if (human != null)
                    human.gameObject.SetActive(true);
            }

            yield return null; // let Unity stabilize, like the agents that leave at once

            if (!StillOurs(departure, poolManager))
                yield break;

            ConfigureDeparture(departure, poolManager, groups, groupsWithRoute);

            // The anchor being on walkable ground does not mean the formation is: check every member.
            VerifySpawnedFormation(departure.Members);

            if (_logEvents)
                Debug.Log($"[ScenarioApplier] Route '{departure.Config.Id}' entered after " +
                          $"{departure.Delay:0.##}s with {departure.Members.Count} agent(s)");
        }

        /// <summary>
        /// True while the instances of a departure are still the ones it reserved: the scenario it was decided
        /// for is still the loaded one, and the pool still counts its agents as active. The pool owns an
        /// instance, so one it has taken back — a reset, or the scenario being cleared — must not be switched
        /// on, nor configured, by an entry that outlived it.
        /// </summary>
        private bool StillOurs(Departure departure, HumanPoolManager poolManager)
        {
            if (departure.Generation != _entryGeneration || poolManager == null || departure.Members.Count == 0)
                return false;

            IReadOnlyList<GameObject> active = poolManager.ActiveHumans;
            foreach (HumanAgent human in departure.Members)
            {
                if (human == null || !active.Contains(human.gameObject))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Waits a delay of simulation time rather than of wall-clock time. Scaled time keeps the entries in
        /// step with the run, and standing still while the clock is paused keeps a crowd from slipping into a
        /// scenario that is standing still: the window measures how long the agents take to enter the
        /// simulation, not how long the operator waits.
        /// </summary>
        private static IEnumerator WaitSimulationSeconds(float delay)
        {
            float elapsed = 0f;
            while (elapsed < delay)
            {
                if (Supervisor.Instance == null || !Supervisor.Instance.IsPaused)
                    elapsed += Time.deltaTime;

                yield return null;
            }
        }

        /// <summary>
        /// Configures the agents of one departure and lays their formation out, which is where the per-agent
        /// draws of a route happen: speed, start, arrival and entry hold all belong to the agent, while the
        /// shape of a formation is only placed once its members exist.
        /// </summary>
        private void ConfigureDeparture(
            Departure departure,
            HumanPoolManager poolManager,
            Dictionary<string, HumanGroup> groups,
            HashSet<string> groupsWithRoute)
        {
            HumanGroup group = null;
            for (int i = 0; i < departure.Members.Count; i++)
            {
                var human = departure.Members[i];
                if (human == null)
                    continue;

                ConfigureHuman(human, departure.Config, departure.FirstIndex + i, departure.Config.Count, poolManager);
                if (departure.GroupId != null)
                {
                    if (group == null)
                    {
                        if (!groups.TryGetValue(departure.GroupId, out group))
                        {
                            SpawnPlanner.GroupLayout layout = SpawnPlanner.ResolveLayout(departure.Config);
                            group = new HumanGroup(
                                departure.GroupId,
                                layout.Spacing,
                                layout.HasFormation ? layout.Formation : null,
                                layout.Parameter);
                            groups[departure.GroupId] = group;
                        }
                    }

                    // The group walks one shared route: the first agent of the route defines it, and every
                    // member holds a slot around its reference point instead of walking alone.
                    if (groupsWithRoute.Add(departure.GroupId))
                    {
                        group.SetRoute(
                            PlanFormationRoute(human, departure.GroupId),
                            departure.Config.Speed,
                            HumanEndBehaviorParser.Parse(departure.Config.EndBehavior));
                    }

                    group.Add(human);
                }
                _spawnedHumans[$"{departure.Config.Id}_{_humanCounter++}"] = human;
            }

            // Lay the group out around its leader, facing the direction it is about to walk.
            if (group != null)
                group.PlaceMembersAtSpawn();
        }

        /// <summary>
        /// Drops the departures a scenario still owed and orphans their routines, so an entry decided by a
        /// scenario that is being replaced or stopped can neither show an agent of a map that is no longer
        /// loaded nor wake an instance that has gone back to the pool.
        /// </summary>
        private void CancelPendingEntries()
        {
            _entryGeneration++;
            foreach (Coroutine routine in _pendingEntries)
            {
                if (routine != null)
                    StopCoroutine(routine);
            }

            _pendingEntries.Clear();
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
            bool independent = WalksIndependently(config);
            float arrivalSpacing = independent ? IndependentSpacing(config, total) : 0f;

            // A crowd is not a block: every independent agent gets its own walking speed and its own entry
            // delay, so the flow spreads out instead of stepping off the line as a single rank. Agents that
            // walk in formation keep exactly the speed the scenario authored, or they would pull the shape
            // apart.
            float speedFactor = independent ? CrowdSpeedVariation() : 1f;
            // An authored departure window replaces the short hold a crowd uses by default. The two would add
            // up, and the crowd would then still be entering after the window the scenario asked for.
            float entryHold = independent && config.SpawnWindow <= 0f ? CrowdEntryHold(total) : 0f;

            // The hold has to be set before the route, or the agent would start walking on the frame it is
            // configured and only stop later.
            human.SetStartHold(entryHold);
            if (IsSpatialGoal(config.Goal))
            {
                // A random goal is resolved per agent: the members of a route each draw their own destination
                // inside the zone, at the moment they are configured. An arrival the scenario wrote as one
                // point is spread the same way, but only for a route that really carries several agents: a lone
                // walker goes to the point itself.
                bool crowd = independent && total > 1;
                goals.Add(ResolveSpatialGoalPosition(config.Goal, crowd, arrivalSpacing));
                if (config.Goals != null)
                    goals.AddRange(config.Goals.Where(IsSpatialGoal)
                        .Select(goal => ResolveSpatialGoalPosition(goal, crowd, arrivalSpacing)));
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

            // A route whose agents are on their own asks for no shape at all: every agent draws its own point in
            // the area, kept clear of the agents this route has already placed. That is what turns one entry
            // into a crowd.
            if (WalksIndependently(config))
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
        /// random goal draws its own point inside its zone, kept on navigable ground. When the route is a
        /// crowd, both of them hand a place of its own to each agent instead of the same one to everybody.
        ///
        /// Resolving here, per agent, is what makes a random goal a real destination for each member of a
        /// route instead of one frozen point shared by the whole scenario.
        /// </summary>
        private Vector3 ResolveSpatialGoalPosition(GoalConfig goal, bool keepApart = false, float minDistance = 0f)
        {
            if (goal == null)
                return Vector3.zero;

            if (!IsRandomGoal(goal))
            {
                Vector3 authored = ResolveGoalPosition(goal);
                return keepApart ? DrawSharedArrival(goal, authored, minDistance) : authored;
            }

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
        /// One agent's own arrival around a goal the scenario wrote as a single point.
        ///
        /// A crowd heading for one spot must not land on it: the agents would queue on the same square metre,
        /// the ones behind could never get close enough to report their arrival, and the route would look
        /// stuck. The first agent takes the authored point, the others take a place around it - on navigable
        /// ground, and kept from the arrivals already handed out.
        /// </summary>
        private Vector3 DrawSharedArrival(GoalConfig goal, Vector3 authored, float minDistance)
        {
            if (!_independentGoals.TryGetValue(goal, out List<Vector3> placed))
            {
                placed = new List<Vector3>();
                _independentGoals[goal] = placed;
            }

            Vector3 drawn = placed.Count == 0 ? authored : DrawAround(authored, minDistance);
            for (int attempt = 1; attempt < IndependentDrawAttempts; attempt++)
            {
                if (IsClearOf(drawn, placed, minDistance))
                    break;
                drawn = DrawAround(authored, minDistance);
            }

            placed.Add(drawn);
            return drawn;
        }

        /// <summary>
        /// A place in the disc around an authored goal, projected back onto navigable ground. The radius is
        /// drawn with a square root, so the places spread over the disc instead of crowding its centre.
        /// </summary>
        private static Vector3 DrawAround(Vector3 centre, float radius)
        {
            float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            float reach = Mathf.Sqrt(UnityEngine.Random.value) * Mathf.Max(0f, radius);
            Vector2 drawn = ProjectToNavigable(
                new Vector2(centre.x + Mathf.Cos(angle) * reach, centre.z + Mathf.Sin(angle) * reach),
                centre.y);
            return new Vector3(drawn.x, centre.y, drawn.y);
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
        /// True when the agents of this route are on their own: every one of them draws its own start and its
        /// own arrival, and walks there alone. Crowds are authored this way, whether the route spells the
        /// scatter out or leaves the shape unnamed, and no group id is involved: the route itself is the
        /// walking unit. <see cref="SpawnPlanner.SpreadsApart"/> holds the rule itself.
        /// </summary>
        private bool WalksIndependently(HumanScenarioConfig config)
        {
            if (config?.Spawn == null)
                return false;

            return SpawnPlanner.SpreadsApart(config.Spawn.Formation, HasAreaToAppearIn(config.Spawn));
        }

        /// <summary>
        /// True when a spawn describes an area rather than a single point: a zone, or a named reference that
        /// resolves to one. It is what tells a route that never named a shape apart from a formation pinned to
        /// one spot, which has nowhere to spread into.
        /// </summary>
        private bool HasAreaToAppearIn(SpawnConfig spawn)
        {
            Bounds bounds = ResolveBoundsFromSpawn(spawn);
            return bounds.size.x > 0.01f && bounds.size.z > 0.01f;
        }

        /// <summary>
        /// True when the agents of this route walk together, as one small group holding a shape.
        ///
        /// A route of one agent walks alone by definition, and a route whose agents are on their own is a crowd
        /// whose members each own their start and their arrival. Everything else — several agents and a shape —
        /// walks as a group keyed on the route itself, so no group id ever has to be written by hand. A route
        /// whose goal is not spatial (wander, follow, stay) is left out as well: it has no polyline to walk in
        /// formation, so its agents keep their own goal.
        /// </summary>
        private bool WalksAsAFormation(HumanScenarioConfig config)
        {
            return config != null &&
                   !string.IsNullOrWhiteSpace(config.Id) &&
                   config.Count > 1 &&
                   IsSpatialGoal(config.Goal) &&
                   !WalksIndependently(config);
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
