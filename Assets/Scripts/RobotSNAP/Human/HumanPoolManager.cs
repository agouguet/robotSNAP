using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RobotSNAP.Core.Scenario;
using RobotSNAP.Human;

namespace RobotSNAP.Core
{
    /// <summary>
    /// Pool d'humains - Gère la réutilisation des instances
    /// </summary>
    public class HumanPoolManager : MonoBehaviour, IHumanPool, IResettable, IPlayable
    {
        [Header("Pool Settings")]
        [SerializeField] private GameObject _humanPrefab;
        [SerializeField] private int _defaultPoolSize = 20;
        [SerializeField] private bool _warmupOnStart = true;
        
        [Header("Pool Parent")]
        [SerializeField] private Transform _poolParent;
        [SerializeField] private bool _createPoolParentIfMissing = true;
        [SerializeField] private string _poolParentName = "HumanPool";
        
        [Header("Default Human Configuration")]
        [SerializeField] private float _defaultSpeed = 1.2f;
        [SerializeField] private float _defaultMaxSpeed = 2.0f;
        [SerializeField] private int _defaultControllerType = 0; // 0=SFM, 1=ONNX, 2=Hybrid
        [SerializeField] private float _defaultInteractionRadius = 1.5f;
        [SerializeField] private float _defaultPersonalSpace = 0.8f;
        [SerializeField] private float _defaultAssertiveness = 0.5f;
        
        [Header("Debug")]
        [SerializeField] private bool _logEvents = true;
        
        private Queue<GameObject> _availablePool = new Queue<GameObject>();
        private List<GameObject> _activeHumans = new List<GameObject>();
        private Dictionary<GameObject, HumanConfig> _humanConfigs = new Dictionary<GameObject, HumanConfig>();
        private bool _isPlaying = true;
        private Transform _actualPoolParent;
        private SimulationConfig _currentConfig;
        
        public int ActiveCount => _activeHumans.Count;
        public IReadOnlyList<GameObject> ActiveHumans => _activeHumans;
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            InitializePoolParent();
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
        
        /// <summary>
        /// Applique la configuration globale aux nouveaux humains
        /// </summary>
        public void ApplyConfig(SimulationConfig config)
        {
            _currentConfig = config;
            
            if (config != null)
            {
                _defaultSpeed = config.HumanDefaultSpeed;
                _defaultMaxSpeed = config.HumanMaxSpeed;
                _defaultControllerType = config.HumanControllerType;
                _defaultInteractionRadius = config.HumanInteractionRadius;
                _defaultPersonalSpace = config.HumanPersonalSpace;
                _defaultAssertiveness = config.HumanAssertiveness;
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
            instance.name = $"Human_{GetTotalCount()}";
            
            // Appliquer la configuration par défaut
            ApplyDefaultConfigToHuman(instance);
            
            return instance;
        }
        
        private void ApplyDefaultConfigToHuman(GameObject human)
        {
            var movement = human.GetComponent<HumanMovement>();
            if (movement != null)
            {
                movement.SetDefaultSpeed(_defaultSpeed);
                movement.SetControllerType(_defaultControllerType);
            }
            
            var controller = human.GetComponent<IHumanController>();
            if (controller != null)
            {
                controller.SetInteractionRadius(_defaultInteractionRadius);
                controller.SetPersonalSpace(_defaultPersonalSpace);
                controller.SetAssertiveness(_defaultAssertiveness);
            }
            
            // Stocker la config
            _humanConfigs[human] = new HumanConfig
            {
                Speed = _defaultSpeed,
                MaxSpeed = _defaultMaxSpeed,
                ControllerType = _defaultControllerType,
                InteractionRadius = _defaultInteractionRadius,
                PersonalSpace = _defaultPersonalSpace,
                Assertiveness = _defaultAssertiveness
            };
        }
        
        /// <summary>
        /// Récupère un humain du pool
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
            
            // Réappliquer la config par défaut
            ApplyDefaultConfigToHuman(human);
            
            return human;
        }
        
        /// <summary>
        /// Récupère un humain avec configuration spécifique
        /// </summary>
        public GameObject GetHuman(HumanScenarioConfig scenarioConfig)
        {
            GameObject human = GetHuman();
            
            if (human != null && scenarioConfig != null)
            {
                ApplyScenarioConfigToHuman(human, scenarioConfig);
            }
            
            return human;
        }
        
        private void ApplyScenarioConfigToHuman(GameObject human, HumanScenarioConfig config)
        {
            var movement = human.GetComponent<HumanMovement>();
            if (movement != null)
            {
                movement.SetSpeed(config.Speed);
                if (!string.IsNullOrEmpty(config.Behavior))
                {
                    movement.SetBehavior(config.Behavior);
                }
            }
            
            var controller = human.GetComponent<IHumanController>();
            if (controller != null && config.Personality != null)
            {
                controller.SetAssertiveness(config.Personality.Assertiveness);
                controller.SetPersonalSpace(config.Personality.PersonalSpace);
                controller.SetReactionTime(config.Personality.ReactionTime);
            }
            
            // Appliquer la couleur
            if (config.Color != null && config.Color.Length >= 3)
            {
                var renderer = human.GetComponentInChildren<Renderer>();
                if (renderer != null)
                {
                    renderer.material.color = new Color(config.Color[0], config.Color[1], config.Color[2]);
                }
            }
            
            // Mettre à jour la config stockée
            if (_humanConfigs.ContainsKey(human))
            {
                var existing = _humanConfigs[human];
                existing.Speed = config.Speed;
                existing.ControllerType = GetControllerTypeFromString(config.MovementController?.Type ?? "SFM");
                _humanConfigs[human] = existing;
            }
        }
        
        private int GetControllerTypeFromString(string type)
        {
            switch (type?.ToLower())
            {
                case "sfm": return 0;
                case "onnx": return 1;
                case "hybrid": return 2;
                default: return 0;
            }
        }
        
        /// <summary>
        /// Retourne un humain au pool
        /// </summary>
        public void ReturnHuman(GameObject human)
        {
            if (human == null) return;
            
            human.SetActive(false);
            human.transform.SetParent(_actualPoolParent);
            human.transform.localPosition = Vector3.zero;
            human.transform.localRotation = Quaternion.identity;
            
            var controller = human.GetComponent<IHumanController>();
            controller?.FullReset();
            
            _activeHumans.Remove(human);
            _availablePool.Enqueue(human);
        }
        
        /// <summary>
        /// Retourne tous les humains au pool
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
        /// Désactive tous les humains actifs
        /// </summary>
        public void DeactivateAllHumans()
        {
            foreach (GameObject human in _activeHumans.ToArray())
            {
                if (human != null)
                {
                    human.SetActive(false);
                    var controller = human.GetComponent<IHumanController>();
                    controller?.FullReset();
                    _activeHumans.Remove(human);
                    _availablePool.Enqueue(human);
                }
            }
            
            if (_logEvents)
                Debug.Log($"[HumanPoolManager] Deactivated all humans. Available: {_availablePool.Count}");
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
            _humanConfigs.Clear();
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
                      $"  Total: {GetTotalCount()}\n" +
                      $"  Default Speed: {_defaultSpeed}\n" +
                      $"  Default Controller: {_defaultControllerType}");
        }
        
        [ContextMenu("Return All Humans")]
        private void EditorReturnAll()
        {
            ReturnAllHumans();
        }
        
        #endregion
    }
    
    /// <summary>
    /// Configuration d'un humain
    /// </summary>
    public struct HumanConfig
    {
        public float Speed;
        public float MaxSpeed;
        public int ControllerType;
        public float InteractionRadius;
        public float PersonalSpace;
        public float Assertiveness;
    }
}