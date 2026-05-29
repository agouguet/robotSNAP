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
        
        [YamlMember("dataset")]
        public string DatasetPath { get; set; }
        
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
        
        [YamlMember("center")]
        public Vector3? Center { get; set; }
        
        [YamlMember("size")]
        public Vector3? Size { get; set; }
        
        /// <summary>
        /// Est-ce un point simple ? (non sérialisé)
        /// </summary>
        [YamlIgnore]
        public bool IsPoint => X.HasValue && Y.HasValue && Z.HasValue;
        
        /// <summary>
        /// Est-ce une zone (bounds) ? (non sérialisé)
        /// </summary>
        [YamlIgnore]
        public bool IsBounds => Center.HasValue && Size.HasValue;
        
        /// <summary>
        /// Convertit en Vector3 (pour les points)
        /// </summary>
        public Vector3 ToVector3()
        {
            if (IsPoint) return new Vector3(X.Value, Y.Value, Z.Value);
            if (IsBounds) return Center.Value;
            return Vector3.zero;
        }
        
        /// <summary>
        /// Convertit en Bounds (pour les zones)
        /// </summary>
        public Bounds ToBounds()
        {
            if (IsBounds) return new Bounds(Center.Value, Size.Value);
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
                Center = center,
                Size = size
            };
        }
    }

    /// <summary>
    /// Configuration du robot
    /// </summary>
    [YamlObject]
    public partial class RobotScenarioConfig
    {
        [YamlMember("start")]
        public string StartRef { get; set; }
        
        [YamlMember("goal")]
        public string GoalRef { get; set; }
        
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
        
        [YamlMember("formation")]
        public string Formation { get; set; }
        
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
    /// Configuration du contrôleur de mouvement (SFM, ONNX, Hybride)
    /// </summary>
    [YamlObject]
    public partial class MovementControllerConfig
    {
        [YamlMember("type")]
        public string Type { get; set; } = "SFM";
        
        [YamlMember("onnx_model")]
        public string OnnxModel { get; set; }
        
        [YamlMember("sfm_params")]
        public SFMParams SFMParameters { get; set; }
        
        [YamlMember("hybrid_weights")]
        public HybridWeights HybridWeights { get; set; }
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
    /// Poids hybrides pour le contrôleur mixte
    /// </summary>
    [YamlObject]
    public partial class HybridWeights
    {
        [YamlMember("onnx_weight")]
        public float OnnxWeight { get; set; } = 0.7f;
        
        [YamlMember("sfm_weight")]
        public float SfmWeight { get; set; } = 0.3f;
        
        [YamlMember("adaptation_rate")]
        public float AdaptationRate { get; set; } = 0.1f;
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
            
            if (Robot == null)
            {
                error = "Robot configuration is required";
                return false;
            }
            
            if (string.IsNullOrEmpty(Robot.StartRef))
            {
                error = "Robot start point is required";
                return false;
            }
            
            if (string.IsNullOrEmpty(Robot.GoalRef))
            {
                error = "Robot goal point is required";
                return false;
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