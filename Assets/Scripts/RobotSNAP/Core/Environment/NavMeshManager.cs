using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using RobotSNAP.Utils;

namespace RobotSNAP.Core
{
    /// <summary>
    /// Gère les NavMeshes - Complètement indépendant
    /// </summary>
    public class NavMeshManager : MonoBehaviour, INavMeshManager, IResettable
    {
        [Header("NavMesh Surfaces")]
        [SerializeField] private NavMeshSurface spawnSurface;
        [SerializeField] private NavMeshSurface navigationSurface;
        
        [Header("Settings")]
        [SerializeField] private bool autoBuildOnEnvironmentReady = true;
        
        private bool _isReady;
        private NavMeshQueryFilter _spawnFilter;
        private NavMeshQueryFilter _navigationFilter;
        
        public bool IsReady => _isReady;
        
        private void Awake()
        {
            InitializeFilters();
            EventBus.Instance.Subscribe<EnvironmentCreatedEvent>(OnEnvironmentCreated);
            EventBus.Instance.Subscribe<ResetRequestEvent>(OnResetRequest);
        }
        
        private void OnDestroy()
        {
            EventBus.Instance.Unsubscribe<EnvironmentCreatedEvent>(OnEnvironmentCreated);
            EventBus.Instance.Unsubscribe<ResetRequestEvent>(OnResetRequest);
        }
        
        private void InitializeFilters()
        {
            if (spawnSurface != null)
            {
                _spawnFilter = new NavMeshQueryFilter
                {
                    agentTypeID = spawnSurface.agentTypeID,
                    areaMask = spawnSurface.layerMask
                };
            }
            
            if (navigationSurface != null)
            {
                _navigationFilter = new NavMeshQueryFilter
                {
                    agentTypeID = navigationSurface.agentTypeID,
                    areaMask = navigationSurface.layerMask
                };
            }
        }
        
        private void OnEnvironmentCreated(EnvironmentCreatedEvent evt)
        {
            if (autoBuildOnEnvironmentReady)
            {
                StartCoroutine(BuildNavMeshes());
            }
        }
        
        private void OnResetRequest(ResetRequestEvent evt)
        {
            StartCoroutine(Reset());
        }
        
        public IEnumerator BuildNavMeshes()
        {
            _isReady = false;
            
            if (spawnSurface != null)
            {
                spawnSurface.BuildNavMesh();
                yield return null;
            }
            
            if (navigationSurface != null)
            {
                navigationSurface.BuildNavMesh();
                yield return null;
            }
            
            _isReady = true;
            
            EventBus.Instance.Publish(new NavMeshBuiltEvent { success = true });
        }
        
        public Vector3 GetRandomPoint(float radius)
        {
            NavMeshQueryFilter filter = navigationSurface != null ? _navigationFilter : _spawnFilter;
            return NavMeshUtils.GetRandomPointOnNavMesh(transform.position, radius, filter);
        }
        
        public float GetPathLength(Vector3 start, Vector3 end)
        {
            if (navigationSurface == null)
            {
                return Vector3.Distance(start, end);
            }
            
            return NavMeshUtils.GetNavMeshPathLength(start, end, _navigationFilter);
        }
        
        public IEnumerator Reset()
        {
            yield return BuildNavMeshes();
        }
        
        public NavMeshQueryFilter GetNavigationFilter() => _navigationFilter;
        public NavMeshQueryFilter GetSpawnFilter() => _spawnFilter;

        public NavMeshSurface GetSpawnSurface() => spawnSurface;
        public NavMeshSurface GetNavigationSurface() => navigationSurface;

        public System.Action OnNavMeshVisualizationReady;
    }
}