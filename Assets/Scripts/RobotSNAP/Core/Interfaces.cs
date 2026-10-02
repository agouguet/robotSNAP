using System;
using UnityEngine;

namespace RobotSNAP.Core
{
    // The events and interfaces still in force. The ones that were only ever declared - a spawn request
    // nobody published, a bridge nobody implemented, the environment notifications that the direct calls in
    // GameManager replaced long ago - are gone, so what a reader finds in this file is what the simulation
    // actually speaks.

    #region Events

    /// <summary>Raised by the NavMesh manager once the surfaces of an environment have been rebuilt.</summary>
    public struct NavMeshBuiltEvent
    {
        public bool success;
    }

    // --- Commandes de la simulation (plus explicites que les événements ci-dessus) ---

    /// <summary>
    /// Commande : Démarrer la simulation depuis zéro.
    /// Charge le scénario par défaut et lance le Clock.
    /// </summary>
    public struct StartSimulationCommand { }

    /// <summary>
    /// Commande : Arrêter complètement la simulation.
    /// Détruit les environnements, réinitialise le scénario et met le Clock en pause.
    /// </summary>
    public struct StopSimulationCommand { }

    /// <summary>
    /// Commande : Mettre l'horloge en pause (sans décharger le scénario).
    /// </summary>
    public struct PauseSimulationCommand { }

    /// <summary>
    /// Commande : Reprendre l'horloge (sans recharger le scénario).
    /// </summary>
    public struct ResumeSimulationCommand { }

    /// <summary>
    /// Notification : l'état de la simulation a changé.
    /// Utilisé pour mettre à jour l'UI sans polling.
    /// </summary>
    public struct SimulationStateChangedEvent
    {
        public SimulationState NewState { get; set; }
    }

    public enum SimulationState
    {
        Idle,       // Aucun scénario chargé
        Ready,      // Scénario chargé, Clock arrêté (en attente de démarrage)
        Running,    // Scénario chargé et Clock en cours
        Paused      // Scénario chargé mais Clock en pause
    }

    #endregion

    #region Interfaces

    public interface IEventBus
    {
        void Publish<T>(T evt);
        void Subscribe<T>(Action<T> handler);
        void Unsubscribe<T>(Action<T> handler);
    }

    public interface INavMeshManager
    {
        System.Collections.IEnumerator BuildNavMeshes();
        bool IsReady { get; }
        Vector3 GetRandomSpawnPoint(float radius);
        Vector3 GetRandomNavigationPoint(float radius);
        Vector3 GetRandomPointSimple(float radius);
        float GetPathLength(Vector3 start, Vector3 end);
    }

    public interface IHumanController
    {
        void Initialize();
        void FullReset();
        void SetPlay(bool isPlaying);
        void SetGoal(Vector3 goal);
        void SetInteractionRadius(float radius);
        void SetPersonalSpace(float space);
        void SetAssertiveness(float value);
        void SetReactionTime(float time);
    }

    public interface IResettable
    {
        System.Collections.IEnumerator Reset();
    }

    public interface IPlayable
    {
        void SetPlay(bool isPlaying);
        bool IsPlaying { get; }
    }

    #endregion
}
