using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using RosMessageTypes.Geometry;
using RosMessageTypes.Simulation;
using RosMessageTypes.Std;
using RobotSNAP.Agents;
using RobotSNAP.CameraControl;
using RobotSNAP.Core;
using RobotSNAP.Core.Scenario;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using UnityEngine;

namespace RobotSNAP.ROS
{
    /// <summary>
    /// Publishes the whole running application on ROS: the scenario that was applied, its occupancy grid,
    /// the humans of the scene and a JSON snapshot of the rest of the runtime state, so a client can
    /// follow a session without reading the Unity scene.
    ///
    /// One component owns the four streams because they answer one question together, and because the
    /// streams do not cost the same: the crowd moves every frame while the grid is heavy and changes only
    /// when a scenario is applied, so each stream is paced on its own and the grid is built once per
    /// applied scenario instead of once per frame.
    ///
    /// Nothing here may throw when no ROS server is listening, which is how this project usually runs: the
    /// connector raises an exception on a topic that has no registered publisher, so every publish goes
    /// through <see cref="CanPublish"/> first.
    /// </summary>
    public class SimulationStatePublisher : MonoBehaviour
    {
        [Header("ROS Configuration")]
        [Tooltip("Take the topic prefix from EnvROS, like the other publishers of this folder.")]
        [SerializeField] private bool autoDetectPrefix = true;

        [Tooltip("Prefix used when auto detection is off.")]
        [SerializeField] private string customPrefix = "";

        [Tooltip("Rate of the crowd and of the JSON state snapshot, in Hz. Zero disables those two streams.")]
        [SerializeField] private float publishFrequencyHz = 10f;

        [Tooltip("Rate of the scenario stream, in Hz. A scenario only changes when it is applied, so this stream runs far below the crowd one.")]
        [SerializeField] private float sceneInfoFrequencyHz = 1f;

        [Tooltip("Shortest delay between two publishes of the occupancy grid, in seconds. The whole grid is serialized on every publish, so the value is floored at one second.")]
        [SerializeField] private float mapIntervalSeconds = 1f;

        [Header("Topic Names (the EnvROS prefix is prepended)")]
        [SerializeField] private string sceneInfoTopic = "/simulation/scene_info";
        [SerializeField] private string mapTopic = "/simulation/map";
        [SerializeField] private string peopleTopic = "/simulation/people";
        [SerializeField] private string stateTopic = "/simulation/state";

        [Header("Frame ID")]
        [Tooltip("Frame written in every header this component stamps.")]
        [SerializeField] private string frameId = "/map";

        [Header("Debug")]
        [SerializeField] private bool logPublishEvents = false;

        [Header("References")]
        [SerializeField] private EnvROS _envROS;

        // ROS side
        private ROSConnection _ros;
        private string _prefix = "";
        private string _sceneInfoTopicName;
        private string _mapTopicName;
        private string _peopleTopicName;
        private string _stateTopicName;
        private bool _initialized;

        // Application side: the references are refreshed lazily because the environments - and the EnvROS
        // that lives inside them - are destroyed and rebuilt each time a scenario is loaded.
        private ScenarioManager _scenarioManager;
        private Clock _clock;
        private Robot _robot;
        private CameraController _cameraController;
        private SimulationState? _lastState;

        // Pacing: wall-clock intervals, so a paused simulation clock does not stall the bridge.
        private float _peopleInterval;
        private float _stateInterval;
        private float _sceneInfoInterval;
        private float _mapInterval;
        private float _peopleTimer;
        private float _stateTimer;
        private float _sceneInfoTimer;
        private float _mapTimer;

        // Occupancy grid of the applied scenario, rebuilt only on application and never per frame.
        private sbyte[] _mapData;
        private int _mapWidth;
        private int _mapHeight;
        private float _mapResolution;
        private PoseMsg _mapOrigin = new PoseMsg();
        private bool _mapPublishPending;

        #region Unity Lifecycle

        private void Start()
        {
            Initialize();
        }

        private void Update()
        {
            if (!_initialized) return;

            float deltaTime = Time.unscaledDeltaTime;

            _sceneInfoTimer += deltaTime;
            if (_sceneInfoTimer >= _sceneInfoInterval)
            {
                _sceneInfoTimer = 0f;
                PublishSceneInfo();
            }

            _mapTimer += deltaTime;
            if (_mapPublishPending || _mapTimer >= _mapInterval)
            {
                _mapTimer = 0f;
                _mapPublishPending = false;
                PublishMap();
            }

            _peopleTimer += deltaTime;
            if (_peopleTimer >= _peopleInterval)
            {
                _peopleTimer = 0f;
                PublishPeople();
            }

            _stateTimer += deltaTime;
            if (_stateTimer >= _stateInterval)
            {
                _stateTimer = 0f;
                PublishState();
            }
        }

        private void OnDestroy()
        {
            if (_scenarioManager != null)
            {
                _scenarioManager.OnScenarioLoaded -= OnScenarioLoaded;
                _scenarioManager.OnScenarioApplied -= OnScenarioApplied;
            }

            EventBus.Instance.Unsubscribe<SimulationStateChangedEvent>(OnSimulationStateChanged);
        }

        #endregion

        #region Initialization

        /// <summary>
        /// The snapshot that <c>/simulation/state</c> carries, without waiting for the next tick. Handy to
        /// check the bridge from the editor, and the only way to see the payload when nothing is listening.
        /// </summary>
        public string CurrentStateJson => BuildStateJson();

        /// <summary>
        /// Resolves the connection, registers the four publishers and hooks the events whose payload the
        /// streams need: the applied scenario for the grid, and the state the manager publishes for the
        /// JSON snapshot. Called from Start, and once more from the inspector menu to retry by hand.
        /// </summary>
        public void Initialize()
        {
            _ros = ROSConnection.GetOrCreateInstance();
            if (_ros == null)
            {
                Debug.LogError($"[{name}] ROSConnection unavailable, simulation state will not be published");
                enabled = false;
                return;
            }

            ResolveTopics(force: true);

            _scenarioManager ??= FindAnyObjectByType<ScenarioManager>();
            if (_scenarioManager != null)
            {
                _scenarioManager.OnScenarioApplied -= OnScenarioApplied;
                _scenarioManager.OnScenarioApplied += OnScenarioApplied;
                _scenarioManager.OnScenarioLoaded -= OnScenarioLoaded;
                _scenarioManager.OnScenarioLoaded += OnScenarioLoaded;
            }

            _clock ??= Clock.Instance;

            // Timing is set here so a rate edited in the inspector is honoured on the next initialize, and
            // a rate of zero parks its stream instead of publishing it every frame.
            _peopleInterval = Interval(1f / Mathf.Max(0f, publishFrequencyHz));
            _stateInterval = Interval(1f / Mathf.Max(0f, publishFrequencyHz));
            _sceneInfoInterval = Interval(1f / Mathf.Max(0f, sceneInfoFrequencyHz));
            _mapInterval = Mathf.Max(1f, mapIntervalSeconds);

            // The grid of the scenario that is already loaded goes out on the first tick of the loop.
            RebuildMapCache();
            _mapPublishPending = true;

            EventBus.Instance.Unsubscribe<SimulationStateChangedEvent>(OnSimulationStateChanged);
            EventBus.Instance.Subscribe<SimulationStateChangedEvent>(OnSimulationStateChanged);

            _initialized = true;

            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Publishing simulation state:\n" +
                          $"  Scene info: {_sceneInfoTopicName} at {_sceneInfoInterval} s\n" +
                          $"  Map: {_mapTopicName} every {_mapInterval} s\n" +
                          $"  People: {_peopleTopicName} at {_peopleInterval} s\n" +
                          $"  State: {_stateTopicName} at {_stateInterval} s");
            }
        }

        /// <summary>Interval of a stream, where a zero rate means "never" instead of "every frame".</summary>
        private static float Interval(float seconds)
        {
            return seconds <= 0f ? float.PositiveInfinity : seconds;
        }

        /// <summary>
        /// Builds the four topic names from the EnvROS prefix and registers them. Loading a scenario
        /// destroys and rebuilds the environments, so the EnvROS instance - and the prefix it carries - can
        /// change mid-session. The names are rebuilt, and the topics re-registered, only when it did change,
        /// because the connector warns about a topic that is registered twice.
        /// </summary>
        private void ResolveTopics(bool force)
        {
            // A destroyed EnvROS compares equal to null, so this looks the new one up after a scenario load.
            _envROS ??= FindAnyObjectByType<EnvROS>();

            string prefix = "";
            if (autoDetectPrefix && _envROS != null)
                prefix = _envROS.Prefix;
            else if (!string.IsNullOrEmpty(customPrefix))
                prefix = customPrefix;

            prefix = (prefix ?? "").Trim('/');
            if (!force && prefix == _prefix && _sceneInfoTopicName != null)
                return;

            _prefix = prefix;
            _sceneInfoTopicName = BuildTopic(prefix, sceneInfoTopic);
            _mapTopicName = BuildTopic(prefix, mapTopic);
            _peopleTopicName = BuildTopic(prefix, peopleTopic);
            _stateTopicName = BuildTopic(prefix, stateTopic);

            RegisterTopic<SceneInfoMsg>(_sceneInfoTopicName);
            RegisterTopic<MapMsg>(_mapTopicName);
            RegisterTopic<PersonEntryArrayMsg>(_peopleTopicName);
            RegisterTopic<StringMsg>(_stateTopicName);
        }

        /// <summary>
        /// Registers a topic unless it already carries a publisher: the connector warns on a topic
        /// registered twice, and initializing the component again must stay a quiet operation.
        /// </summary>
        private void RegisterTopic<T>(string topicName) where T : Message
        {
            if (_ros == null || string.IsNullOrEmpty(topicName)) return;

            RosTopicState topic = _ros.GetTopic(topicName);
            if (topic != null && topic.IsPublisher) return;

            _ros.RegisterPublisher<T>(topicName);
        }

        /// <summary>
        /// Topic name in the house shape (/prefix/topic). The prefix is trimmed so a configured value
        /// carrying its own slashes cannot produce a topic with a doubled separator.
        /// </summary>
        private static string BuildTopic(string prefix, string topic)
        {
            string stream = (topic ?? "").Trim('/');
            if (string.IsNullOrEmpty(stream)) return null;
            return string.IsNullOrEmpty(prefix) ? $"/{stream}" : $"/{prefix}/{stream}";
        }

        /// <summary>Frame id of every header this component stamps.</summary>
        private string FullFrameId => string.IsNullOrEmpty(_prefix) ? frameId : $"/{_prefix}{frameId}";

        /// <summary>
        /// True when a topic can be published to. The connector throws on a topic that holds no publisher,
        /// which is exactly the state of a session running without a ROS server, so asking first keeps that
        /// case quiet.
        /// </summary>
        private bool CanPublish(string topicName)
        {
            if (_ros == null || string.IsNullOrEmpty(topicName)) return false;

            RosTopicState topic = _ros.GetTopic(topicName);
            return topic != null && topic.IsPublisher;
        }

        #endregion

        #region Scenario Event

        private void OnSimulationStateChanged(SimulationStateChangedEvent evt)
        {
            _lastState = evt.NewState;
        }

        /// <summary>
        /// A new scenario replaces the grid a few frames after this event - the map is built with the
        /// environment - so the previous grid is dropped now rather than published once more as if it
        /// belonged to the scenario being loaded.
        /// </summary>
        private void OnScenarioLoaded(ScenarioData scenario)
        {
            if (!_initialized) return;

            ClearMapCache();
            _mapPublishPending = true;
        }

        /// <summary>
        /// A new scenario means a new grid and a new crowd: the grid is sampled once here rather than frame
        /// after frame, and the scenario stream is pulled forward instead of waiting for its next tick.
        /// </summary>
        private void OnScenarioApplied(ScenarioData scenario)
        {
            if (!_initialized) return;

            ResolveTopics(force: false);
            RebuildMapCache();
            _mapPublishPending = true;
            _mapTimer = 0f;

            // Pull the scenario stream forward so a client sees the new scenario on this frame instead of
            // waiting for its next tick, unless that stream is disabled.
            if (!float.IsPositiveInfinity(_sceneInfoInterval))
                _sceneInfoTimer = _sceneInfoInterval;
        }

        #endregion

        #region Stream 1 - Scenario

        /// <summary>
        /// The scenario that is loaded and the environment it runs in. The message also carries the robot
        /// start and target the scenario authored, which describe the session rather than its live state,
        /// and the size of the crowd that is currently in the scene.
        /// </summary>
        private void PublishSceneInfo()
        {
            if (!CanPublish(_sceneInfoTopicName)) return;

            ScenarioManager manager = _scenarioManager ??= FindAnyObjectByType<ScenarioManager>();
            ScenarioData scenario = manager != null ? manager.CurrentScenarioData : null;

            var msg = new SceneInfoMsg
            {
                scenario_name = scenario != null ? scenario.Name : "",
                environment = EnvironmentOf(scenario),
                robot_start_pose = ScenarioPointPose(scenario, scenario?.Robot?.StartRef),
                robot_target_pose = ScenarioPointPose(scenario, scenario?.Robot?.GoalRef),
                num_people = ToUshort(FindHumans().Length),
                num_groups = ToUshort(CountGroups(scenario))
            };

            ROSTimeUtils.UpdateHeader(msg.header);
            msg.header.frame_id = FullFrameId;

            _ros.Publish(_sceneInfoTopicName, msg);

            if (logPublishEvents)
                Debug.Log($"[{name}] Published scene info to {_sceneInfoTopicName}: {msg.scenario_name} ({msg.environment})");
        }

        /// <summary>
        /// The environment the application names for a scenario: its map when it has one, its declared
        /// location otherwise. Same value the scenario browser shows under "Environment".
        /// </summary>
        private static string EnvironmentOf(ScenarioData scenario)
        {
            if (scenario == null) return "";
            if (!string.IsNullOrEmpty(scenario.MapImage)) return scenario.MapImage;
            return scenario.Info?.Location ?? "";
        }

        /// <summary>Pose of an authored scenario point, identity when the scenario names none.</summary>
        private PoseMsg ScenarioPointPose(ScenarioData scenario, string reference)
        {
            ScenarioLoader loader = _scenarioManager != null ? _scenarioManager.Loader : null;
            if (scenario == null || loader == null || string.IsNullOrEmpty(reference))
                return new PoseMsg();

            (Vector3 position, Quaternion rotation) = loader.GetPositionAndRotation(scenario, reference);
            return Util.Geometry.GetMPose(position, rotation);
        }

        /// <summary>Groups the scenario declares, read from the authored group ids of its humans.</summary>
        private static int CountGroups(ScenarioData scenario)
        {
            if (scenario?.Humans == null) return 0;
            return scenario.Humans
                .Where(human => human != null && !string.IsNullOrEmpty(human.Group))
                .Select(human => human.Group)
                .Distinct()
                .Count();
        }

        private static ushort ToUshort(int value) => (ushort)Mathf.Clamp(value, 0, ushort.MaxValue);

        #endregion

        #region Stream 2 - Occupancy Grid

        /// <summary>
        /// Occupancy grid of the applied scenario, in image order: index = row * width + column, where a
        /// column steps along the image x axis and a row along its y axis. The origin published with it is
        /// the corner of cell (0, 0), which the occupancy image places at the (max x, min z) corner of the
        /// map bounds, so rows advance towards +z and columns towards -x.
        ///
        /// Walls are 100 and free cells 0, the values the rest of the project uses for occupancy. The
        /// message reuses the cached array, which is only ever replaced when a scenario is applied, so a
        /// message still waiting in the connector cannot see its data change under it.
        /// </summary>
        private void PublishMap()
        {
            if (!CanPublish(_mapTopicName)) return;

            // A scenario that is loaded but not applied still builds its map, and nothing signals that: the
            // cache is filled here the first time a runtime grid is available, and left alone afterwards.
            // The grid is already sampled by the environment builder, so this costs a null check per tick.
            if (_mapData == null && ScenarioNavigation.IsAvailable)
                RebuildMapCache();

            if (_mapData == null || _mapWidth <= 0 || _mapHeight <= 0) return;

            var msg = new MapMsg
            {
                resolution = _mapResolution,
                width = (uint)_mapWidth,
                height = (uint)_mapHeight,
                origin = _mapOrigin,
                data = _mapData
            };

            ROSTimeUtils.UpdateHeader(msg.header);
            msg.header.frame_id = FullFrameId;

            _ros.Publish(_mapTopicName, msg);

            if (logPublishEvents)
                Debug.Log($"[{name}] Published map to {_mapTopicName}: {_mapWidth}x{_mapHeight} at {_mapResolution} m/cell");
        }

        /// <summary>
        /// Samples the grid of the scenario being applied, once. The grid the agents plan on is the
        /// authority - the environment builder builds it from the very image the map is made of - and the
        /// loader's map asset is only sampled when the scenario has no runtime grid at all.
        /// </summary>
        private void RebuildMapCache()
        {
            ClearMapCache();

            OccupancyGrid grid = ScenarioNavigation.Obstacles;
            if (grid == null || !grid.IsValid)
                grid = BuildGridFromMapAsset();

            if (grid == null || !grid.IsValid) return;

            _mapWidth = grid.Width;
            _mapHeight = grid.Height;
            // The image is sampled with the same cell count on both axes, so one resolution describes the
            // grid; the x cell is published when the two differ by rounding.
            _mapResolution = grid.CellSizeX;
            _mapOrigin = MapOrigin(grid);

            _mapData = new sbyte[grid.CellCount];
            for (int row = 0; row < grid.Height; row++)
            {
                for (int column = 0; column < grid.Width; column++)
                    _mapData[row * grid.Width + column] = grid.IsWalkable(column, row) ? (sbyte)0 : (sbyte)100;
            }
        }

        /// <summary>
        /// Forgets the cached grid, which the map stream reads as "nothing to publish until a new one is
        /// sampled".
        /// </summary>
        private void ClearMapCache()
        {
            _mapData = null;
            _mapWidth = 0;
            _mapHeight = 0;
            _mapResolution = 0f;
            _mapOrigin = new PoseMsg();
        }

        /// <summary>
        /// Samples the occupancy image of the scenario when no runtime grid exists. The texture belongs to
        /// this call - the loader creates it - so it is released once sampled instead of leaking one per
        /// applied scenario.
        /// </summary>
        private OccupancyGrid BuildGridFromMapAsset()
        {
            ScenarioManager manager = _scenarioManager ??= FindAnyObjectByType<ScenarioManager>();
            ScenarioData scenario = manager != null ? manager.CurrentScenarioData : null;
            ScenarioLoader loader = manager != null ? manager.Loader : null;

            if (scenario == null || loader == null || string.IsNullOrEmpty(scenario.MapImage))
                return null;

            MapAsset asset = loader.LoadMap(scenario.MapImage);
            if (asset == null || asset.Kind != MapAssetKind.Image || asset.Texture == null)
                return null;

            OccupancyGrid grid = OccupancyGrid.FromTexture(asset.Texture, asset.Bounds, ScenarioNavigation.Resolution);
            Destroy(asset.Texture);
            return grid;
        }

        /// <summary>
        /// Pose of the corner cell (0, 0) starts at, in the ROS frame the other publishers use. The grid
        /// follows the occupancy image, whose first pixel sits at the (max x, min z) corner of the bounds,
        /// so the origin is that corner rather than the bottom-left corner of a ROS map.
        /// </summary>
        private static PoseMsg MapOrigin(OccupancyGrid grid)
        {
            Bounds bounds = grid.WorldBounds;
            return Util.Geometry.GetMPose(new Vector3(bounds.max.x, 0f, bounds.min.z), Quaternion.identity);
        }

        #endregion

        #region Stream 3 - Humans

        /// <summary>
        /// One entry per human of the scene, sorted by track id so a client can follow a person from one
        /// message to the next. The pose comes from the transform and the twist from the velocity the
        /// movement controller currently applies; the crowd controller exposes no yaw rate, so the angular
        /// part of the twist stays zero.
        /// </summary>
        private void PublishPeople()
        {
            if (!CanPublish(_peopleTopicName)) return;

            HumanAgent[] humans = FindHumans();
            var entries = new PersonEntryMsg[humans.Length];

            for (int index = 0; index < humans.Length; index++)
            {
                HumanAgent human = humans[index];
                Transform humanTransform = human.transform;

                var entry = new PersonEntryMsg
                {
                    track_id = (ulong)Mathf.Max(0, human.agentId),
                    pose = Util.Geometry.GetMPose(humanTransform.position, humanTransform.rotation),
                    twist = new TwistMsg
                    {
                        linear = Util.Geometry.GetGeometryVector3(WorldVelocityOf(human)),
                        angular = Util.Geometry.GetGeometryVector3(Vector3.zero)
                    }
                };

                ROSTimeUtils.UpdateHeader(entry.header);
                entry.header.frame_id = FullFrameId;
                entries[index] = entry;
            }

            var msg = new PersonEntryArrayMsg { people = entries };
            ROSTimeUtils.UpdateHeader(msg.header);
            msg.header.frame_id = FullFrameId;

            _ros.Publish(_peopleTopicName, msg);

            if (logPublishEvents)
                Debug.Log($"[{name}] Published {entries.Length} people to {_peopleTopicName}");
        }

        /// <summary>
        /// Planar velocity of a human, in the ROS frame. It is taken from the velocity the movement
        /// controller reports; the human's own speed property is a scalar and cannot say in which direction
        /// the agent is walking.
        /// </summary>
        private static Vector3<FLU> WorldVelocityOf(HumanAgent human)
        {
            HumanMovement movement = human.GetComponent<HumanMovement>();
            if (movement == null) return Vector3.zero.To<FLU>();

            Vector2 velocity = movement.CurrentVelocity;
            return new Vector3(velocity.x, 0f, velocity.y).To<FLU>();
        }

        /// <summary>
        /// Humans of the scene, pooled ones excluded: a pooled agent is not being simulated and would
        /// otherwise be published as a person standing at the pool's origin.
        /// </summary>
        private static HumanAgent[] FindHumans()
        {
            HumanAgent[] humans = FindObjectsByType<HumanAgent>(FindObjectsInactive.Exclude);
            return humans.OrderBy(human => human.agentId).ToArray();
        }

        #endregion

        #region Stream 4 - Runtime State

        /// <summary>
        /// JSON snapshot of what the three typed streams do not carry: the run state, the clock, the
        /// scenario identity, the grid size, the robot, and what the camera looks at.
        ///
        /// Keys, with every pose in the ROS frame the other publishers use (x forward, y left, z up) and
        /// yaw in radians:
        ///   simulation_state       "idle", "ready", "running" or "paused", as the ScenarioManager publishes it
        ///   playing                true while a scenario is applied and the clock runs
        ///   paused                 true while the simulation clock is paused
        ///   stopped                true while no scenario is applied (idle or ready)
        ///   scenario_applied       true while the agents of the current scenario are in the scene
        ///   sim_time_seconds       time of the simulation clock, in seconds, null without a clock
        ///   time_scale             time scale applied to the simulation clock, null without a clock
        ///   scenario_id            name the scenario was loaded under (its YAML file)
        ///   scenario_name          name declared inside the scenario, null when none is loaded
        ///   environment            map of the scenario, or its declared location
        ///   map_name               map identifier authored in the scenario
        ///   map_width              width of the occupancy grid, in cells, 0 without a grid
        ///   map_height             height of the occupancy grid, in cells, 0 without a grid
        ///   map_resolution         metres per grid cell, 0 without a grid
        ///   human_count            humans currently simulated
        ///   humans                 one entry per human: id, position, velocity, goal, group, controller
        ///   robot                  robot pose {x, y, z, yaw}, null when the scene has no robot
        ///   robot_has_goal         true while the robot drives towards a goal
        ///   robot_goal             robot goal {x, y, z}, null when it has none
        ///   camera_focus           name of the agent the camera follows, null when it follows none
        ///   camera_focus_is_robot  true when the followed agent is the robot
        ///   camera_focus_is_human  true when the followed agent is a human
        ///   camera_focus_agent_id  id of the followed human, null otherwise
        ///   camera_tool            "select", "move", "rotate" or "zoom", the tool of the view
        ///   camera_mode            "free", "topdown", "firstperson", "thirdperson" or "orbit"
        ///   camera_pose            camera pose {x, y, z, yaw}, null without a camera
        /// </summary>
        private string BuildStateJson()
        {
            ScenarioManager manager = _scenarioManager ??= FindAnyObjectByType<ScenarioManager>();
            Supervisor supervisor = Supervisor.Instance;
            _clock ??= Clock.Instance;
            CameraController camera = _cameraController ??= FindAnyObjectByType<CameraController>();
            Robot robot = _robot != null ? _robot : (_robot = FindAnyObjectByType<Robot>());

            ScenarioData scenario = manager != null ? manager.CurrentScenarioData : null;
            SimulationState state = ResolveState(manager, supervisor);
            bool paused = supervisor != null ? supervisor.IsPaused : (_clock != null && _clock.IsPaused);
            bool applied = state == SimulationState.Running || state == SimulationState.Paused;

            Transform focus = camera != null ? camera.GetCurrentFollowTarget() : null;
            HumanAgent focusedHuman = focus != null ? focus.GetComponentInParent<HumanAgent>() : null;

            var payload = new Dictionary<string, object>
            {
                { "simulation_state", StateName(state) },
                { "playing", state == SimulationState.Running && !paused },
                { "paused", paused },
                { "stopped", !applied },
                { "scenario_applied", applied },
                { "sim_time_seconds", _clock != null ? (object)_clock.CurrentTimeSeconds : null },
                { "time_scale", _clock != null ? (object)_clock.TimeScale : null },
                { "scenario_id", manager != null ? manager.CurrentScenarioId : null },
                { "scenario_name", scenario != null ? scenario.Name : null },
                { "environment", EnvironmentOf(scenario) },
                { "map_name", scenario != null ? scenario.MapImage : null },
                { "map_width", _mapWidth },
                { "map_height", _mapHeight },
                { "map_resolution", _mapResolution },
                { "human_count", FindHumans().Length },
                { "humans", HumansJson(FindHumans()) },
                { "robot", robot != null ? PoseJson(robot.RobotTransform) : null },
                { "robot_has_goal", robot != null && robot.HasGoal },
                { "robot_goal", robot != null && robot.HasGoal ? PositionJson(robot.Goal) : null },
                { "camera_focus", focus != null ? focus.name : null },
                { "camera_focus_is_robot", focus != null && focus.GetComponentInParent<Robot>() != null },
                { "camera_focus_is_human", focusedHuman != null },
                { "camera_focus_agent_id", focusedHuman != null ? (object)focusedHuman.agentId : null },
                { "camera_tool", camera != null ? camera.ActiveTool.ToString().ToLowerInvariant() : null },
                { "camera_mode", camera != null ? camera.CurrentMode.ToString().ToLowerInvariant() : null },
                { "camera_pose", camera != null && camera.mainCamera != null ? PoseJson(camera.mainCamera.transform) : null }
            };

            return JsonConvert.SerializeObject(payload, Formatting.None);
        }

        private void PublishState()
        {
            if (!CanPublish(_stateTopicName)) return;

            string json = BuildStateJson();
            _ros.Publish(_stateTopicName, new StringMsg(json));

            if (logPublishEvents && Time.frameCount % 60 == 0)
                Debug.Log($"[{name}] Published state to {_stateTopicName}: {json}");
        }

        /// <summary>
        /// Run state as the application understands it. The manager publishes every transition, so its last
        /// word is used, corrected by the two things the manager cannot see: the clock is the authority on
        /// pausing, and a scenario that is no longer loaded means idle whatever was published before.
        /// </summary>
        private SimulationState ResolveState(ScenarioManager manager, Supervisor supervisor)
        {
            if (manager == null || !manager.HasScenarioLoaded)
                return SimulationState.Idle;

            SimulationState state = _lastState ?? SimulationState.Ready;
            if (supervisor != null && supervisor.IsPaused && state == SimulationState.Running)
                return SimulationState.Paused;

            return state;
        }

        private static string StateName(SimulationState state)
        {
            switch (state)
            {
                case SimulationState.Idle: return "idle";
                case SimulationState.Running: return "running";
                case SimulationState.Paused: return "paused";
                default: return "ready";
            }
        }

        /// <summary>Pose of a transform in the ROS frame, or null when there is no transform.</summary>
        private static object PoseJson(Transform transform)
        {
            if (transform == null) return null;

            Vector3<FLU> position = transform.position.To<FLU>();
            return new
            {
                x = position.x,
                y = position.y,
                z = position.z,
                yaw = YawOf(transform)
            };
        }

        /// <summary>Position in the ROS frame, as a JSON object.</summary>
        private static object PositionJson(Vector3 worldPosition)
        {
            Vector3<FLU> position = worldPosition.To<FLU>();
            return new { x = position.x, y = position.y, z = position.z };
        }

        /// <summary>
        /// One entry per simulated human, in the ROS frame. The typed people stream carries the same
        /// information as a real message; this list is what makes the JSON snapshot readable on its own,
        /// by a client that has no message definitions at all.
        /// </summary>
        private static object HumansJson(HumanAgent[] humans)
        {
            var list = new List<object>(humans.Length);

            foreach (HumanAgent human in humans)
            {
                Vector3<FLU> position = human.Position.To<FLU>();
                Vector3<FLU> velocity = human.Velocity.To<FLU>();

                list.Add(new
                {
                    id = human.agentId,
                    x = position.x,
                    y = position.y,
                    z = position.z,
                    vx = velocity.x,
                    vy = velocity.y,
                    vz = velocity.z,
                    speed = human.CurrentSpeed,
                    goal = human.HasDestination ? PositionJson(human.CurrentGoal) : null,
                    group = human.Group != null ? (object)human.Group.Id : null,
                    controller = human.IsExternallyControlled ? "external" : "sfm",
                    end_behavior = human.EndBehavior.ToString().ToLowerInvariant()
                });
            }

            return list;
        }

        /// <summary>
        /// Heading of a transform around the ROS z axis, read from its forward vector rather than from the
        /// Unity euler angles, whose yaw is expressed around the Unity up axis and would come out negated.
        /// </summary>
        private static float YawOf(Transform transform)
        {
            Vector3<FLU> forward = (transform.rotation * Vector3.forward).To<FLU>();
            return Mathf.Atan2(forward.y, forward.x);
        }

        #endregion

        #region Editor Utilities

        [ContextMenu("Log Configuration")]
        private void EditorLogConfiguration()
        {
            Debug.Log($"[{name}] Configuration:\n" +
                      $"  Prefix: '{_prefix}'\n" +
                      $"  Scene info: {_sceneInfoTopicName}\n" +
                      $"  Map: {_mapTopicName}\n" +
                      $"  People: {_peopleTopicName}\n" +
                      $"  State: {_stateTopicName}\n" +
                      $"  Grid: {_mapWidth}x{_mapHeight} at {_mapResolution} m/cell\n" +
                      $"  ROS connection: {(_ros != null ? "OK" : "Missing")}\n" +
                      $"  EnvROS: {(_envROS != null ? "OK" : "Missing")}");
        }

        [ContextMenu("Rebuild Map Cache")]
        private void EditorRebuildMapCache()
        {
            RebuildMapCache();
            _mapPublishPending = true;
            Debug.Log($"[{name}] Grid cache rebuilt: {_mapWidth}x{_mapHeight}");
        }

        #endregion
    }
}
