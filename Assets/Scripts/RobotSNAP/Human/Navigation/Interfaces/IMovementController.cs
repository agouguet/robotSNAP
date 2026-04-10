using UnityEngine;
using RobotSNAP.Human;

namespace RobotSNAP.Movement.Interfaces
{
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