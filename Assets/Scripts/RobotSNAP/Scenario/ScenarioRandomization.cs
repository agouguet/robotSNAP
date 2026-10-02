using System.Collections.Generic;
using UnityEngine;

namespace RobotSNAP.Core.Scenario
{
    /// <summary>
    /// Turns an authored scenario into the one a run actually plays, by drawing every value whose
    /// <c>*_range</c> sibling asks for it.
    ///
    /// A scenario is authored once and run many times, and a study wants the runs to differ: a crossing that
    /// carries two walkers once and five the next, a robot that faces one heading one run and another the
    /// next. The authored document is therefore never mutated - it is the one the editor and the interface
    /// hold, and the loader caches it between runs - so a run works on its own copy, in which every range has
    /// already been replaced by the value that was drawn for it. What the simulation applies is then a plain
    /// scenario, and an episode's agents can never be re-drawn halfway through because something re-read a
    /// range.
    ///
    /// <para><b>The seed</b> - this runs right after <see cref="SimulationRuntimeSettings.Apply"/>, which
    /// seeds <c>UnityEngine.Random</c> for the whole run, so a session that fixed a seed replays its draws
    /// exactly. Every draw happens here, in scenario order, before anything is activated: the sequence a run
    /// produces does not depend on when an agent happens to enter. A scenario with no range anywhere draws
    /// nothing at all, so a file written before ranges existed consumes the very same random sequence it
    /// always did.</para>
    /// </summary>
    public static class ScenarioRandomization
    {
        /// <summary>
        /// The scenario this run plays: a copy of <paramref name="authored"/> with every range drawn.
        ///
        /// <paramref name="notes"/> receives one line per value that was drawn, so the caller can say what an
        /// episode got - the drawn count of a route is not visible anywhere else once the agents are spawned.
        /// </summary>
        public static ScenarioData Resolve(ScenarioData authored, out List<string> notes)
        {
            notes = new List<string>();
            if (authored == null)
                return null;

            var resolved = new ScenarioData
            {
                Info = authored.Info,
                Points = authored.Points != null
                    ? new Dictionary<string, RefPoint>(authored.Points)
                    : new Dictionary<string, RefPoint>(),
                // The single-robot section is a way of reading an old file; the resolved copy carries the list
                // every reader of the scenario already normalizes to, so the run sees one shape of document.
                Robot = null,
                Robots = new List<RobotScenarioConfig>(),
                Humans = new List<HumanScenarioConfig>()
            };

            foreach (RobotScenarioConfig robot in authored.NormalizedRobots())
            {
                if (robot == null)
                    continue;

                var copy = new RobotScenarioConfig
                {
                    Id = robot.Id,
                    Type = robot.Type,
                    StartRef = robot.StartRef,
                    GoalRef = robot.GoalRef,
                    WaypointRefs = robot.WaypointRefs,
                    Behavior = robot.Behavior,
                    Speed = DrawPositive(robot.SpeedRange, robot.Speed, 0.01f)
                };

                if (TryApplyYawDraw(robot, resolved.Points, out float yaw))
                    notes.Add($"{robot.Id} start yaw {yaw:0.#} deg (from {robot.StartYawRange})");

                if (robot.SpeedRange != null && robot.SpeedRange.IsUsable)
                    notes.Add($"{robot.Id} speed {copy.Speed:0.##} m/s (from {robot.SpeedRange})");

                resolved.Robots.Add(copy);
            }

            if (authored.Humans != null)
            {
                foreach (HumanScenarioConfig human in authored.Humans)
                {
                    if (human == null)
                        continue;

                    resolved.Humans.Add(CloneHuman(human, notes));
                }
            }

            return resolved;
        }

        /// <summary>Copy of one route with its ranged values drawn, and the ranges left behind.</summary>
        private static HumanScenarioConfig CloneHuman(HumanScenarioConfig human, List<string> notes)
        {
            var copy = new HumanScenarioConfig
            {
                Id = human.Id,
                Count = DrawCount(human.CountRange, human.Count),
                Spawn = CloneSpawn(human.Spawn),
                Goal = human.Goal,
                Goals = human.Goals,
                EndBehavior = human.EndBehavior,
                Group = human.Group,
                MovementController = human.MovementController,
                Behavior = human.Behavior,
                Speed = DrawPositive(human.SpeedRange, human.Speed, 0.01f),
                SpawnWindow = DrawSpawnWindow(human),
                Color = human.Color,
                Personality = human.Personality
            };

            if (human.CountRange != null && human.CountRange.IsUsable)
                notes.Add($"{human.Id} count {copy.Count} (from {human.CountRange})");
            if (human.SpeedRange != null && human.SpeedRange.IsUsable)
                notes.Add($"{human.Id} speed {copy.Speed:0.##} m/s (from {human.SpeedRange})");
            if (human.SpawnWindowRange != null && human.SpawnWindowRange.IsUsable)
                notes.Add($"{human.Id} departure window {copy.SpawnWindowSeconds:0.##} s (from {human.SpawnWindowRange})");
            if (human.Spawn != null && human.Spawn.SpacingRange != null && human.Spawn.SpacingRange.IsUsable)
                notes.Add($"{human.Id} spacing {copy.Spawn.Spacing:0.##} m (from {human.Spawn.SpacingRange})");

            return copy;
        }

        /// <summary>
        /// Departure window of a resolved route. A range materializes a value the run then owns; without one
        /// the authored value is carried through as it stands, so a document that never wrote
        /// <c>spawn_window</c> still says nothing and a document that wrote zero still asks for a simultaneous
        /// entry. Resolving a run must not turn one into the other.
        /// </summary>
        private static float? DrawSpawnWindow(HumanScenarioConfig human)
        {
            if (human.SpawnWindowRange == null || !human.SpawnWindowRange.IsUsable)
                return human.SpawnWindow;

            return Mathf.Max(0f, Draw(human.SpawnWindowRange, human.SpawnWindow ?? 0f));
        }

        private static SpawnConfig CloneSpawn(SpawnConfig spawn)
        {
            if (spawn == null)
                return null;

            float drawn = Draw(spawn.SpacingRange, spawn.Spacing);
            return new SpawnConfig
            {
                Type = spawn.Type,
                Reference = spawn.Reference,
                Position = spawn.Position,
                Zone = spawn.Zone,
                Formation = spawn.Formation,
                FormationParameter = spawn.FormationParameter,
                // The runtime clamps spacing to what a formation can hold; drawing the legal value here means
                // the number quoted back to the author is the number the agents were actually laid out with.
                Spacing = SpawnPlanner.ClampSpacing(drawn, spawn.Formation),
                RelativeTo = spawn.RelativeTo
            };
        }

        /// <summary>
        /// Draws the spawn heading of one robot, writing it onto the start point of the resolved scenario so
        /// the code that places a robot reads the drawn yaw with the very lookup it always used.
        /// </summary>
        private static bool TryApplyYawDraw(
            RobotScenarioConfig robot,
            Dictionary<string, RefPoint> points,
            out float yaw)
        {
            yaw = 0f;
            ScenarioRange range = robot.StartYawRange;
            if (range == null || !range.IsUsable)
                return false;
            if (string.IsNullOrEmpty(robot.StartRef) || points == null ||
                !points.TryGetValue(robot.StartRef, out RefPoint start) || start == null)
                return false;

            yaw = Random.Range(range.Min, range.Max);
            points[robot.StartRef] = new RefPoint
            {
                X = start.X,
                Y = start.Y,
                Z = start.Z,
                Center = start.Center,
                Size = start.Size,
                Yaw = yaw
            };
            return true;
        }

        /// <summary>
        /// The value a run uses: the draw when a usable range was authored, the scalar otherwise. No range
        /// means no draw, so a scenario that randomizes nothing keeps the random sequence it always had.
        /// </summary>
        public static float Draw(ScenarioRange range, float authored) =>
            range != null && range.IsUsable ? Random.Range(range.Min, range.Max) : authored;

        /// <summary>Draw for a value that has to stay above zero, such as a speed.</summary>
        public static float DrawPositive(ScenarioRange range, float authored, float floor) =>
            Mathf.Max(floor, Draw(range, authored));

        /// <summary>
        /// Draw for a count: both bounds are included, because an author writing "2 to 6 agents" means six is
        /// one of the crowds they want to see. A negative draw is floored at zero.
        /// </summary>
        public static int DrawCount(ScenarioRange range, int authored)
        {
            if (range == null || !range.IsUsable)
                return authored;

            int low = Mathf.Max(0, Mathf.RoundToInt(Mathf.Min(range.Min, range.Max)));
            int high = Mathf.Max(0, Mathf.RoundToInt(Mathf.Max(range.Min, range.Max)));
            return high > low ? Random.Range(low, high + 1) : low;
        }
    }
}
