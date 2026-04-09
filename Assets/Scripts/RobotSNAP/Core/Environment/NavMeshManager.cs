using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using RobotSNAP.Utils;
using System.Collections.Generic;

using UnityEngine.UI;
using System;
using System.IO;
using System.Linq;

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
        private bool _isRebuilding = false;
        
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
                // Utilisez l'agentTypeID par défaut (0) si le vôtre est invalide
                int agentTypeID = spawnSurface.agentTypeID;
                if (agentTypeID < 0)
                {
                    Debug.LogWarning($"AgentTypeID invalide ({agentTypeID}) pour spawnSurface, utilisation de 0");
                    agentTypeID = 0;
                }
                
                _spawnFilter = new NavMeshQueryFilter
                {
                    agentTypeID = agentTypeID,
                    areaMask = spawnSurface.layerMask
                };
                
                Debug.Log($"Spawn Filter corrigé - AgentTypeID: {_spawnFilter.agentTypeID}, AreaMask: {_spawnFilter.areaMask}");
            }
            
            if (navigationSurface != null)
            {
                int agentTypeID = navigationSurface.agentTypeID;
                if (agentTypeID < 0)
                {
                    Debug.LogWarning($"AgentTypeID invalide ({agentTypeID}) pour navigationSurface, utilisation de 0");
                    agentTypeID = 0;
                }
                
                _navigationFilter = new NavMeshQueryFilter
                {
                    agentTypeID = agentTypeID,
                    areaMask = navigationSurface.layerMask
                };
                
                Debug.Log($"Navigation Filter corrigé - AgentTypeID: {_navigationFilter.agentTypeID}, AreaMask: {_navigationFilter.areaMask}");
            }
        }
        
        private void OnEnvironmentCreated(EnvironmentCreatedEvent evt)
        {
            if (autoBuildOnEnvironmentReady)
            {
                // Attendre que l'environnement soit complètement instancié
                StartCoroutine(DelayedBuildNavMeshes());
            }
        }
        
        private IEnumerator DelayedBuildNavMeshes()
        {
            // Attendre plusieurs frames pour que Unity ait fini d'instancier tous les objets
            // yield return new WaitForEndOfFrame();
            // yield return new WaitForEndOfFrame();
            // yield return new WaitForSeconds(0.2f);
            
            yield return BuildNavMeshes();
        }
        
        private void OnResetRequest(ResetRequestEvent evt)
        {
            StartCoroutine(Reset());
        }
        
        public IEnumerator BuildNavMeshes()
        {
            if (_isRebuilding)
            {
                Debug.Log("[NavMeshManager] Already rebuilding, skipping...");
                yield break;
            }
            
            _isRebuilding = true;
            _isReady = false;
            
            Debug.Log("[NavMeshManager] Starting NavMesh rebuild...");
            yield return null;
            
            // 2. Nettoyage COMPLET du NavMesh
            // 2a. Supprimer les données des surfaces
            // if (spawnSurface != null)
            // {
            //     spawnSurface.RemoveData();
            // }
            
            // if (navigationSurface != null)
            // {
            //     navigationSurface.RemoveData();
            // }
            
            yield return null;
            
            // 2b. Supprimer TOUTES les données NavMesh
            // NavMesh.RemoveAllNavMeshData();
            // 2c. Forcer un garbage collect et libérer les assets
            // Resources.UnloadUnusedAssets();
            // GC.Collect();
            
            yield return null;
            // yield return new WaitForSeconds(0.2f);
            
            // 3. Reconstruire avec un délai entre chaque
            if (spawnSurface != null)
            {
                Debug.Log("[NavMeshManager] Building spawn surface...");
                spawnSurface.BuildNavMesh();
                yield return null;
                // yield return new WaitForSeconds(0.1f);
            }
            
            if (navigationSurface != null)
            {
                Debug.Log("[NavMeshManager] Building navigation surface...");
                navigationSurface.BuildNavMesh();
                yield return null;
                // yield return new WaitForSeconds(0.3f); // Attendre plus longtemps
            }
            
            _isReady = true;
            _isRebuilding = false;
            
            Debug.Log("[NavMeshManager] NavMesh rebuild completed");
            
            EventBus.Instance.Publish(new NavMeshBuiltEvent { success = true });
        }

        public void ClearNavMeshes()
        {
            if (spawnSurface != null)
            {
                spawnSurface.RemoveData();
            }
            
            if (navigationSurface != null)
            {
                navigationSurface.RemoveData();
            }
            
            NavMesh.RemoveAllNavMeshData();
        }
        
        public Vector3 GetRandomNavigationPoint(float radius)
        {
            if (!_isReady)
            {
                Debug.LogWarning("[NavMeshManager] Not ready yet, returning center point");
                return transform.position;
            }
            return GetRandomPointSimple(radius);
            return NavMeshUtils.GetRandomPointOnNavMesh(transform.position, radius, _navigationFilter);
        }

        public Vector3 GetRandomSpawnPoint(float radius)
        {
            if (!_isReady)
            {
                Debug.LogWarning("[NavMeshManager] Not ready yet, returning center point");
                return transform.position;
            }
            return GetRandomPointSimple(radius);
            return NavMeshUtils.GetRandomPointOnNavMesh(transform.position, radius, _spawnFilter);
        }

        public Vector3 GetRandomPointSimple(float radius)
        {
            // Ne pas utiliser de filtre, juste le NavMesh par défaut
            return NavMeshUtils.GetRandomPointOnNavMeshSimple(transform.position, radius);
        }
        
        public float GetPathLength(Vector3 start, Vector3 end)
        {
            if (!_isReady)
            {
                return Vector3.Distance(start, end);
            }
            
            if (navigationSurface == null)
            {
                return Vector3.Distance(start, end);
            }
            
            return NavMeshUtils.GetNavMeshPathLength(start, end, _navigationFilter);
        }
        
        public IEnumerator Reset()
        {
            Debug.Log("[NavMeshManager] Reset requested, rebuilding NavMeshes...");
            
            // Attendre que l'environnement soit détruit et recréé
            // yield return new WaitForSeconds(0.1f);
            
            yield return BuildNavMeshes();
        }
        
        public NavMeshQueryFilter GetNavigationFilter() => _navigationFilter;
        public NavMeshQueryFilter GetSpawnFilter() => _spawnFilter;

        public NavMeshSurface GetSpawnSurface() => spawnSurface;
        public NavMeshSurface GetNavigationSurface() => navigationSurface;

        public System.Action OnNavMeshVisualizationReady;
    }
}