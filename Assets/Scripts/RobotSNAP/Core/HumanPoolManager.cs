using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotSNAP.Core
{
    /// <summary>
    /// Pool d'humains - Gère la réutilisation des instances
    /// </summary>
    public class HumanPoolManager : MonoBehaviour, IHumanPool, IResettable, IPlayable
    {
        [Header("Pool Settings")]
        [SerializeField] private GameObject humanPrefab;
        [SerializeField] private int defaultPoolSize = 20;
        [SerializeField] private bool warmupOnStart = true;
        
        [Header("Pool Parent")]
        [SerializeField] private Transform poolParent;
        [SerializeField] private bool createPoolParentIfMissing = true;
        [SerializeField] private string poolParentName = "HumanPool";
        
        [Header("Human Configuration")]
        [SerializeField] private float defaultSpeed = 0.8f;
        [SerializeField] private float defaultMaxSpeed = 1f;
        
        private Queue<GameObject> _availablePool = new Queue<GameObject>();
        private List<GameObject> _activeHumans = new List<GameObject>();
        private bool _isPlaying = true;
        private Transform _actualPoolParent;
        
        public int ActiveCount => _activeHumans.Count;
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            InitializePoolParent();
        }
        
        private void Start()
        {
            if (warmupOnStart)
            {
                StartCoroutine(Prewarm(defaultPoolSize));
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
            if (poolParent != null)
            {
                _actualPoolParent = poolParent;
            }
            else if (createPoolParentIfMissing)
            {
                GameObject poolContainer = new GameObject(poolParentName);
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
            
            Debug.Log($"[HumanPoolManager] Prewarmed {poolSize} humans");
        }
        
        private GameObject CreateHumanInstance()
        {
            if (humanPrefab == null)
            {
                Debug.LogError("[HumanPoolManager] HumanPrefab is null!");
                return null;
            }
            
            Transform parent = _actualPoolParent ?? transform;
            GameObject instance = Instantiate(humanPrefab, Vector3.zero, Quaternion.identity, parent);
            instance.SetActive(false);
            instance.name = $"Human_{GetTotalCount()}";
            
            IHumanController controller = instance.GetComponent<IHumanController>();
            controller?.Initialize();
            
            return instance;
        }
        
        public GameObject GetHuman()
        {
            if (_availablePool.Count == 0)
            {
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
            
            return human;
        }
        
        public void ReturnHuman(GameObject human)
        {
            if (human == null) return;
            
            human.SetActive(false);
            human.transform.SetParent(_actualPoolParent);
            human.transform.localPosition = Vector3.zero;
            human.transform.localRotation = Quaternion.identity;
            
            IHumanController controller = human.GetComponent<IHumanController>();
            controller?.FullReset();
            
            _activeHumans.Remove(human);
            _availablePool.Enqueue(human);
        }
        
        /// <summary>
        /// Désactive tous les humains actifs et les retourne au pool
        /// </summary>
        public void DeactivateAllHumans()
        {
            foreach (GameObject human in _activeHumans.ToArray())
            {
                if (human != null)
                {
                    human.SetActive(false);
                    IHumanController controller = human.GetComponent<IHumanController>();
                    controller?.FullReset();
                    _activeHumans.Remove(human);
                    _availablePool.Enqueue(human);
                }
            }
            
            if (logPoolEvents)
            {
                Debug.Log($"[HumanPoolManager] Deactivated all humans. Available: {_availablePool.Count}");
            }
        }
        
        public void ReturnAll()
        {
            DeactivateAllHumans();
        }
        
        #endregion
        
        #region IPlayable Implementation
        
        public void SetPlay(bool isPlaying)
        {
            _isPlaying = isPlaying;
            
            foreach (GameObject human in _activeHumans)
            {
                IHumanController controller = human.GetComponent<IHumanController>();
                controller?.SetPlay(isPlaying);
            }
        }
        
        public bool IsPlaying => _isPlaying;
        
        #endregion
        
        #region IResettable Implementation
        
        public IEnumerator Reset()
        {
            DeactivateAllHumans();
            yield return null;
        }
        
        #endregion
        
        #region Public Methods
        
        public void SetPoolParent(Transform newParent)
        {
            poolParent = newParent;
            _actualPoolParent = newParent ?? (createPoolParentIfMissing ? CreatePoolContainer() : transform);
            
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
            GameObject container = new GameObject(poolParentName);
            container.transform.SetParent(transform);
            return container.transform;
        }
        
        public void ClearPool()
        {
            DeactivateAllHumans();
            
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
        
        #endregion
        
        #region Debug
        
        [Header("Debug")]
        [SerializeField] private bool logPoolEvents = true;
        
        [ContextMenu("Log Pool Stats")]
        private void EditorLogPoolStats()
        {
            Debug.Log($"[HumanPoolManager] Pool Stats:\n" +
                      $"  Available: {GetAvailableCount()}\n" +
                      $"  Active: {ActiveCount}\n" +
                      $"  Total: {GetTotalCount()}");
        }
        
        [ContextMenu("Deactivate All Humans")]
        private void EditorDeactivateAll()
        {
            DeactivateAllHumans();
        }
        
        #endregion
    }
}