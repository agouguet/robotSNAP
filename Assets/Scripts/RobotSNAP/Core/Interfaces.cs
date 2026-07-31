using System;
using UnityEngine;

namespace RobotSNAP.Core
{
    #region Events

    // --- Événements existants ---
    
    public struct EnvironmentCreatedEvent
    {
        public GameObject environmentRoot;
        public float floorRadius;
    }
    
    public struct NavMeshBuiltEvent
    {
        public bool success;
    }
    
    public struct SpawnRequestEvent
    {
        public SpawnType type;
        public int count;
    }
    
    public struct SpawnCompletedEvent
    {
        public GameObject[] spawnedObjects;
        public SpawnType type;
    }
    
    public struct ResetRequestEvent
    {
        public string dataset;
    }
    
    public struct ResetCompletedEvent
    {
        public bool success;
    }
    
    // Événement legacy, conservé pour rétrocompatibilité
    public struct PlayStateChangedEvent
    {
        public bool isPlaying;
    }

    // --- Nouveaux événements de commande (plus explicites) ---

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

    // --- Enums existants ---

    public enum SpawnType
    {
        Robot,
        Human,
        All
    }

    public struct GameManagerInitializedEvent
    {
        public int EnvironmentId;
        public bool Success;
    }

    public struct GameManagerResetStartedEvent
    {
        public int EnvironmentId;
    }

    public struct GameManagerResetCompletedEvent
    {
        public int EnvironmentId;
        public bool Success;
    }

    public struct ScenarioDurationReachedEvent
    {
        public int EnvironmentId;
    }

    #endregion

    #region Interfaces

    public interface IEventBus
    {
        void Publish<T>(T evt);
        void Subscribe<T>(Action<T> handler);
        void Unsubscribe<T>(Action<T> handler);
    }

    public interface IEnvironmentBuilder
    {
        System.Collections.IEnumerator BuildEnvironment();
        // float GetFloorRadius();
        bool IsReady { get; }
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

    public interface ISpawner
    {
        System.Collections.IEnumerator SpawnRobot(SpawnData data);
        System.Collections.IEnumerator SpawnHuman(SpawnData data);
        void DespawnAll();
        bool CanSpawn { get; }
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

    public interface IROSBridge
    {
        void Initialize(string prefix);
        void PublishResetDone(bool success);
        void PublishMap(MapData map);
        void PublishLocalGoal(Vector3 goal);
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

    #region Data Structures

    public struct SpawnData
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 goalPosition;
        public Quaternion goalRotation;
        public string id;
        public SpawnType type;
    }

    public struct MapData
    {
        public int width;
        public int height;
        public float resolution;
        public byte[] data;
        public Vector3 origin;
    }

    #endregion
}