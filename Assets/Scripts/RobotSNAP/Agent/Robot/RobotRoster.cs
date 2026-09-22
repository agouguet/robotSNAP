using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RobotSNAP.Core.Scenario;

namespace RobotSNAP.Agents
{
    /// <summary>
    /// Owns the robots of the running scenario: which ones exist, what type each drives as, and where each
    /// one starts.
    ///
    /// An instance is kept between two applications of a scenario when its id and its type both still match,
    /// because building a robot means building an articulation - a few dozen bodies and joints - and a
    /// scenario is re-applied every time the user presses reset. The instance that comes back is the same
    /// picture, the same subscriptions and the same streams; only its pose, its route and its speed are
    /// written again.
    ///
    /// Which robot a client reaches without naming one is decided by the id, not by the order of the list:
    /// <c>robot_1</c> is the one that keeps the legacy topics. The order of the scenario still decides how
    /// the interface lists them, and nothing else.
    /// </summary>
    public sealed class RobotRoster : MonoBehaviour
    {
        /// <summary>Id of the robot that answers on the topics a single-robot client already knows.</summary>
        public const string PrimaryId = "robot_1";

        /// <summary>A body a type can be built from, when it is not the default one of the environment.</summary>
        [System.Serializable]
        public sealed class Body
        {
            [Tooltip("Type id of RobotProfiles, such as jackal or kuri.")]
            public string TypeId;

            [Tooltip("Prefab instantiated for that type. It carries its own Robot, its own wheels and its own sensors.")]
            public GameObject Prefab;
        }

        [Header("Robot bodies")]
        [Tooltip("Body used for a type the catalogue does not know. The environment hands its own robot prefab " +
                 "here, so a project with a single base keeps working.")]
        [SerializeField] private GameObject _defaultPrefab;

        [Tooltip("Body pinned per type, for a scene that wants to choose the prefab itself. Anything not " +
                 "pinned here comes from the catalogue the editor tool writes.")]
        [SerializeField] private List<Body> _bodies = new List<Body>();

        [Header("Debug")]
        [SerializeField] private bool _logEvents = false;

        private sealed class Slot
        {
            public string Id;
            public string TypeId;
            public Robot Robot;
            public RobotIdentity Identity;

            /// <summary>
            /// Where the scenario put this robot. Kept because the transform of a freshly teleported
            /// articulation is only settled at the next physics step, so reading it back to compare two
            /// starts would compare zeroes.
            /// </summary>
            public Vector3 StartPosition;
        }

        private readonly List<Slot> _slots = new List<Slot>();
        private readonly List<RobotScenarioConfig> _desired = new List<RobotScenarioConfig>();
        private readonly List<Vector3> _route = new List<Vector3>();

        /// <summary>
        /// Walkable grid per clearance, kept for the length of one application: a Husky needs a wider corridor
        /// than a pedestrian, and every robot of the same type would otherwise grow the same grid again.
        /// </summary>
        private readonly Dictionary<float, OccupancyGrid> _walkablePerRadius = new Dictionary<float, OccupancyGrid>();
        private readonly List<float> _staleRadii = new List<float>();

        /// <summary>
        /// The catalogue the project ships, read on first use. It is an asset, so the lookup happens once per
        /// roster rather than once per robot: several robots of several types are built in one application.
        /// </summary>
        private RobotCatalog _catalog;
        private bool _catalogRead;

        /// <summary>The roster of the running environment, or null before one is created.</summary>
        public static RobotRoster Current { get; private set; }

        private RobotCatalog Catalog
        {
            get
            {
                if (!_catalogRead)
                {
                    _catalog = RobotCatalog.Load();
                    _catalogRead = true;
                }

                return _catalog;
            }
        }

        /// <summary>Number of robots currently in the scene.</summary>
        public int Count => _slots.Count;

        /// <summary>
        /// The robot a client reaches without naming one: <c>robot_1</c> when it is part of the scenario,
        /// and the first robot of the list when a scenario named its robots differently.
        /// </summary>
        public Robot Primary
        {
            get
            {
                foreach (Slot slot in _slots)
                {
                    if (slot.Id == PrimaryId)
                        return slot.Robot;
                }

                return _slots.Count > 0 ? _slots[0].Robot : null;
            }
        }

        private void Awake()
        {
            Current = this;
        }

        private void OnDestroy()
        {
            if (Current == this)
                Current = null;
        }

        /// <summary>
        /// Body to build a type from: the one pinned in the scene, the one the project catalogued for that
        /// type, and the default body of the environment in that order.
        ///
        /// The catalogue is what makes a scenario's type name mean something: the roster is created at run
        /// time, so it has nothing wired by hand, and it is the catalogue asset that says a "jackal" is the
        /// Jackal prefab of this project. A type nobody catalogued still runs - as the base.
        /// </summary>
        public GameObject PrefabFor(string typeId)
        {
            GameObject pinned = FindIn(_bodies, typeId);
            if (pinned != null)
                return pinned;

            GameObject catalogued = Catalog != null ? Catalog.PrefabFor(typeId) : null;
            return catalogued != null ? catalogued : _defaultPrefab;
        }

        /// <summary>The default body, so a caller can hand the environment's prefab over without one.</summary>
        public void SetDefaultPrefab(GameObject prefab)
        {
            if (_defaultPrefab == null)
                _defaultPrefab = prefab;
        }

        /// <summary>Every robot of the scenario, in the order the scenario lists them.</summary>
        public void FillRobots(List<Robot> destination)
        {
            if (destination == null)
                return;

            destination.Clear();
            foreach (Slot slot in _slots)
            {
                if (slot.Robot != null)
                    destination.Add(slot.Robot);
            }
        }

        /// <summary>The robot that carries an id, or false when the scenario has none.</summary>
        public bool TryGet(string id, out Robot robot)
        {
            robot = null;
            if (string.IsNullOrWhiteSpace(id))
                return false;

            string wanted = id.Trim();
            foreach (Slot slot in _slots)
            {
                if (string.Equals(slot.Id, wanted, System.StringComparison.OrdinalIgnoreCase))
                {
                    robot = slot.Robot;
                    return robot != null;
                }
            }

            return false;
        }

        /// <summary>The id of a robot of the roster, or null when it is not one of them.</summary>
        public string IdOf(Robot robot)
        {
            foreach (Slot slot in _slots)
            {
                if (slot.Robot == robot)
                    return slot.Id;
            }

            return null;
        }

        /// <summary>
        /// Brings the roster in line with a scenario: instances that still match are kept and placed again,
        /// the ones the scenario dropped are destroyed, and the ones it added are built.
        ///
        /// The route of a robot is its waypoints followed by its goal, so a robot with a single goal - every
        /// scenario written before several were possible - drives exactly as it always did.
        /// </summary>
        public IEnumerator Sync(
            IReadOnlyList<RobotScenarioConfig> configs,
            ScenarioLoader loader,
            ScenarioData scenario,
            GameObject fallbackPrefab,
            bool resetPose,
            bool log)
        {
            if (fallbackPrefab != null)
                SetDefaultPrefab(fallbackPrefab);

            _desired.Clear();
            _walkablePerRadius.Clear();
            if (configs != null)
            {
                foreach (RobotScenarioConfig config in configs)
                {
                    if (config != null)
                        _desired.Add(config);
                }
            }

            var kept = new List<Slot>(_desired.Count);
            foreach (RobotScenarioConfig config in _desired)
            {
                string id = string.IsNullOrWhiteSpace(config.Id)
                    ? ScenarioData.DefaultRobotId(kept.Count)
                    : config.Id.Trim();
                RobotProfile profile = RobotProfiles.Find(config.Type);

                Slot slot = TakeReusable(id, profile.Id);
                if (slot == null)
                    slot = Build(id, profile, log);

                if (slot == null)
                    continue;

                kept.Add(slot);
            }

            // Whatever the scenario no longer asks for is gone, with its streams and its subscriptions.
            foreach (Slot stale in _slots)
            {
                if (stale.Robot != null)
                    Destroy(stale.Robot.gameObject);
            }

            _slots.Clear();
            _slots.AddRange(kept);

            for (int index = 0; index < _slots.Count; index++)
            {
                Place(_slots[index], _desired[index], loader, scenario, resetPose, log);
            }

            WarnAboutOverlap(log);
            yield return null;
        }

        /// <summary>Removes every robot and forgets them, for an environment that is being torn down.</summary>
        public void Clear()
        {
            foreach (Slot slot in _slots)
            {
                if (slot.Robot != null)
                    Destroy(slot.Robot.gameObject);
            }

            _slots.Clear();
            _desired.Clear();
        }

        /// <summary>
        /// The slot that can serve an id and a type, taken out of the live list so that whatever stays behind
        /// is what the scenario dropped.
        /// </summary>
        private Slot TakeReusable(string id, string typeId)
        {
            for (int index = 0; index < _slots.Count; index++)
            {
                Slot slot = _slots[index];
                if (slot.Robot == null)
                    continue;

                if (!string.Equals(slot.Id, id, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.Equals(slot.TypeId, typeId, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                _slots.RemoveAt(index);
                return slot;
            }

            return null;
        }

        private Slot Build(string id, RobotProfile profile, bool log)
        {
            GameObject prefab = PrefabFor(profile.Id);
            if (prefab == null)
            {
                Debug.LogError(
                    $"[RobotRoster] No body to build '{id}' ({profile.Id}) from: neither a prefab for that " +
                    "type nor a default one. The scenario runs without that robot.");
                return null;
            }

            GameObject instance = Instantiate(prefab, transform);
            instance.name = id;
            instance.tag = "Robot";

            var robot = instance.GetComponent<Robot>();
            if (robot == null)
            {
                Debug.LogError($"[RobotRoster] The body of '{profile.Id}' carries no Robot component.");
                Destroy(instance);
                return null;
            }

            var identity = instance.GetComponent<RobotIdentity>();
            if (identity == null)
                identity = instance.AddComponent<RobotIdentity>();

            identity.Bind(id, IsPrimary(id), profile);

            // Where this body rests, read from the body itself: the wheels of a Freight touch the ground under
            // its base link, those of a Jackal hang 6.5 cm below it. Measuring here, on a fresh instance,
            // gives a real answer for every type - including one somebody adds later.
            if (log)
                Debug.Log($"[RobotRoster] {id} rests {robot.MeasureGroundOffset():0.####} m above its base link.");

            robot.ApplyProfile(profile);

            if (log)
                Debug.Log($"[RobotRoster] Built {id} as {profile.DisplayName} (radius {profile.Radius:0.##} m, " +
                          $"{profile.Mass:0.#} kg).");

            return new Slot { Id = id, TypeId = profile.Id, Robot = robot, Identity = identity };
        }

        private static GameObject FindIn(List<Body> bodies, string typeId)
        {
            if (bodies == null || string.IsNullOrWhiteSpace(typeId))
                return null;

            foreach (Body body in bodies)
            {
                if (body == null || body.Prefab == null || string.IsNullOrWhiteSpace(body.TypeId))
                    continue;

                if (string.Equals(body.TypeId.Trim(), typeId, System.StringComparison.OrdinalIgnoreCase))
                    return body.Prefab;
            }

            return null;
        }

        private void Place(
            Slot slot,
            RobotScenarioConfig config,
            ScenarioLoader loader,
            ScenarioData scenario,
            bool resetPose,
            bool log)
        {
            Robot robot = slot.Robot;
            if (robot == null)
                return;

            if (resetPose && loader != null)
            {
                (Vector3 position, Quaternion rotation) = loader.GetPositionAndRotation(scenario, config.StartRef);
                position = FitToFootprint(slot, position, config.StartRef, log);
                slot.StartPosition = position;
                robot.Reset();

                // The scenario says where the robot stands; it does not say how far its own base link floats
                // above the floor. A body dropped with its base link on the ground plane sinks its wheels
                // into the floor and gets pushed back out by the solver, so the pose is raised by what this
                // body measured at build time.
                Vector3 grounded = position + Vector3.up * robot.GroundOffset;
                robot.SetBaseLinkPose(grounded, rotation);

                if (log)
                    Debug.Log($"[RobotRoster] {slot.Id} starts at {position} " +
                              $"(base link at {grounded.y:0.###} m), yaw {rotation.eulerAngles.y:0.#}.");
            }

            _route.Clear();
            if (config.WaypointRefs != null)
            {
                foreach (string waypointRef in config.WaypointRefs)
                {
                    if (!string.IsNullOrWhiteSpace(waypointRef))
                        _route.Add(Resolve(loader, scenario, waypointRef));
                }
            }

            _route.Add(Resolve(loader, scenario, config.GoalRef));
            robot.SetGoals(_route);
            robot.SetSpeed(config.Speed);
        }

        private static Vector3 Resolve(ScenarioLoader loader, ScenarioData scenario, string reference)
        {
            return loader != null ? loader.GetPositionAndRotation(scenario, reference).position : Vector3.zero;
        }

        /// <summary>
        /// Brings a start point back onto ground this robot can occupy.
        ///
        /// The grid of the scenario keeps a pedestrian of 25 cm clear of the walls, which is not enough for a
        /// Husky: its declared footprint is 49 cm, so a start an author placed against a wall can be inside
        /// one for that robot and free for a TurtleBot. Physics alone does not get a robot out of a wall it
        /// was teleported into - it stands there with its wheels commanded and never moves - so the point is
        /// projected onto the nearest cell its own footprint fits in, and the move is reported.
        /// </summary>
        private Vector3 FitToFootprint(Slot slot, Vector3 position, string startReference, bool log)
        {
            if (slot.Robot == null)
                return position;

            OccupancyGrid grid = WalkableFor(slot.Robot.Radius);
            if (grid == null)
                return position;

            var point = new Vector2(position.x, position.z);
            if (grid.IsWorldWalkable(point))
                return position;

            // Far enough to leave a corridor wall, close enough that the scenario keeps meaning what it says.
            float searchRadius = Mathf.Max(1.5f, slot.Robot.Radius * 4f);
            if (!ScenarioNavigation.TryProjectToWalkable(grid, point, searchRadius, out Vector2 walkable))
            {
                Debug.LogWarning(
                    $"[RobotRoster] {slot.Id} starts at ({point.x:0.##}, {point.y:0.##}), where its " +
                    $"{slot.Robot.Radius * 2f:0.##} m footprint does not fit, and no walkable cell is within " +
                    $"{searchRadius:0.#} m. Move the start of '{startReference}' in the scenario editor.");
                return position;
            }

            Debug.LogWarning(
                $"[RobotRoster] {slot.Id} starts at ({point.x:0.##}, {point.y:0.##}), where its " +
                $"{slot.Robot.Radius * 2f:0.##} m footprint does not fit; it was placed at " +
                $"({walkable.x:0.##}, {walkable.y:0.##}) instead. Move the start of '{startReference}' in the " +
                "scenario editor to choose where it really begins.");

            if (log)
                Debug.Log($"[RobotRoster] {slot.Id} start moved from {position} to ({walkable.x:0.##}, 0, {walkable.y:0.##}).");

            return new Vector3(walkable.x, position.y, walkable.y);
        }

        /// <summary>Walkable grid as a robot of that footprint sees it, grown once per radius.</summary>
        private OccupancyGrid WalkableFor(float radius)
        {
            OccupancyGrid obstacles = ScenarioNavigation.Obstacles;
            if (obstacles == null || !obstacles.IsValid)
                return null;

            // Quantised so two robots of nearly the same size share one grid.
            float key = Mathf.Round(Mathf.Max(0.05f, radius) * 100f) / 100f;
            if (_walkablePerRadius.TryGetValue(key, out OccupancyGrid cached))
                return cached;

            OccupancyGrid grown = obstacles.WithObstaclesInflatedBy(key);
            _walkablePerRadius[key] = grown;
            return grown;
        }

        /// <summary>True for the robot that keeps the streams a single-robot client already knows.</summary>
        public static bool IsPrimary(string id)
        {
            return string.Equals(id, PrimaryId, System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reports two robots of the scenario placed inside each other. Nothing moves them - where a robot
        /// starts is what its author decided - but a scenario that stacks two bodies is worth saying out
        /// loud, because the physics will separate them and the run will not match the map.
        /// </summary>
        private void WarnAboutOverlap(bool log)
        {
            for (int first = 0; first < _slots.Count; first++)
            {
                for (int second = first + 1; second < _slots.Count; second++)
                {
                    Robot a = _slots[first].Robot;
                    Robot b = _slots[second].Robot;
                    if (a == null || b == null)
                        continue;

                    // The authored starts are what a person drew, so they are what the check reads: the
                    // transform of a robot that has just been teleported is not settled yet.
                    Vector3 offset = _slots[first].StartPosition - _slots[second].StartPosition;
                    offset.y = 0f;
                    if (offset.magnitude >= a.Radius + b.Radius)
                        continue;

                    Debug.LogWarning(
                        $"[RobotRoster] {_slots[first].Id} and {_slots[second].Id} start " +
                        $"{offset.magnitude:0.##} m apart, closer than their footprints " +
                        $"({a.Radius:0.##} + {b.Radius:0.##} m). They will push each other apart.");
                }
            }

            if (log && _slots.Count > 1)
                Debug.Log($"[RobotRoster] {_slots.Count} robots in the scenario; {PrimaryId} keeps the legacy topics.");
        }
    }
}
