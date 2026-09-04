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
            // Ne pas initialiser les filtres ici, attendre que les surfaces soient prêtes
            // On s'abonne aux événements
            EventBus.Instance.Subscribe<EnvironmentCreatedEvent>(OnEnvironmentCreated);
            EventBus.Instance.Subscribe<ResetRequestEvent>(OnResetRequest);
        }
        
        private void OnDestroy()
        {
            EventBus.Instance.Unsubscribe<EnvironmentCreatedEvent>(OnEnvironmentCreated);
            EventBus.Instance.Unsubscribe<ResetRequestEvent>(OnResetRequest);
        }
        
        /// <summary>
        /// Initialise les filtres avec les valeurs valides des surfaces.
        /// À appeler après que les surfaces aient été construites.
        /// </summary>
        private void InitializeFilters()
        {
            // Filtrer spawn
            if (spawnSurface != null)
            {
                int agentTypeID = spawnSurface.agentTypeID;
                if (agentTypeID < 0)
                {
                    Debug.LogWarning($"[NavMeshManager] AgentTypeID invalide ({agentTypeID}) pour spawnSurface, utilisation de 0");
                    agentTypeID = 0;
                }
                
                _spawnFilter = new NavMeshQueryFilter
                {
                    agentTypeID = agentTypeID,
                    areaMask = spawnSurface.layerMask
                };
                
                Debug.Log($"[NavMeshManager] Spawn Filter initialisé - AgentTypeID: {_spawnFilter.agentTypeID}, AreaMask: {_spawnFilter.areaMask}");
            }
            else
            {
                Debug.LogWarning("[NavMeshManager] spawnSurface est null, le filtre ne sera pas créé.");
            }
            
            // Filtrer navigation
            if (navigationSurface != null)
            {
                int agentTypeID = navigationSurface.agentTypeID;
                if (agentTypeID < 0)
                {
                    Debug.LogWarning($"[NavMeshManager] AgentTypeID invalide ({agentTypeID}) pour navigationSurface, utilisation de 0");
                    agentTypeID = 0;
                }
                
                _navigationFilter = new NavMeshQueryFilter
                {
                    agentTypeID = agentTypeID,
                    areaMask = navigationSurface.layerMask
                };
                
                Debug.Log($"[NavMeshManager] Navigation Filter initialisé - AgentTypeID: {_navigationFilter.agentTypeID}, AreaMask: {_navigationFilter.areaMask}");
            }
            else
            {
                Debug.LogWarning("[NavMeshManager] navigationSurface est null, le filtre ne sera pas créé.");
            }
        }
        
        private void OnEnvironmentCreated(EnvironmentCreatedEvent evt)
        {
            if (autoBuildOnEnvironmentReady)
            {
                StartCoroutine(DelayedBuildNavMeshes());
            }
        }
        
        private IEnumerator DelayedBuildNavMeshes()
        {
            // Attendre quelques frames pour que l'environnement soit complètement instancié
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            
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
            
            // 1. S'assurer que les références sont valides
            if (spawnSurface == null && navigationSurface == null)
            {
                Debug.LogError("[NavMeshManager] No NavMeshSurface assigned! Aborting build.");
                _isRebuilding = false;
                yield break;
            }
            
            // 2. Nettoyer les anciennes données (optionnel, mais recommandé)
            if (spawnSurface != null)
                spawnSurface.RemoveData();
            if (navigationSurface != null)
                navigationSurface.RemoveData();
            
            // 3. Forcer la mise à jour des surfaces (certaines surfaces ont besoin d'être réinitialisées)
            yield return null;
            
            // 4. Reconstruire avec un délai entre chaque
            if (spawnSurface != null)
            {
                Debug.Log("[NavMeshManager] Building spawn surface...");
                spawnSurface.BuildNavMesh();
                yield return null;
            }
            
            if (navigationSurface != null)
            {
                Debug.Log("[NavMeshManager] Building navigation surface...");
                navigationSurface.BuildNavMesh();
                yield return null;
            }
            
            // 5. Initialiser les filtres MAINTENANT (après le build, les agentTypeID sont stables)
            InitializeFilters();
            
            _isReady = true;
            _isRebuilding = false;
            
            Debug.Log("[NavMeshManager] NavMesh rebuild completed successfully.");
            
            EventBus.Instance.Publish(new NavMeshBuiltEvent { success = true });
        }

        public void ClearNavMeshes()
        {
            if (spawnSurface != null)
                spawnSurface.RemoveData();
            if (navigationSurface != null)
                navigationSurface.RemoveData();
            
            NavMesh.RemoveAllNavMeshData();
            _isReady = false;
        }
        
        public Vector3 GetRandomNavigationPoint(float radius)
        {
            if (!_isReady)
            {
                Debug.LogWarning("[NavMeshManager] Not ready yet, returning center point");
                return transform.position;
            }
            return GetRandomPointSimple(radius);
        }

        public Vector3 GetRandomSpawnPoint(float radius)
        {
            if (!_isReady)
            {
                Debug.LogWarning("[NavMeshManager] Not ready yet, returning center point");
                return transform.position;
            }
            return GetRandomPointSimple(radius);
        }

        public Vector3 GetRandomPointSimple(float radius)
        {
            // Utilise la méthode simple sans filtre spécifique
            return NavMeshUtils.GetRandomPointOnNavMeshSimple(transform.position, radius);
        }
        
        public float GetPathLength(Vector3 start, Vector3 end)
        {
            if (!_isReady || navigationSurface == null)
            {
                return Vector3.Distance(start, end);
            }

            return NavMeshUtils.GetNavMeshPathLength(start, end, _navigationFilter);
        }
        
        public IEnumerator Reset()
        {
            Debug.Log("[NavMeshManager] Reset requested, rebuilding NavMeshes...");
            yield return BuildNavMeshes();
        }
        
        public NavMeshQueryFilter GetNavigationFilter() => _navigationFilter;
        public NavMeshQueryFilter GetSpawnFilter() => _spawnFilter;

        public NavMeshSurface GetSpawnSurface() => spawnSurface;
        public NavMeshSurface GetNavigationSurface() => navigationSurface;

        public System.Action OnNavMeshVisualizationReady;
    }
}