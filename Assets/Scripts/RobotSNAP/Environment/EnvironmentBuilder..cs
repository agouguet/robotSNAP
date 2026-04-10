// Scripts/RobotSNAP/Environment/EnvironmentBuilder.cs
using System.Collections;
using UnityEngine;
using RobotSNAP.Core;

namespace RobotSNAP.Environment
{
    /// <summary>
    /// Construit l'environnement physique - Délègue à EnvironmentCreator
    /// </summary>
    public class EnvironmentBuilder : MonoBehaviour, IEnvironmentBuilder, IResettable
    {
        [Header("Environment Settings")]
        [SerializeField] private EnvironmentCreatorFromDataset _datasetCreator;
        [SerializeField] private bool _showGizmos = true;
        
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
        
        /// <summary>
        /// Construit l'environnement avec le créateur actuel
        /// </summary>
        public IEnumerator BuildEnvironment()
        {
            if (_currentEnvironment != null)
            {
                Destroy(_currentEnvironment);
            }
            
            _currentEnvironment = new GameObject("Environment");
            _currentEnvironment.transform.parent = transform;
            
            if (_datasetCreator != null)
            {
                yield return StartCoroutine(_datasetCreator.CreateEnvironment());
                _isReady = _datasetCreator.IsEnvironmentReady;
            }
            else
            {
                Debug.LogWarning("[EnvironmentBuilder] No dataset creator assigned!");
                _isReady = false;
            }
            
            if (_isReady)
            {
                EventBus.Instance.Publish(new EnvironmentCreatedEvent
                {
                    environmentRoot = _currentEnvironment,
                    floorRadius = _datasetCreator?.GetFloorRadius() ?? 10f
                });
            }
            
            yield return null;
        }
        
        /// <summary>
        /// Construit l'environnement avec une map spécifique
        /// </summary>
        public IEnumerator BuildEnvironmentWithMap(string mapName)
        {
            if (_currentEnvironment != null)
            {
                Destroy(_currentEnvironment);
            }
            
            _currentEnvironment = new GameObject("Environment");
            _currentEnvironment.transform.parent = transform;
            
            if (_datasetCreator != null)
            {
                yield return StartCoroutine(_datasetCreator.LoadMap(mapName));
                _isReady = _datasetCreator.IsEnvironmentReady;
            }
            
            yield return null;
        }
        
        public IEnumerator Reset()
        {
            _isReady = false;
            yield return BuildEnvironment();
        }
        
        private void OnDrawGizmos()
        {
            if (!_showGizmos) return;
            if (_datasetCreator != null && _datasetCreator.IsEnvironmentReady)
            {
                Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
                Gizmos.DrawWireSphere(transform.position, _datasetCreator.GetFloorRadius());
            }
        }
    }
}