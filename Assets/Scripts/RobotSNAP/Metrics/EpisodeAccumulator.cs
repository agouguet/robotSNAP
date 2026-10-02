using System;
using System.Collections.Generic;
using UnityEngine;

namespace RobotSNAP.Metrics
{
    /// <summary>One crowd member's pose at one control step, as the accumulator reads it.</summary>
    public readonly struct HumanSample
    {
        public readonly int Id;
        public readonly Vector3 Position;

        /// <summary>
        /// Footprint radius of this crowd member, in metres.
        ///
        /// It travels with the sample because a social distance is a distance between *bodies*: the gap two
        /// people leave each other is measured from their outlines, not from their centres. Two agents whose
        /// centres are one metre apart are shoulder to shoulder when each is half a metre wide, and the
        /// recorder that only read centres called that a comfortable metre.
        /// </summary>
        public readonly float Radius;

        public HumanSample(int id, Vector3 position, float radius = 0f)
        {
            Id = id;
            Position = position;
            Radius = Mathf.Max(0f, radius);
        }
    }

    /// <summary>One robot's pose at one control step, as the accumulator reads it.</summary>
    public readonly struct RobotSample
    {
        /// <summary>Roster id of the robot (<c>robot_1</c>), which is the key its track is filed under.</summary>
        public readonly string Id;

        public readonly Vector3 Position;

        /// <summary>Footprint radius of this robot, in metres; see <see cref="HumanSample.Radius"/>.</summary>
        public readonly float Radius;

        public RobotSample(string id, Vector3 position, float radius = 0f)
        {
            Id = id;
            Position = position;
            Radius = Mathf.Max(0f, radius);
        }
    }

    /// <summary>
    /// Builds one episode's numbers from the samples of a running session. It is a plain class with no
    /// MonoBehaviour on it on purpose: the whole of a social-navigation metric is a question of arithmetic over
    /// a sequence of poses, and arithmetic that cannot be called without entering Play mode is arithmetic
    /// nobody tests.
    ///
    /// The accumulator is fed one sample per control step - the step a velocity command is applied on - and
    /// every metric is defined where it is computed:
    ///
    ///   path length        the sum of the planar distances between consecutive robot poses, so a robot that
    ///                      drives in a circle pays for the circle and one that drives straight does not
    ///   straight line      the planar distance from the first robot pose to the goal, or to the last pose
    ///                      when the episode ended without one: the shortest a perfect run could have been
    ///   average speed      path length over elapsed simulated time, never over wall time
    ///   maximum speed      the largest <c>distance / elapsed simulated time</c> between two consecutive
    ///                      samples, so a pause between two samples cannot invent a spike
    ///   human distance     the planar distance from the robot to the nearest crowd member. The minimum is
    ///                      taken over the episode, the average over the samples. An episode with no human
    ///                      reports <see cref="EpisodeMetrics.NoHumanDistance"/> rather than a zero that
    ///                      would read as a collision
    ///   clearance          the human distance minus the two bodies: the robot's footprint radius and the
    ///                      crowd member's. It is the gap left between the outlines, which is the distance a
    ///                      bystander can see, rather than the distance between two centres. <c>min_clearance_m</c>
    ///                      is its smallest value over the episode and can go negative when the bodies overlap
    ///   personal space     a clearance of <c>personalSpaceRadius</c> metres or less. An intrusion is one
    ///                      rising edge - the robot going from "no human inside" to "at least one human
    ///                      inside" - so a robot that sits in the space counts once, not once per sample, and
    ///                      the time spent is the simulated time sampled while at least one human was inside.
    ///                      Because it is a clearance, a passage that looks like half a metre between the
    ///                      bodies is what the radius counts, whatever the agents are made of
    ///
    /// The episode has exactly one subject: the tracked robot, whose id is <see cref="Robot"/> and whose path
    /// the scalar metrics - path length, speed, straight line - describe. A scenario that fields several
    /// robots is still one episode of that robot, because a metric averaged over two robots at once would be
    /// the metric of neither; the trajectories of the other robots are recorded beside it under their own
    /// roster keys, so a dashboard can draw the whole run while a benchmark still compares one controlled
    /// robot against one mission. <see cref="EpisodeMetrics.Robots"/> lists every robot that was observed,
    /// the tracked one first.
    /// </summary>
    public sealed class EpisodeAccumulator
    {
        private readonly Dictionary<string, TrajectoryBuffer> _robotTracks = new();
        private readonly List<string> _robotOrder = new();
        private readonly List<RobotSample> _singleRobot = new(1);
        private readonly Dictionary<int, TrajectoryBuffer> _humanTracks = new();
        private readonly int _trajectoryCapacity;
        private readonly double _personalSpaceRadius;
        private readonly string _id;
        private readonly int _index;
        private readonly string _session;
        private readonly string _scenario;
        private readonly string _robot;
        private readonly string _startedAt;
        private readonly double _startWorldSeconds;

        private bool _hasRobotPose;
        private Vector3 _firstRobotPose;
        private Vector3 _lastRobotPose;
        private double _lastRobotSeconds;
        private double _previousSampleSeconds;

        private bool _hasGoal;
        private Vector3 _goal;

        private double _pathLength;
        private double _maxSpeed;
        private double _humanDistanceSum;
        private double _minHumanDistance = double.PositiveInfinity;
        private double _minClearance = double.PositiveInfinity;
        private double _closestHumanRadius;
        private double _robotRadius;
        private int _humanSamples;
        private double _personalSpaceSeconds;
        private int _personalSpaceIntrusions;
        private bool _personalSpaceEntered;
        private int _steps;
        private string _outcome;

        /// <param name="id">Identifier of this episode, unique within its session.</param>
        /// <param name="index">1-based place of this episode in its session, in the order the episodes finished.</param>
        /// <param name="session">Identifier of the session that holds it.</param>
        /// <param name="scenario">Id of the scenario that was applied.</param>
        /// <param name="robotId">Roster id of the tracked robot.</param>
        /// <param name="personalSpaceRadius">Radius in metres of the disc the social metrics measure against.</param>
        /// <param name="trajectoryCapacity">Points kept per agent; see <see cref="TrajectoryBuffer"/>.</param>
        /// <param name="startedAt">ISO-8601 UTC instant the episode started.</param>
        /// <param name="startWorldSeconds">Simulated clock reading at the first sample.</param>
        public EpisodeAccumulator(
            string id,
            int index,
            string session,
            string scenario,
            string robotId,
            double personalSpaceRadius,
            int trajectoryCapacity,
            string startedAt,
            double startWorldSeconds)
        {
            _id = id;
            _index = index;
            _session = session;
            _scenario = scenario;
            _robot = robotId;
            _personalSpaceRadius = Math.Max(0.0, personalSpaceRadius);
            _trajectoryCapacity = trajectoryCapacity;
            _startedAt = startedAt;
            _startWorldSeconds = startWorldSeconds;
        }

        /// <summary>Identifier of the episode being built.</summary>
        public string Id => _id;

        /// <summary>Roster id of the tracked robot.</summary>
        public string Robot => _robot;

        /// <summary>Id of the scenario the episode is running.</summary>
        public string Scenario => _scenario;

        /// <summary>Simulated seconds between the first sample and <paramref name="worldSeconds"/> so far.</summary>
        public double ElapsedSeconds(double worldSeconds) => Math.Max(0.0, worldSeconds - _startWorldSeconds);

        /// <summary>The outcome the run has already earned, or null while nothing has happened yet.</summary>
        public string LatchedOutcome => _outcome;

        /// <summary>
        /// Remembers the first thing that happened to this episode, and only the first.
        ///
        /// A run is not over because its robot reached the goal, clipped a wall or left the map: a driver
        /// carries on from there, and the trajectory of the session is what the session ran. What each of
        /// those moments does now is name the run - the episode is still filed as the goal it reached or the
        /// collision it took - while the samples keep coming until the session itself ends. The order is the
        /// recorder's own: a robot that reached its goal is filed as arriving, whatever it did afterwards.
        /// </summary>
        public void LatchOutcome(string outcome)
        {
            if (string.IsNullOrEmpty(outcome) || _outcome != null)
                return;

            _outcome = outcome;
        }

        /// <summary>
        /// Names the goal the straight-line metric measures against. Called when the robot's goal is known; an
        /// episode that never gets one falls back to its end pose.
        /// </summary>
        public void SetGoal(Vector3 goal)
        {
            _goal = goal;
            _hasGoal = true;
        }

        /// <summary>
        /// Records one control step of a single-robot scene: the tracked robot, the crowd, and when. It is the
        /// shape every caller before the roster existed used, and it is kept so an episode can be driven
        /// without building a fleet of one.
        /// </summary>
        public void Sample(double worldSeconds, Vector3 robotPosition, IReadOnlyList<HumanSample> humans)
        {
            _singleRobot.Clear();
            _singleRobot.Add(new RobotSample(_robot, robotPosition));
            Sample(worldSeconds, _singleRobot, humans);
        }

        /// <summary>
        /// Records one control step: where every robot is, where the crowd is, and when. The first sample
        /// starts each path; later ones extend it and feed the distance and personal-space metrics, which are
        /// computed against the tracked robot only - the one whose id this accumulator was built with - so the
        /// scalars of an episode never mix two robots' motion. The poses are projected on the ground plane,
        /// because a social metric is a horizontal one and the lidar height has nothing to say about it.
        /// </summary>
        public void Sample(double worldSeconds, IReadOnlyList<RobotSample> robots, IReadOnlyList<HumanSample> humans)
        {
            Vector3 trackedPosition = _lastRobotPose;
            float trackedRadius = 0f;
            bool haveTracked = false;

            if (robots != null)
            {
                for (int index = 0; index < robots.Count; index++)
                {
                    RobotSample sample = robots[index];
                    string key = string.IsNullOrEmpty(sample.Id) ? _robot : sample.Id;
                    TrackFor(key).Add(worldSeconds, sample.Position.x, sample.Position.z);

                    if (!haveTracked && string.Equals(key, _robot, StringComparison.Ordinal))
                    {
                        trackedPosition = sample.Position;
                        trackedRadius = sample.Radius;
                        haveTracked = true;
                    }
                }

                // A fleet that does not name the tracked robot still has a subject: the first one sampled, so
                // an episode is never left without the robot the scalar metrics belong to.
                if (!haveTracked && robots.Count > 0)
                {
                    trackedPosition = robots[0].Position;
                    trackedRadius = robots[0].Radius;
                    haveTracked = true;
                }
            }

            Vector2 robot = Planar(trackedPosition);

            // An episode without a robot is not an episode: there is nobody to measure a path, a speed or a
            // distance against, and a distance to the origin would be a number a reader could mistake for a
            // real one. The step is dropped rather than recorded wrong.
            if (!haveTracked)
            {
                _previousSampleSeconds = worldSeconds;
                return;
            }

            if (_hasRobotPose)
            {
                double elapsed = worldSeconds - _lastRobotSeconds;
                double distance = Vector2.Distance(Planar(_lastRobotPose), robot);
                _pathLength += distance;

                if (elapsed > 0.0)
                {
                    double speed = distance / elapsed;
                    if (speed > _maxSpeed) _maxSpeed = speed;
                }
            }
            else
            {
                _firstRobotPose = trackedPosition;
                _hasRobotPose = true;
            }

            _lastRobotPose = trackedPosition;
            _lastRobotSeconds = worldSeconds;
            _steps++;

            // The tracked robot's outline is half of every clearance this step computes, so it is kept with
            // the episode: the number recorded beside the distances has to be the number they were measured
            // against, not a default a reader has to guess. A sample that reports no radius leaves the last
            // known one in place instead of erasing it.
            if (trackedRadius > 0f)
                _robotRadius = trackedRadius;

            double nearest = double.PositiveInfinity;
            bool inside = false;

            if (humans != null)
            {
                for (int index = 0; index < humans.Count; index++)
                {
                    HumanSample human = humans[index];
                    Vector2 position = Planar(human.Position);
                    double distance = Vector2.Distance(robot, position);
                    // What a person perceives is the gap left between the two bodies, not the distance
                    // between two points inside them: the outlines of both agents are taken off here, which
                    // is what makes "we passed half a metre apart" mean the half metre somebody can see.
                    double clearance = distance - trackedRadius - human.Radius;

                    if (distance < nearest) nearest = distance;
                    if (clearance < _minClearance)
                    {
                        _minClearance = clearance;
                        _closestHumanRadius = human.Radius;
                    }
                    if (clearance < _personalSpaceRadius) inside = true;

                    if (!_humanTracks.TryGetValue(human.Id, out TrajectoryBuffer track))
                    {
                        track = new TrajectoryBuffer(_trajectoryCapacity);
                        _humanTracks[human.Id] = track;
                    }
                    track.Add(worldSeconds, human.Position.x, human.Position.z);
                }
            }

            if (!double.IsPositiveInfinity(nearest))
            {
                _humanDistanceSum += nearest;
                _humanSamples++;
                if (nearest < _minHumanDistance) _minHumanDistance = nearest;
            }

            if (inside)
            {
                if (!_personalSpaceEntered)
                {
                    _personalSpaceIntrusions++;
                    _personalSpaceEntered = true;
                }

                if (_steps > 1)
                    _personalSpaceSeconds += Math.Max(0.0, worldSeconds - _previousSampleSeconds);
            }
            else
            {
                _personalSpaceEntered = false;
            }

            _previousSampleSeconds = worldSeconds;
        }

        /// <summary>
        /// The buffer one robot's track is kept in, created the first time that robot is sampled and filed in
        /// the order the fleet was first seen, so the document lists the robots the same way every episode.
        /// </summary>
        private TrajectoryBuffer TrackFor(string key)
        {
            if (!_robotTracks.TryGetValue(key, out TrajectoryBuffer track))
            {
                track = new TrajectoryBuffer(_trajectoryCapacity);
                _robotTracks[key] = track;
                _robotOrder.Add(key);
            }

            return track;
        }

        /// <summary>
        /// Closes the episode: the record carries every metric, including the trajectory subsampling factor
        /// the buffers settled on, so a reader can tell a full-resolution path from a decimated one. Every
        /// robot the episode saw is written under its own roster key - the tracked one first - followed by the
        /// crowd; the tracked robot keeps its key even when the fleet never sampled it, because a reader of a
        /// single-robot episode expects the key the document's <c>robot</c> names.
        /// </summary>
        public EpisodeMetrics Finish(string outcome, double worldSeconds, double wallSeconds)
        {
            // What the run earned beats why it stopped: an episode that reached its goal at the twentieth
            // second and was ended by the red button afterwards is a successful run that was cut short, not a
            // run that never got anywhere.
            string decided = _outcome ?? outcome;
            double world = ElapsedSeconds(worldSeconds);
            Vector3 straightTo = _hasGoal ? _goal : _lastRobotPose;
            double straight = _hasRobotPose
                ? Vector2.Distance(Planar(_firstRobotPose), Planar(straightTo))
                : 0.0;

            var trajectories = new Dictionary<string, List<double[]>>();
            var robots = new List<string>(_robotOrder.Count + 1);
            int stride = 1;

            // The tracked robot opens the list and the document, whether the fleet sampled it or not: the
            // scalar metrics name it, and a reader must find the key they describe.
            string trackedKey = MetricsContract.RobotTrackKey(_robot);
            robots.Add(_robot);
            trajectories[trackedKey] = _robotTracks.TryGetValue(_robot, out TrajectoryBuffer trackedTrack)
                ? new List<double[]>(trackedTrack.Points)
                : new List<double[]>();
            if (trackedTrack != null && trackedTrack.Stride > stride) stride = trackedTrack.Stride;

            for (int index = 0; index < _robotOrder.Count; index++)
            {
                string key = _robotOrder[index];
                if (string.Equals(key, _robot, StringComparison.Ordinal))
                    continue;

                TrajectoryBuffer track = _robotTracks[key];
                robots.Add(key);
                trajectories[MetricsContract.RobotTrackKey(key)] = new List<double[]>(track.Points);
                if (track.Stride > stride) stride = track.Stride;
            }

            foreach (KeyValuePair<int, TrajectoryBuffer> entry in _humanTracks)
            {
                trajectories[MetricsContract.HumanTrackKey(entry.Key)] =
                    new List<double[]>(entry.Value.Points);
                if (entry.Value.Stride > stride) stride = entry.Value.Stride;
            }

            return new EpisodeMetrics
            {
                Id = _id,
                Index = _index,
                Session = _session,
                Scenario = _scenario,
                Robot = _robot,
                Robots = robots,
                StartedAt = _startedAt,
                Outcome = decided,
                WorldSeconds = world,
                WallSeconds = Math.Max(0.0, wallSeconds),
                Steps = _steps,
                PathLengthMetres = _pathLength,
                StraightLineMetres = straight,
                AverageSpeedMetresPerSecond = world > 0.0 ? _pathLength / world : 0.0,
                MaxSpeedMetresPerSecond = _maxSpeed,
                MinHumanDistanceMetres = _humanSamples > 0
                    ? _minHumanDistance
                    : EpisodeMetrics.NoHumanDistance,
                AverageHumanDistanceMetres = _humanSamples > 0
                    ? _humanDistanceSum / _humanSamples
                    : EpisodeMetrics.NoHumanDistance,
                MinClearanceMetres = _humanSamples > 0
                    ? _minClearance
                    : EpisodeMetrics.NoHumanDistance,
                RobotRadiusMetres = _robotRadius,
                HumanRadiusMetres = _closestHumanRadius,
                PersonalSpaceIntrusions = _personalSpaceIntrusions,
                PersonalSpaceSeconds = _personalSpaceSeconds,
                PersonalSpaceRadiusMetres = _personalSpaceRadius,
                TrajectoryStride = stride,
                Trajectories = trajectories,
            };
        }

        private static Vector2 Planar(Vector3 position) => new Vector2(position.x, position.z);
    }
}
