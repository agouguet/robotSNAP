using System.Collections;
using UnityEngine;
using RobotSNAP.Core;

namespace RobotSNAP.Environment
{
    /// <summary>
    /// Construit l'environnement physique - Complètement indépendant
    /// </summary>
    public class EnvironmentBuilder : MonoBehaviour, IEnvironmentBuilder, IResettable
    {
        [Header("Environment Settings")]
        [SerializeField] private GameObject floorPrefab;
        [SerializeField] private GameObject wallPrefab;
        [SerializeField] private float floorRadius = 10f;
        [SerializeField] private int seed = 42;
        [SerializeField] private bool showGizmos = true;

        [Header("Dataset Creation")]
        [SerializeField] private bool useDatasetCreator = false;
        [SerializeField] private EnvironmentCreatorFromDataset datasetCreator;
        
        private GameObject _currentEnvironment;
        private bool _isReady;
        
        public bool IsReady => _isReady;
        
        private void Awake()
        {
            EventBus.Instance.Subscribe<ResetRequestEvent>(OnResetRequest);
        }
        
        private void OnDestroy()
        {
            EventBus.Instance.Unsubscribe<ResetRequestEvent>(OnResetRequest);
        }
        
        private void OnResetRequest(ResetRequestEvent evt)
        {
            StartCoroutine(Reset());
        }
        
        public IEnumerator BuildEnvironment()
        {
            if (_currentEnvironment != null)
            {
                Destroy(_currentEnvironment);
            }
            
            _currentEnvironment = new GameObject("Environment");
            _currentEnvironment.transform.parent = transform;

            if (useDatasetCreator && datasetCreator != null)
            {
                yield return StartCoroutine(datasetCreator.CreateEnvironment());
                
                // Récupérer les données pour le spawn
                if (datasetCreator.HasValidTask)
                {
                    // Les tâches robot seront utilisées par SpawnCoordinator
                    var robotTask = datasetCreator.RobotTask;
                    // Stocker pour SpawnCoordinator
                }
            }
            else
            {
                // Create floor
                if (floorPrefab != null)
                {
                    GameObject floor = Instantiate(floorPrefab, Vector3.zero, Quaternion.identity, _currentEnvironment.transform);
                    floor.transform.localScale = new Vector3(floorRadius * 2f, 1f, floorRadius * 2f);
                }
                
                // Create boundary walls
                CreateBoundaryWalls();
                
                _isReady = true;
                
                EventBus.Instance.Publish(new EnvironmentCreatedEvent
                {
                    environmentRoot = _currentEnvironment,
                    floorRadius = floorRadius
                });
                
                yield return null;
            }
        }
        
        private void CreateBoundaryWalls()
        {
            if (wallPrefab == null) return;
            
            float wallHeight = 2f;
            float wallThickness = 0.2f;
            int segments = 32;
            
            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                Vector3 position = new Vector3(
                    Mathf.Cos(angle) * floorRadius,
                    wallHeight / 2f,
                    Mathf.Sin(angle) * floorRadius
                );
                Quaternion rotation = Quaternion.LookRotation(position.normalized);
                
                GameObject wall = Instantiate(wallPrefab, position, rotation, _currentEnvironment.transform);
                wall.transform.localScale = new Vector3(wallThickness, wallHeight, 2f * Mathf.PI * floorRadius / segments);
            }
        }
        
        public float GetFloorRadius() => floorRadius;
        
        public IEnumerator Reset()
        {
            _isReady = false;
            yield return BuildEnvironment();
        }
        
        private void OnDrawGizmos()
        {
            if (!showGizmos) return;
            
            Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, floorRadius);
        }
    }
}