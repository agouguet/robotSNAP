using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace RobotSNAP.Movement.Interfaces
{
    /// <summary>
    /// Interface pour les agents utilisant le Social Force Model
    /// </summary>
    public interface ISocialForceAgent
    {
        // Transformations
        Transform Transform { get; }
        Rigidbody RB { get; }
        
        // Navigation
        NavMeshPath NavPath { get; }
        Vector3 NearestGoalPoint { get; }
        
        // Détection
        HashSet<GameObject> Neighbors { get; }
        HashSet<GameObject> Obstacles { get; }
        
        // Paramètres physiques
        float Mass { get; }
        float Radius { get; }
        float DesiredSpeed { get; }
        
        // État
        Vector3 Velocity { get; set; }
        
        // Forces
        Vector3 ComputeGoalForce(Vector3 currentPos, Vector3 goalPos, float desiredSpeed);
        Vector3 ComputeSocialForce();
        Vector3 ComputeObstacleForce();
        Vector3 ComputeRobotForce();
        
        // Configuration des forces (pour le système de scénarios)
        void SetAssertiveness(float value);
        void SetPersonalSpace(float value);
        void SetInteractionRadius(float value);
        void SetReactionTime(float value);
    }
}