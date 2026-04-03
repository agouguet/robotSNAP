using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace RobotSNAP
{
    // Minimal contract exposing what the SocialForceCalculator needs from an agent.
    public interface ISocialForceAgent
    {
        Transform Transform { get; }
        Rigidbody RB { get; }
        NavMeshPath NavPath { get; }
        HashSet<GameObject> Neighbors { get; }
        HashSet<GameObject> Obstacles { get; }
        float DesiredSpeed { get; }
        float Mass { get; }
        float Radius { get; }
        float RobotRadius { get; }
        float RobotRepulsion { get; }
        Vector3 Velocity { get; set;}
        Vector3 NearestGoalPoint { get; }
    }
}
