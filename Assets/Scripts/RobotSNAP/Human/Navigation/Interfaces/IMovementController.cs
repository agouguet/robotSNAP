using UnityEngine;

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
        /// <param name="currentPosition">Position actuelle (2D)</param>
        /// <param name="currentVelocity">Vélocité actuelle (2D)</param>
        /// <param name="goalPosition">Position du goal (2D) - point sur le chemin</param>
        /// <param name="neighbors">Liste des positions des agents voisins (2D)</param>
        /// <param name="neighborVelocities">Vélocités des voisins (optionnel)</param>
        /// <param name="deltaTime">Temps écoulé depuis la dernière frame</param>
        /// <returns>Vélocité désirée en m/s</returns>
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
        /// </summary>
        float GetConfidence();
    }
}