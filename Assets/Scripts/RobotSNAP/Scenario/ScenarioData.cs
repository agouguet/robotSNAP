// Scripts/RobotSNAP/Core/Scenario/ScenarioData.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using VYaml.Annotations;

namespace RobotSNAP.Core.Scenario
{
    /// <summary>
    /// Informations de base d'un scénario (pour affichage sans tout charger)
    /// </summary>
    [YamlObject]
    public partial class ScenarioInfo
    {
        [YamlMember("name")]
        public string Name { get; set; }

        [YamlMember("type")]
        public string Type { get; set; } = "";
        
        [YamlMember("description")]
        public string Description { get; set; }
        
        [YamlMember("version")]
        public string Version { get; set; }
        
        [YamlMember("author")]
        public string Author { get; set; }
        
        [YamlMember("created")]
        public string Created { get; set; }
        
        [YamlMember("tags")]
        public string[] Tags { get; set; }
        
        [YamlMember("map")]
        public string MapImage { get; set; }

        [YamlMember("location")]
        public string Location { get; set; } = "";
        
        [YamlMember("dataset")]
        public string DatasetPath { get; set; }
        
        [YamlMember("preview")]
        public string PreviewImage { get; set; }

        [YamlMember("robot_type")]
        public string RobotType { get; set; } = "TurtleBot4";



        /// <summary>
        /// Durée maximale du scénario (en secondes). 0 = illimitée.
        /// </summary>
        [YamlMember("duration")]
        public float Duration { get; set; } = 0f;
        
        /// <summary>
        /// Retourne une représentation lisible du scénario (non sérialisé)
        /// </summary>
        [YamlIgnore]
        public string DisplayText => $"{Name} v{Version} - {(Description?.Length > 50 ? Description.Substring(0, 47) + "..." : Description)}";
        
        /// <summary>
        /// Vérifie si le scénario a un tag spécifique
        /// </summary>
        public bool HasTag(string tag)
        {
            if (Tags == null) return false;
            return Array.Exists(Tags, t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
        }
        
        public override string ToString() => DisplayText;
    }

    /// <summary>
    /// Représente un point 3D avec des floats (utilisé pour la sérialisation YAML)
    /// </summary>
    [YamlObject]
    public partial class Point
    {
        [YamlMember("x")] public float X { get; set; }
        [YamlMember("y")] public float Y { get; set; }
        [YamlMember("z")] public float Z { get; set; }
        
        public Vector3 ToVector3() => new Vector3(X, Y, Z);
        
        public static Point FromVector3(Vector3 v) => new Point { X = v.x, Y = v.y, Z = v.z };
    }

    /// <summary>
    /// Point de référence - position ou zone
    /// </summary>
    [YamlObject]
    public partial class RefPoint
    {
        [YamlMember("x")]
        public float? X { get; set; }
        
        [YamlMember("y")]
        public float? Y { get; set; }
        
        [YamlMember("z")]
        public float? Z { get; set; }

        [YamlMember("yaw")]
        public float? Yaw { get; set; }
        
        [YamlMember("center")]
        public Point Center { get; set; }
        
        [YamlMember("size")]
        public Point Size { get; set; }
        
        /// <summary>
        /// Est-ce un point simple ?
        /// </summary>
        [YamlIgnore]
        public bool IsPoint => X.HasValue && Y.HasValue && Z.HasValue;
        
        /// <summary>
        /// Est-ce une zone (bounds) ?
        /// </summary>
        [YamlIgnore]
        public bool IsBounds => Center != null && Size != null;

        /// <summary>
        /// Obtient la rotation (quaternion) à partir du yaw.
        /// Si null, retourne Quaternion.identity.
        /// </summary>
        [YamlIgnore]
        public Quaternion Rotation => Yaw.HasValue ? Quaternion.Euler(0, Yaw.Value, 0) : Quaternion.identity;
        
        /// <summary>
        /// Convertit en Vector3 (pour les points)
        /// </summary>
        public Vector3 ToVector3()
        {
            if (IsPoint) return new Vector3(X.Value, Y.Value, Z.Value);
            if (IsBounds) return Center.ToVector3();
            return Vector3.zero;
        }
        
        /// <summary>
        /// Convertit en Bounds (pour les zones)
        /// </summary>
        public Bounds ToBounds()
        {
            if (IsBounds) return new Bounds(Center.ToVector3(), Size.ToVector3());
            return new Bounds(ToVector3(), Vector3.zero);
        }
        
        /// <summary>
        /// Crée un point depuis un Vector3
        /// </summary>
        public static RefPoint FromVector3(Vector3 position)
        {
            return new RefPoint
            {
                X = position.x,
                Y = position.y,
                Z = position.z
            };
        }
        
        /// <summary>
        /// Crée une zone depuis un centre et une taille
        /// </summary>
        public static RefPoint FromBounds(Vector3 center, Vector3 size)
        {
            return new RefPoint
            {
                Center = Point.FromVector3(center),
                Size = Point.FromVector3(size)
            };
        }
    }

    /// <summary>
    /// One robot of a scenario: which type drives, where it starts, and the route it follows.
    ///
    /// A scenario used to hold exactly one of these, in the <c>robot</c> section. It now holds a list, in
    /// <c>robots</c>, and the single section is read as a list of one so a scenario written before
    /// multi-robot keeps working untouched.
    /// </summary>
    [YamlObject]
    public partial class RobotScenarioConfig
    {
        /// <summary>
        /// Id of the robot inside the scenario, such as <c>robot_1</c>. It is what a client names on the
        /// command topic and what the ROS streams of the robot are namespaced with.
        /// </summary>
        [YamlMember("id")]
        public string Id { get; set; }

        /// <summary>
        /// Type of the robot, as an id of <see cref="RobotSNAP.Agents.RobotProfiles"/>: <c>turtlebot4</c>,
        /// <c>jackal</c>, <c>husky</c>, <c>pioneer_p3dx</c>, or <c>freight</c> for the default base.
        ///
        /// The label the scenario shows in its details (<c>scenario_info.robot_type</c>) is not read here:
        /// a scenario written before the types existed carries a label that no longer matches the base it
        /// was authored against, and reading it would change how an old scenario drives.
        /// </summary>
        [YamlMember("type")]
        public string Type { get; set; }

        [YamlMember("start")]
        public string StartRef { get; set; }
        
        [YamlMember("goal")]
        public string GoalRef { get; set; }

        /// <summary>
        /// Optional ordered points visited between start and the final goal.
        /// Kept separate from goal for backward compatibility with existing scenarios.
        /// </summary>
        [YamlMember("waypoints")]
        public List<string> WaypointRefs { get; set; }
        
        [YamlMember("behavior")]
        public string Behavior { get; set; } = "normal";
        
        [YamlMember("speed")]
        public float Speed { get; set; } = 1.2f;
    }

    /// <summary>
    /// Configuration d'un humain
    /// </summary>
    [YamlObject]
    public partial class HumanScenarioConfig
    {
        [YamlMember("id")]
        public string Id { get; set; }
        
        [YamlMember("count")]
        public int Count { get; set; } = 1;
        
        [YamlMember("spawn")]
        public SpawnConfig Spawn { get; set; }
        
        [YamlMember("goal")]
        public GoalConfig Goal { get; set; }

        /// <summary>
        /// Optional ordered goals visited after the legacy primary goal.
        /// </summary>
        [YamlMember("goals")]
        public List<GoalConfig> Goals { get; set; }

        /// <summary>
        /// What the agents do once the last point of their route is reached:
        /// "stay" (default), "disappear" or "loop".
        /// </summary>
        [YamlMember("end_behavior")]
        public string EndBehavior { get; set; } = "stay";

        /// <summary>
        /// Optional group id. Every entry sharing the same group id walks together in formation.
        /// </summary>
        [YamlMember("group")]
        public string Group { get; set; }

        [YamlMember("movement_controller")]
        public MovementControllerConfig MovementController { get; set; } 
        
        [YamlMember("behavior")]
        public string Behavior { get; set; } = "normal";
        
        [YamlMember("speed")]
        public float Speed { get; set; } = 1.0f;
        
        [YamlMember("color")]
        public float[] Color { get; set; }
        
        [YamlMember("personality")]
        public PersonalityConfig Personality { get; set; }
    }

    /// <summary>
    /// Configuration de spawn
    /// </summary>
    [YamlObject]
    public partial class SpawnConfig
    {
        [YamlMember("type")]
        public string Type { get; set; } = "point";
        
        [YamlMember("ref")]
        public string Reference { get; set; }

        [YamlMember("position")]
        public Point Position { get; set; }

        [YamlMember("zone")]
        public RefPoint Zone { get; set; } 
        
        [YamlMember("formation")]
        public string Formation { get; set; }

        /// <summary>
        /// The single value a formation can tune: a wedge opening (degrees), a row stagger (m),
        /// a cluster radius (m) or a pair front spacing (m). Zero keeps the formation default.
        /// </summary>
        [YamlMember("formation_parameter")]
        public float FormationParameter { get; set; }
        
        [YamlMember("spacing")]
        public float Spacing { get; set; } = 1.5f;
        
        [YamlMember("relative_to")]
        public string RelativeTo { get; set; }
    }

    /// <summary>
    /// Configuration d'objectif
    /// </summary>
    [YamlObject]
    public partial class GoalConfig
    {
        [YamlMember("type")]
        public string Type { get; set; } = "point";
        
        [YamlMember("ref")]
        public string Reference { get; set; }

        [YamlMember("position")]
        public Point Position { get; set; }
        
        [YamlMember("zone")]
        public RefPoint Zone { get; set; }
        
        [YamlMember("target")]
        public string Target { get; set; }
        
        [YamlMember("radius")]
        public float Radius { get; set; } = 5f;
    }

    /// <summary>
    /// Configuration de personnalité (pour SFM)
    /// </summary>
    [YamlObject]
    public partial class PersonalityConfig
    {
        [YamlMember("assertiveness")]
        public float Assertiveness { get; set; } = 0.5f;
        
        [YamlMember("personal_space")]
        public float PersonalSpace { get; set; } = 0.8f;
        
        [YamlMember("reaction_time")]
        public float ReactionTime { get; set; } = 0.3f;
    }

    /// <summary>
    /// Configuration du contrôleur de mouvement (SFM, External)
    /// </summary>
    [YamlObject]
    public partial class MovementControllerConfig
    {
        [YamlMember("type")]
        public string Type { get; set; } = "SFM";

        [YamlMember("sfm_params")]
        public SFMParams SFMParameters { get; set; }
    }

    /// <summary>
    /// Paramètres SFM pour le scénario
    /// </summary>
    [YamlObject]
    public partial class SFMParams
    {
        [YamlMember("force_strength")]
        public float ForceStrength { get; set; } = 10f;
        
        [YamlMember("social_force")]
        public float SocialForce { get; set; } = 5f;
        
        [YamlMember("obstacle_force")]
        public float ObstacleForce { get; set; } = 8f;
        
        [YamlMember("goal_force")]
        public float GoalForce { get; set; } = 12f;
        
        [YamlMember("relaxation_time")]
        public float RelaxationTime { get; set; } = 0.5f;
        
        [YamlMember("interaction_radius")]
        public float InteractionRadius { get; set; } = 2f;
    }

    /// <summary>
    /// Scénario complet
    /// </summary>
    [YamlObject]
    public partial class ScenarioData
    {
        [YamlMember("scenario_info")]
        public ScenarioInfo Info { get; set; }
        
        [YamlMember("points")]
        public Dictionary<string, RefPoint> Points { get; set; }
        
        /// <summary>
        /// The robots of the scenario, in the order the interface lists them. The first one is the robot a
        /// client reaches without naming anybody, so the order is what an old single-robot client sees.
        /// </summary>
        [YamlMember("robots")]
        public List<RobotScenarioConfig> Robots { get; set; }

        /// <summary>
        /// The single robot of a scenario written before several were possible. Read as the first entry of
        /// <see cref="Robots"/>, and written back as that list once the scenario is saved again.
        /// </summary>
        [YamlMember("robot")]
        public RobotScenarioConfig Robot { get; set; }
        
        [YamlMember("humans")]
        public List<HumanScenarioConfig> Humans { get; set; }
        
        // ========== PROPRIÉTÉS DE CONFORT (non sérialisées) ==========
        
        /// <summary>
        /// Nom du scénario (délégué à Info)
        /// </summary>
        [YamlIgnore]
        public string Name => Info?.Name ?? "Unknown";
        
        /// <summary>
        /// Description du scénario
        /// </summary>
        [YamlIgnore]
        public string Description => Info?.Description ?? "";
        
        /// <summary>
        /// Version du scénario
        /// </summary>
        [YamlIgnore]
        public string Version => Info?.Version ?? "1.0";
        
        /// <summary>
        /// Image de map associée
        /// </summary>
        [YamlIgnore]
        public string MapImage => Info?.MapImage ?? "";
        
        /// <summary>
        /// Chemin du dataset
        /// </summary>
        [YamlIgnore]
        public string DatasetPath => Info?.DatasetPath ?? "";
        
        /// <summary>
        /// Durée maximale du scénario (délégué à Info)
        /// </summary>
        [YamlIgnore]
        public float Duration => Info?.Duration ?? 0f;

        /// <summary>
        /// The robots of the scenario, whichever shape the file was written in, with an id and a type
        /// filled in for every entry.
        ///
        /// A scenario that names a list is used as it is; one that names a single robot becomes a list of
        /// one; and a scenario that names neither comes back empty, which is what a scenario with no robot
        /// of its own (a pure crowd, driven from outside) is.
        /// </summary>
        public List<RobotScenarioConfig> NormalizedRobots()
        {
            var robots = new List<RobotScenarioConfig>();

            if (Robots != null)
            {
                foreach (RobotScenarioConfig robot in Robots)
                {
                    if (robot != null)
                        robots.Add(robot);
                }
            }

            if (robots.Count == 0 && Robot != null)
                robots.Add(Robot);

            for (int index = 0; index < robots.Count; index++)
            {
                RobotScenarioConfig robot = robots[index];
                if (string.IsNullOrWhiteSpace(robot.Id))
                    robot.Id = DefaultRobotId(index);
                if (string.IsNullOrWhiteSpace(robot.Type))
                    robot.Type = RobotSNAP.Agents.RobotProfiles.DefaultId;
            }

            return robots;
        }

        /// <summary>Id a robot takes when the scenario names none: <c>robot_1</c>, <c>robot_2</c>, ...</summary>
        public static string DefaultRobotId(int index) => $"robot_{index + 1}";

        /// <summary>Number of robots the scenario drives, legacy section included.</summary>
        [YamlIgnore]
        public int RobotCount => NormalizedRobots().Count;
        
        // ========== MÉTHODES (pas d'attribut YamlIgnore nécessaire) ==========
        
        /// <summary>
        /// Valide que le scénario est correctement configuré
        /// </summary>
        public bool IsValid(out string error)
        {
            error = null;
            
            if (Info == null)
            {
                error = "Missing scenario_info section";
                return false;
            }
            
            if (string.IsNullOrEmpty(Info.Name))
            {
                error = "Scenario name is required";
                return false;
            }
            
            if (Robot == null && (Robots == null || Robots.Count == 0))
            {
                error = "Robot configuration is required";
                return false;
            }
            
            // Every robot of the scenario needs a start and something to reach, whatever its rank. A single
            // robot keeps the message it always had, so a client that reads it still recognises the case.
            List<RobotScenarioConfig> robots = NormalizedRobots();
            foreach (RobotScenarioConfig robot in robots)
            {
                string subject = robots.Count == 1 ? "Robot" : $"Robot {robot.Id}";

                if (string.IsNullOrEmpty(robot.StartRef))
                {
                    error = $"{subject} start point is required";
                    return false;
                }

                if (string.IsNullOrEmpty(robot.GoalRef))
                {
                    error = $"{subject} goal point is required";
                    return false;
                }
            }
            
            return true;
        }
        
        /// <summary>
        /// Retourne une représentation lisible
        /// </summary>
        public override string ToString()
        {
            return $"[Scenario] {Name} v{Version} - {Humans?.Count ?? 0} human groups, {Points?.Count ?? 0} points";
        }
    }
}
