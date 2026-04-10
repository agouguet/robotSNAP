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