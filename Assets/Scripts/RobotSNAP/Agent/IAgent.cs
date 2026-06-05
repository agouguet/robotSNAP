using UnityEngine;
using System;

namespace RobotSNAP.Agents
{
    /// <summary>
    /// Interface commune pour tous les agents (humains, robots, etc.) dans la simulation.
    /// Fournit les propriétés et méthodes essentielles pour le mouvement, la perception et les interactions sociales.
    /// </summary>
    public interface IAgent
    {
        // ==================== IDENTIFICATION ====================
        /// <summary>Nom de l'agent (pour l'affichage UI et les logs).</summary>
        string AgentName { get; }
        
        /// <summary>GameObject Unity associé.</summary>
        GameObject gameObject { get; }
        
        /// <summary>Transform Unity associé.</summary>
        Transform transform { get; }

        // ==================== POSITION ET ORIENTATION ====================
        /// <summary>Position 3D dans le monde (généralement au sol, Y = 0).</summary>
        Vector3 Position { get; }
        
        /// <summary>Position 2D (plan XZ).</summary>
        Vector2 Position2D { get; }
        
        /// <summary>Rotation de l'agent (Yaw uniquement pour la navigation).</summary>
        Quaternion Rotation { get; }
        
        /// <summary>Direction avant (vecteur unitaire).</summary>
        Vector3 Forward { get; }

        // ==================== MOUVEMENT ET VITESSE ====================
        /// <summary>Vélocité linéaire 3D (m/s).</summary>
        Vector3 Velocity { get; }
        
        /// <summary>Vélocité linéaire 2D (plan XZ).</summary>
        Vector2 Velocity2D { get; }
        
        /// <summary>Vitesse linéaire instantanée (magnitude).</summary>
        float Speed { get; }
        
        /// <summary>Vitesse angulaire (rad/s) autour de l'axe vertical.</summary>
        float AngularSpeed { get; }

        // ==================== CARACTÉRISTIQUES PHYSIQUES ====================
        /// <summary>Rayon de collision / encombrement (m).</summary>
        float Radius { get; }
        
        /// <summary>Masse (kg) utilisée dans les forces sociales.</summary>
        float Mass { get; }

        // ==================== NAVIGATION ET OBJECTIFS ====================
        /// <summary>Indique si un objectif (goal) est actif.</summary>
        bool HasGoal { get; }
        
        /// <summary>Position de l'objectif courant (en 3D).</summary>
        Vector3 Goal { get; }
        
        /// <summary>Définit un nouvel objectif.</summary>
        void SetGoal(Vector3 goal);
        
        /// <summary>Annule l'objectif courant et arrête tout déplacement autonome.</summary>
        void ClearGoal();
        
        /// <summary>Arrête immédiatement le mouvement (vitesse à zéro).</summary>
        void Stop();

        // ==================== COMPORTEMENT ET PERSONNALITÉ ====================
        /// <summary>Nom du comportement actuel (normal, timid, aggressive, etc.).</summary>
        string Behavior { get; }
        
        /// <summary>Change le comportement (affecte vitesse max, réactivité, etc.).</summary>
        void SetBehavior(string behavior);
        
        /// <summary>Vitesse désirée (préférence personnelle).</summary>
        float DesiredSpeed { get; }
        
        /// <summary>Change la vitesse désirée de l'agent.</summary>
        void SetSpeed(float speed);

        // ==================== PARAMÈTRES SOCIAUX (SFM) ====================
        /// <summary>Rayon d'interaction sociale (distance à laquelle les autres sont perçus).</summary>
        float InteractionRadius { get; }
        
        /// <summary>Rayon d'espace personnel (distance de confort).</summary>
        float PersonalSpace { get; }
        
        /// <summary>Assertivité (0 = très social / évitant, 1 = dominant).</summary>
        float Assertiveness { get; }
        
        /// <summary>Temps de réaction (inertie / latence).</summary>
        float ReactionTime { get; }
        
        /// <summary>Modifie le rayon d'interaction sociale.</summary>
        void SetInteractionRadius(float radius);
        
        /// <summary>Modifie l'espace personnel.</summary>
        void SetPersonalSpace(float space);
        
        /// <summary>Modifie l'assertivité.</summary>
        void SetAssertiveness(float value);
        
        /// <summary>Modifie le temps de réaction.</summary>
        void SetReactionTime(float time);

        // ==================== CYCLE DE VIE ====================
        /// <summary>Indique si l'agent est actif (non détruit, non désactivé, pas en pause).</summary>
        bool IsActive { get; }
        
        /// <summary>Réinitialise l'agent : stop, clear goal, reset pose (position par défaut).</summary>
        void Reset();
        
        /// <summary>Active ou désactive l'agent (le rend perceptible ou non).</summary>
        void SetActive(bool active);
    }
}