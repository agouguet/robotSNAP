using UnityEngine;
using RobotSNAP.Agents;

namespace RobotSNAP.Agents.Movement.Interfaces
{
    /// <summary>
    /// The robot as perceived by a human controller: where it is, where it goes and how wide it is.
    /// Controllers that ignore the robot simply do not read it.
    /// </summary>
    public readonly struct RobotObservation
    {
        public RobotObservation(bool isVisible, Vector2 position, Vector2 velocity, float radius)
        {
            IsVisible = isVisible;
            Position = position;
            Velocity = velocity;
            Radius = Mathf.Max(0.05f, radius);
        }

        public bool IsVisible { get; }
        public Vector2 Position { get; }
        public Vector2 Velocity { get; }
        public float Radius { get; }

        public static RobotObservation None => default;
    }

    /// <summary>
    /// Interface pour les contrôleurs de mouvement
    /// Le contrôleur ne gère que le calcul de la vélocité désirée
    /// La navigation, les collisions et les murs sont gérés par HumanMovement
    /// </summary>
    public interface IMovementController
    {
        /// <summary>
        /// Calcule la vélocité désirée en fonction de l'état courant
        /// </summary>
        Vector2 ComputeVelocity(
            Vector2 currentPosition,
            Vector2 currentVelocity,
            Vector2 goalPosition,
            Vector2[] neighbors,
            Vector2[] neighborVelocities,
            Vector2[] staticObstacles,
            RobotObservation robot,
            float deltaTime
        );
        
        /// <summary>
        /// Réinitialise l'état du contrôleur
        /// </summary>
        void Reset();
        
        /// <summary>
        /// Retourne le niveau de confiance de la prédiction (0-1)
        /// Pour SFM, retourne toujours 1
        /// </summary>
        float GetConfidence();
        
        /// <summary>
        /// Met à jour les paramètres du contrôleur
        /// </summary>
        public void UpdateParameters(HumanConfig config);
    }
}
