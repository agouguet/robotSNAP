using System.Collections.Generic;
using UnityEngine;

namespace RobotSNAP.Agents
{
    /// <summary>
    /// What makes one robot different from another: how much room it takes, what it weighs, how fast it
    /// may drive and what its lidar sees.
    ///
    /// A profile is data, not a scene object: a new robot type is a new entry in
    /// <see cref="RobotProfiles"/>, and the body it drives is the prefab the project imported for that
    /// type. The figures of a type are the ones its own prefab carries - the masses and the wheel
    /// geometry come from the URDF the model was imported from, not from a vendor sheet - so a scenario
    /// that asks for a Jackal drives the Jackal the project actually ships.
    ///
    /// The radius is the one that matters most for social navigation: the crowd's social force model
    /// reads the footprint of the robot, so a Kuri is given less room than a Freight in the crowd as
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

        /// <summary>
        /// Mass in kilograms. It is the mass the base of the robot is driven with, so the number is the
        /// total the model's own links add up to and not the weight of a bare chassis.
        /// </summary>
        public float Mass { get; }

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

        public RobotProfile(
            string id,
            string displayName,
            string description,
            float radius,
            float mass,
            float maxLinearSpeed,
            float maxAngularSpeed,
            float lidarSpanDegrees,
            float lidarRange,
            int lidarRays,
            float lidarHeight,
            float lidarFrequencyHz)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            Radius = radius;
            Mass = mass;
            MaxLinearSpeed = maxLinearSpeed;
            MaxAngularSpeed = maxAngularSpeed;
            LidarSpanDegrees = lidarSpanDegrees;
            LidarRange = lidarRange;
            LidarRays = Mathf.Max(2, lidarRays);
            LidarHeight = Mathf.Max(0.05f, lidarHeight);
            LidarFrequencyHz = Mathf.Max(1f, lidarFrequencyHz);
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
    /// The types are the robots the project imported under <c>Assets/Prefabs/Robots</c>: the base the
    /// application shipped with, plus the models the URDF importer brought in. The prefab of each type is
    /// bound to its id by the catalogue the editor tool writes, so a type and the robot that drives it are
    /// the same thing - there is no second, purely visual body to keep in step.
    ///
    /// The names are the ids written in a scenario file. Lookup is tolerant on purpose: a scenario written
    /// by hand says <c>Jackal</c>, <c>jackal</c> or <c>Jack-al</c> and means the same robot, and a type
    /// nobody knows - <c>husky</c> or <c>turtlebot4</c> from a scenario written before the real models were
    /// imported - falls back to the default instead of leaving a scenario unplayable. Such a scenario then
    /// drives the base it always drove, because that is what it was authored against.
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
                DefaultId, "Freight", "The default 70 kg base, 1.0 m/s, full-circle lidar at 3.5 m",
                radius: 0.25f, mass: 70f,
                maxLinearSpeed: 1.0f, maxAngularSpeed: 2.0f,
                lidarSpanDegrees: 360f, lidarRange: 3.5f, lidarRays: 180,
                lidarHeight: 0.79f, lidarFrequencyHz: 20f),

            // The small indoor carrier the SWD starter kit models: a 27 kg differential base on two 6 cm
            // wheels half a metre apart, with its lidar 20 cm above the floor. It is the smallest footprint
            // of the wheeled fleet and the one a pedestrian barely has to step around.
            new RobotProfile(
                "bibus", "Bibus", "Small indoor carrier (29 kg, 0.55 m wide), 1.0 m/s, lidar at 20 cm",
                radius: 0.28f, mass: 29.1f,
                maxLinearSpeed: 1.0f, maxAngularSpeed: 2.0f,
                lidarSpanDegrees: 360f, lidarRange: 12f, lidarRays: 360,
                lidarHeight: 0.20f, lidarFrequencyHz: 10f),

            // Small and fast, on four wheels: the footprint of the default base, twice its speed, and able
            // to turn on the spot. It is the robot that forces the crowd to react.
            //
            // It is also the one robot of this fleet that is driven through its own tyres rather than by a
            // base told where to go, and the only one whose mass is the one its own model declares: 16.523
            // kg of chassis, four 0.477 kg wheels, and the accessory links the importer gave a kilogram
            // each put back to nothing. It brakes and accelerates at the rate a motor-limited platform can
            // command - 1.5 m/s^2 and 2.5 rad/s^2 - instead of at the rate its tyres would allow, which
            // are the only figures here the URDF does not carry: the model publishes no effort limit for
            // its wheel joints.
            new RobotProfile(
                "jackal", "Jackal",
                "Small fast 4-wheel robot on its own tyres (18.4 kg), 2.0 m/s, 270 degrees lidar",
                radius: 0.26f, mass: 16.523f,
                maxLinearSpeed: 2.0f, maxAngularSpeed: 4.0f,
                lidarSpanDegrees: 270f, lidarRange: 10f, lidarRays: 270,
                lidarHeight: 0.30f, lidarFrequencyHz: 20f),

            // The opposite trade: a light, short, narrow domestic robot. Its 16 cm footprint and its 7 kg
            // are what make it the one the crowd walks over rather than around.
            new RobotProfile(
                "kuri", "Kuri", "Light domestic robot (7 kg, 0.31 m wide), 1.0 m/s, lidar at 55 cm",
                radius: 0.16f, mass: 7f,
                maxLinearSpeed: 1.0f, maxAngularSpeed: 2.0f,
                lidarSpanDegrees: 360f, lidarRange: 8f, lidarRays: 360,
                lidarHeight: 0.55f, lidarFrequencyHz: 10f),

            // A legged humanoid, 1.6 m tall: it has no wheel at all, so it does not roll. It is the
            // pedestrian-scale case - a robot that moves among people at their own height.
            new RobotProfile(
                "ginger", "Ginger", "Legged humanoid (96 kg, 1.6 m tall), 1.2 m/s, no wheels",
                radius: 0.28f, mass: 96f,
                maxLinearSpeed: 1.2f, maxAngularSpeed: 2.0f,
                lidarSpanDegrees: 360f, lidarRange: 10f, lidarRays: 360,
                lidarHeight: 1.45f, lidarFrequencyHz: 10f),
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
        /// underscores and dots, so "Jackal", "jackal" and "jack-al" are one type.
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
