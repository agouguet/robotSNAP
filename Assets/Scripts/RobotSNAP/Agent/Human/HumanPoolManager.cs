using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RobotSNAP.Core.Scenario;
using RobotSNAP.Core;

namespace RobotSNAP.Agents
{
    /// <summary>
    /// Pool d'humains - Gère la réutilisation des instances.
    /// Ne contient aucune logique de configuration métier (vitesse, comportement, etc.).
    /// La configuration est entièrement déléguée au ScenarioApplier.
    /// </summary>
    public class HumanPoolManager : MonoBehaviour, IResettable, IPlayable
    {
        [Header("Pool Settings")]
        [SerializeField] private GameObject _humanPrefab;
        [SerializeField] private int _defaultPoolSize = 20;
        [SerializeField] private bool _warmupOnStart = true;
        
        [Header("Pool Parent")]
        [SerializeField] private Transform _poolParent;
        [SerializeField] private bool _createPoolParentIfMissing = true;
        [SerializeField] private string _poolParentName = "HumanPool";
        
        [Header("Debug")]
        [SerializeField] private bool _logEvents = true;
        

        private HumanManager _humanManager;
        private Queue<GameObject> _availablePool = new Queue<GameObject>();
        private List<GameObject> _activeHumans = new List<GameObject>();
        private bool _isPlaying = true;
        private Transform _actualPoolParent;
        
        public int ActiveCount => _activeHumans.Count;
        public IReadOnlyList<GameObject> ActiveHumans => _activeHumans;
        private int _totalCreated = 0;
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            InitializePoolParent();
            _humanManager = GetComponentInParent<HumanManager>();
            if (_humanManager == null)
                Debug.LogWarning("[HumanPoolManager] HumanManager not found in parent hierarchy!");
        }
        
        private void Start()
        {
            if (_warmupOnStart)
            {
                StartCoroutine(Prewarm(_defaultPoolSize));
            }
            
            EventBus.Instance.Subscribe<ResetRequestEvent>(OnResetRequest);
            EventBus.Instance.Subscribe<PlayStateChangedEvent>(OnPlayStateChanged);
        }
        
        private void OnDestroy()
        {
            EventBus.Instance.Unsubscribe<ResetRequestEvent>(OnResetRequest);
            EventBus.Instance.Unsubscribe<PlayStateChangedEvent>(OnPlayStateChanged);
        }
        
        #endregion
        
        #region Initialization
        
        private void InitializePoolParent()
        {
            if (_poolParent != null)
            {
                _actualPoolParent = _poolParent;
            }
            else if (_createPoolParentIfMissing)
            {
                GameObject poolContainer = new GameObject(_poolParentName);
                _actualPoolParent = poolContainer.transform;
                _actualPoolParent.SetParent(transform);
            }
            else
            {
                _actualPoolParent = transform;
            }
        }
        
        #endregion
        
        #region Event Handlers
        
        private void OnResetRequest(ResetRequestEvent evt)
        {
            StartCoroutine(Reset());
        }
        
        private void OnPlayStateChanged(PlayStateChangedEvent evt)
        {
            SetPlay(evt.isPlaying);
        }
        
        #endregion
        
        #region Pool Management
        
        public IEnumerator Prewarm(int poolSize)
        {
            for (int i = 0; i < poolSize; i++)
            {
                GameObject human = CreateHumanInstance();
                _availablePool.Enqueue(human);
                
                if (i % 5 == 0 && i > 0)
                {
                    yield return null;
                }
            }
            
            if (_logEvents)
                Debug.Log($"[HumanPoolManager] Prewarmed {poolSize} humans");
        }
        
        private GameObject CreateHumanInstance()
        {
            if (_humanPrefab == null)
            {
                Debug.LogError("[HumanPoolManager] HumanPrefab is null!");
                return null;
            }
            
            Transform parent = _actualPoolParent ?? transform;
            GameObject instance = Instantiate(_humanPrefab, Vector3.zero, Quaternion.identity, parent);
            instance.SetActive(false);
            instance.name = $"Human_{_totalCreated}";
            instance.GetComponent<HumanAgent>()?.SetAgentId(_totalCreated);
            instance.GetComponent<HumanAgent>()?.SetAgentName($"Human {_totalCreated}");
            if (_humanManager != null)
                instance.GetComponent<HumanAgent>()?.SetHumanManager(_humanManager);
            _totalCreated++;
        
            
            return instance;
        }
        
        /// <summary>
        /// Récupère une instance humaine du pool (désactivée et non configurée).
        /// </summary>
        public GameObject GetHuman()
        {
            if (_availablePool.Count == 0)
            {
                if (_logEvents)
                    Debug.LogWarning("[HumanPoolManager] Pool exhausted, creating new instance");
                GameObject newHuman = CreateHumanInstance();
                _availablePool.Enqueue(newHuman);
            }
            
            GameObject human = _availablePool.Dequeue();
            _activeHumans.Add(human);
            
            if (human.transform.parent != _actualPoolParent)
            {
                human.transform.SetParent(_actualPoolParent);
            }
            
            // Ne pas configurer ici. Le ScenarioApplier le fera.
            return human;
        }
        
        /// <summary>
        /// Retourne un humain au pool.
        /// </summary>
        public void ReturnHuman(GameObject human)
        {
            if (human == null) return;
            
            // Désactiver et remettre à zéro la position/rotation
            human.SetActive(false);
            human.transform.SetParent(_actualPoolParent);
            human.transform.localPosition = Vector3.zero;
            human.transform.localRotation = Quaternion.identity;
            
            // Réinitialiser complètement le contrôleur (annule tout état)
            var controller = human.GetComponent<IHumanController>();
            controller?.FullReset();
            
            _activeHumans.Remove(human);
            _availablePool.Enqueue(human);
        }
        
        /// <summary>
        /// Retourne tous les humains actifs au pool.
        /// </summary>
        public void ReturnAllHumans()
        {
            foreach (GameObject human in _activeHumans.ToArray())
            {
                ReturnHuman(human);
            }
            
            if (_logEvents)
                Debug.Log($"[HumanPoolManager] Returned all humans to pool");
        }
        
        /// <summary>
        /// Désactive tous les humains actifs (équivalent à ReturnAllHumans).
        /// </summary>
        public void DeactivateAllHumans()
        {
            ReturnAllHumans();
        }
        
        #endregion
        
        #region IPlayable Implementation
        
        public void SetPlay(bool isPlaying)
        {
            _isPlaying = isPlaying;
            
            foreach (GameObject human in _activeHumans)
            {
                var controller = human.GetComponent<IHumanController>();
                controller?.SetPlay(isPlaying);
            }
        }
        
        public bool IsPlaying => _isPlaying;
        
        #endregion
        
        #region IResettable Implementation
        
        public IEnumerator Reset()
        {
            ReturnAllHumans();
            Debug.Log($"[HumanPoolManager] Reset complete. Active: {ActiveCount}, Available: {_availablePool.Count}");
            yield return null;
        }
        
        #endregion
        
        #region Public Methods
        
        public void SetPoolParent(Transform newParent)
        {
            _poolParent = newParent;
            _actualPoolParent = newParent ?? (_createPoolParentIfMissing ? CreatePoolContainer() : transform);
            
            foreach (var human in _availablePool)
            {
                if (human != null) human.transform.SetParent(_actualPoolParent);
            }
            
            foreach (var human in _activeHumans)
            {
                if (human != null) human.transform.SetParent(_actualPoolParent);
            }
        }
        
        private Transform CreatePoolContainer()
        {
            GameObject container = new GameObject(_poolParentName);
            container.transform.SetParent(transform);
            return container.transform;
        }
        
        public void ClearPool()
        {
            ReturnAllHumans();
            
            foreach (var human in _availablePool)
            {
                if (human != null) Destroy(human);
            }
            _availablePool.Clear();
        }
        
        #endregion
        
        #region Public Getters
        
        public int GetAvailableCount() => _availablePool.Count;
        public int GetTotalCount() => _availablePool.Count + _activeHumans.Count;
        public Transform GetPoolParent() => _actualPoolParent;
        public List<GameObject> GetActiveHumans() => new List<GameObject>(_activeHumans);
        
        #endregion
        
        #region Debug
        
        [ContextMenu("Log Pool Stats")]
        private void EditorLogPoolStats()
        {
            Debug.Log($"[HumanPoolManager] Pool Stats:\n" +
                      $"  Available: {GetAvailableCount()}\n" +
                      $"  Active: {ActiveCount}\n" +
                      $"  Total: {GetTotalCount()}");
        }
        
        [ContextMenu("Return All Humans")]
        private void EditorReturnAll()
        {
            ReturnAllHumans();
        }
        
        #endregion
    }
}