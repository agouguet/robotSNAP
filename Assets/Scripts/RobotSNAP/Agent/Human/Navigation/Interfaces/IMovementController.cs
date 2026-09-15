using System.Collections.Generic;
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
        /// <param name="neighbors">
        /// Positions des voisins perçus, ou <c>null</c> si l'agent n'en voit aucun.
        /// La liste est lue telle quelle pendant l'appel : le contrôleur ne la conserve pas et ne la modifie
        /// pas, ce qui évite une copie en tableau à chaque pas physique.
        /// </param>
        /// <param name="neighborVelocities">
        /// Vélocités des voisins, alignées index par index sur <paramref name="neighbors"/>.
        /// </param>
        /// <param name="staticObstacles">
        /// Positions des obstacles statiques perçus, ou <c>null</c> s'il n'y en a aucun.
        /// </param>
        /// <param name="cruiseSpeedOverride">
        /// Vitesse de croisière imposée pour cette frame (0 = celle de la configuration).
        /// Un suiveur de groupe en a besoin : plafonné à sa vitesse de croisière, il ne peut
        /// jamais rattraper le leader, donc l'écart de formation ne se referme jamais.
        /// </param>
        Vector2 ComputeVelocity(
            Vector2 currentPosition,
            Vector2 currentVelocity,
            Vector2 goalPosition,
            IReadOnlyList<Vector2> neighbors,
            IReadOnlyList<Vector2> neighborVelocities,
            IReadOnlyList<Vector2> staticObstacles,
            RobotObservation robot,
            float deltaTime,
            float cruiseSpeedOverride = 0f
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
