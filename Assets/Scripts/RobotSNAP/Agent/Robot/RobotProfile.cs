using System.Collections.Generic;
using UnityEngine;

namespace RobotSNAP.Agents
{
    /// <summary>
    /// What makes one robot different from another: how much room it takes, what it weighs, how fast it
    /// may drive and what its lidar sees.
    ///
    /// A profile is data, not a scene object: a new robot type is a new entry in
    /// <see cref="RobotProfiles"/>, and a body that has to be built by hand is bound to its id by the
    /// roster. The figures are the ones the vendors publish, so a scenario written for a TurtleBot 4
    /// behaves like a TurtleBot 4 - a slow, small robot people barely notice - and not like the default
    /// base that only shares its shape.
    ///
    /// The radius is the one that matters most for social navigation: the crowd's social force model reads
    /// the footprint of the robot, so a Husky is given a wider berth than a TurtleBot, in the crowd as
    /// well as in the planner and in the spawn check.
    /// </summary>
    public sealed class RobotProfile
    {
        /// <summary>Stable identifier written in the scenario, lower case, no spaces.</summary>
        public string Id { get; }

        /// <summary>Name a person reads, and the label the interface shows.</summary>
        public string DisplayName { get; }

        /// <summary>One line on what this robot is, for the tooltip of a type list.</summary>
        public string Description { get; }

        /// <summary>Footprint radius in metres, at ground level.</summary>
        public float Radius { get; }

        /// <summary>Mass of the base in kilograms.</summary>
        public float Mass { get; }

        /// <summary>
        /// Scale applied to the body of the default base, so a small robot and a large one are not the same
        /// picture. One keeps the body exactly as the prefab was authored.
        /// </summary>
        public float BodyScale { get; }

        /// <summary>Highest linear speed the base may be commanded, in m/s.</summary>
        public float MaxLinearSpeed { get; }

        /// <summary>Highest angular speed the base may be commanded, in rad/s.</summary>
        public float MaxAngularSpeed { get; }

        /// <summary>Field of view of the lidar, in degrees. 360 is a full circle, 270 the usual three-quarter.</summary>
        public float LidarSpanDegrees { get; }

        /// <summary>Range of the lidar, in metres.</summary>
        public float LidarRange { get; }

        /// <summary>Number of rays of one scan. It is what the scan costs, so a wide sensor gets few of them.</summary>
        public int LidarRays { get; }

        /// <summary>Height of the laser plane above the ground, in metres.</summary>
        public float LidarHeight { get; }

        /// <summary>Publishing rate of the scan, in Hz.</summary>
        public float LidarFrequencyHz { get; }

        /// <summary>
        /// Colour of the body, so two robots of a scenario are told apart at a glance. Clear leaves the
        /// materials of the prefab untouched, which is what the legacy default asks for.
        /// </summary>
        public Color BodyColor { get; }

        public RobotProfile(
            string id,
            string displayName,
            string description,
            float radius,
            float mass,
            float bodyScale,
            float maxLinearSpeed,
            float maxAngularSpeed,
            float lidarSpanDegrees,
            float lidarRange,
            int lidarRays,
            float lidarHeight,
            float lidarFrequencyHz,
            Color bodyColor)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            Radius = radius;
            Mass = mass;
            BodyScale = bodyScale <= 0f ? 1f : bodyScale;
            MaxLinearSpeed = maxLinearSpeed;
            MaxAngularSpeed = maxAngularSpeed;
            LidarSpanDegrees = lidarSpanDegrees;
            LidarRange = lidarRange;
            LidarRays = Mathf.Max(2, lidarRays);
            LidarHeight = Mathf.Max(0.05f, lidarHeight);
            LidarFrequencyHz = Mathf.Max(1f, lidarFrequencyHz);
            BodyColor = bodyColor;
        }

        /// <summary>True when the lidar sees all around, which is drawn as a ring rather than a cone.</summary>
        public bool HasFullCircleLidar => Mathf.Abs(LidarSpanDegrees - 360f) < 1f;

        /// <summary>
        /// First and last angle of the sweep, in radians, in the frame of the sensor.
        ///
        /// A full circle keeps the convention the project already shipped with - zero to two pi, growing
        /// counter-clockwise - while a partial sensor is centred on the robot's forward axis, which is what
        /// every 270 degrees lidar does.
        /// </summary>
        public float LidarAngleMin => HasFullCircleLidar ? 0f : -LidarSpanDegrees * 0.5f * Mathf.Deg2Rad;

        /// <summary>Last angle of the sweep, in radians, in the frame of the sensor.</summary>
        public float LidarAngleMax => HasFullCircleLidar ? 2f * Mathf.PI : LidarSpanDegrees * 0.5f * Mathf.Deg2Rad;

        public override string ToString() => $"{DisplayName} ({Id})";
    }

    /// <summary>
    /// The robot types this build knows, and the one place a scenario's type name is turned into a profile.
    ///
    /// The names are the ids written in a scenario file. Lookup is tolerant on purpose: a scenario written
    /// by hand says <c>TurtleBot4</c>, <c>turtlebot4</c> or <c>TurtleBot 4</c> and means the same robot, and
    /// a type nobody knows falls back to the default instead of leaving a scenario unplayable.
    /// </summary>
    public static class RobotProfiles
    {
        /// <summary>
        /// The type a scenario gets when it names none. It is the base the application shipped with, so a
        /// scenario written before several types existed keeps the robot it was authored against.
        /// </summary>
        public const string DefaultId = "freight";

        private static readonly RobotProfile[] BuiltIn =
        {
            // The base that was already in the project: a 70 kg differential platform driving at 1 m/s with a
            // full-circle lidar. Its numbers are the ones the prefab carries, so this entry changes nothing.
            new RobotProfile(
                DefaultId, "Freight", "The default 70 kg base, 1.0 m/s, full-circle lidar",
                radius: 0.25f, mass: 70f, bodyScale: 1f,
                maxLinearSpeed: 1.0f, maxAngularSpeed: 2.0f,
                lidarSpanDegrees: 360f, lidarRange: 3.5f, lidarRays: 180,
                lidarHeight: 0.79f, lidarFrequencyHz: 20f,
                bodyColor: Color.clear),

            // Small, slow, and seen everywhere in the social navigation literature: people walk around it
            // without changing their path, which is exactly why it is the usual reference platform.
            new RobotProfile(
                "turtlebot4", "TurtleBot 4", "Small and slow (0.31 m/s), 360 degrees lidar at 12 m",
                radius: 0.17f, mass: 15f, bodyScale: 0.68f,
                maxLinearSpeed: 0.31f, maxAngularSpeed: 1.9f,
                lidarSpanDegrees: 360f, lidarRange: 12f, lidarRays: 360,
                lidarHeight: 0.32f, lidarFrequencyHz: 10f,
                bodyColor: new Color(0.55f, 0.75f, 0.95f)),

            // The opposite trade: same small footprint, but three times faster than the default and able to
            // turn on the spot. It is the robot that forces the crowd to react.
            new RobotProfile(
                "jackal", "Jackal", "Small but fast (2.0 m/s), 270 degrees lidar at 10 m",
                radius: 0.27f, mass: 17f, bodyScale: 1f,
                maxLinearSpeed: 2.0f, maxAngularSpeed: 4.0f,
                lidarSpanDegrees: 270f, lidarRange: 10f, lidarRays: 270,
                lidarHeight: 0.33f, lidarFrequencyHz: 20f,
                bodyColor: new Color(0.20f, 0.55f, 0.30f)),

            // A corridor blocker: twice the footprint of the default, half its speed, and a long range
            // sensor. Behaviour that a small robot gets away with has to be planned around this one.
            new RobotProfile(
                "husky", "Husky A200", "Large and heavy (50 kg, 0.99 m), 1.0 m/s, 20 m lidar",
                radius: 0.49f, mass: 50f, bodyScale: 1.85f,
                maxLinearSpeed: 1.0f, maxAngularSpeed: 1.5f,
                lidarSpanDegrees: 270f, lidarRange: 20f, lidarRays: 270,
                lidarHeight: 0.99f, lidarFrequencyHz: 20f,
                bodyColor: new Color(0.90f, 0.45f, 0.12f)),

            // The perception trap: a narrow 180 degrees sweep that only sees 5 m ahead, so a policy that
            // works on a full-circle robot has to cope with a blind side and a late detection.
            new RobotProfile(
                "pioneer_p3dx", "Pioneer 3-DX", "Light (9 kg), 1.2 m/s, but only 180 degrees at 5 m",
                radius: 0.22f, mass: 9f, bodyScale: 0.85f,
                maxLinearSpeed: 1.2f, maxAngularSpeed: 2.0f,
                lidarSpanDegrees: 180f, lidarRange: 5f, lidarRays: 180,
                lidarHeight: 0.38f, lidarFrequencyHz: 10f,
                bodyColor: new Color(0.55f, 0.35f, 0.80f)),
        };

        /// <summary>Every built-in profile, in the order the interface lists them.</summary>
        public static IReadOnlyList<RobotProfile> All => BuiltIn;

        /// <summary>The profile of the default base, never null.</summary>
        public static RobotProfile Default => BuiltIn[0];

        /// <summary>
        /// The profile a scenario asked for, tolerating the spellings a hand-written file may use, and
        /// falling back to <see cref="Default"/> for a type this build does not know.
        /// </summary>
        public static RobotProfile Find(string idOrName)
        {
            return TryFind(idOrName, out RobotProfile profile) ? profile : Default;
        }

        /// <summary>
        /// Looks a profile up by id, display name, or display name without its spaces and punctuation.
        /// Returns false rather than a fallback, so a caller can tell an unknown type from the default one.
        /// </summary>
        public static bool TryFind(string idOrName, out RobotProfile profile)
        {
            profile = null;
            string wanted = Canonical(idOrName);
            if (wanted.Length == 0)
                return false;

            foreach (RobotProfile candidate in BuiltIn)
            {
                if (Canonical(candidate.Id) == wanted || Canonical(candidate.DisplayName) == wanted)
                {
                    profile = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>The ids of every type, in display order.</summary>
        public static string[] Ids()
        {
            var ids = new string[BuiltIn.Length];
            for (int index = 0; index < BuiltIn.Length; index++)
                ids[index] = BuiltIn[index].Id;
            return ids;
        }

        /// <summary>The names of every type, in display order.</summary>
        public static string[] DisplayNames()
        {
            var names = new string[BuiltIn.Length];
            for (int index = 0; index < BuiltIn.Length; index++)
                names[index] = BuiltIn[index].DisplayName;
            return names;
        }

        /// <summary>
        /// Compares type names the way a person writes them: case is ignored, and so are spaces, dashes,
        /// underscores and dots, so "TurtleBot 4", "turtlebot4" and "turtlebot-4" are one type.
        /// </summary>
        private static string Canonical(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var characters = new List<char>(value.Length);
            foreach (char character in value)
            {
                if (char.IsLetterOrDigit(character))
                    characters.Add(char.ToLowerInvariant(character));
            }

            return new string(characters.ToArray());
        }
    }
}
