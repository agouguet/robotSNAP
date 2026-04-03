using System;
using UnityEngine;

namespace RobotSNAP.Core
{
    #region Events
    
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
    
    public struct PlayStateChangedEvent
    {
        public bool isPlaying;
    }
    
    public enum SpawnType
    {
        Robot,
        Human,
        All
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
        float GetFloorRadius();
        bool IsReady { get; }
    }
    
    public interface INavMeshManager
    {
        System.Collections.IEnumerator BuildNavMeshes();
        bool IsReady { get; }
        Vector3 GetRandomPoint(float radius);
        float GetPathLength(Vector3 start, Vector3 end);
    }
    
    public interface ISpawner
    {
        System.Collections.IEnumerator SpawnRobot(SpawnData data);
        System.Collections.IEnumerator SpawnHuman(SpawnData data);
        void DespawnAll();
        bool CanSpawn { get; }
    }
    
    public interface IHumanPool
    {
        System.Collections.IEnumerator Prewarm(int poolSize);
        GameObject GetHuman();
        void ReturnHuman(GameObject human);
        void DeactivateAllHumans();
        int ActiveCount { get; }
    }
    
    public interface IHumanController
    {
        void Initialize(float desiredSpeed, float maxSpeed);
        void SetGoal(Vector3 goal);
        void SetPlay(bool isPlaying);
        void Reset();
        void FullReset();
        Vector3 GetPosition();
        Vector3 GetVelocity();
        Vector3 GetCurrentPosition3D();  // Ajouté
        Vector2 GetCurrentPosition2D();  // Ajouté
        bool HasDestination { get; }      // Ajouté
        void SetVelocity(Vector3 velocity); // Ajouté
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