using UnityEngine;
using RobotSNAP.Core;
using RobotSNAP.ROS;

namespace RobotSNAP
{
    /// <summary>
    /// Contrôleur de robot avec support clavier et ROS.
    /// Permet de basculer entre les modes de contrôle.
    /// </summary>
    public class RobotInputController : MonoBehaviour
    {
        [Header("Control Mode")]
        [SerializeField] private ControlMode controlMode = ControlMode.Keyboard;
        [SerializeField] private KeyCode toggleModeKey = KeyCode.M;
        
        [Header("Keyboard Settings")]
        [SerializeField] private float maxLinearSpeed = 2f;
        [SerializeField] private float maxAngularSpeed = 2f;
        [SerializeField] private float acceleration = 2f;
        [SerializeField] private float deceleration = 3f;
        
        [Header("Keyboard Keys")]
        [SerializeField] private KeyCode forwardKey = KeyCode.W;
        [SerializeField] private KeyCode backwardKey = KeyCode.S;
        [SerializeField] private KeyCode leftKey = KeyCode.A;
        [SerializeField] private KeyCode rightKey = KeyCode.D;
        [SerializeField] private KeyCode stopKey = KeyCode.Space;
        
        [Header("ROS Settings")]
        [SerializeField] private bool autoDetectPrefix = true;
        [SerializeField] private string customPrefix = "";
        [SerializeField] private string cmdVelTopic = "/cmd_vel";
        
        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;
        
        private Robot _robot;
        private EnvROS _envROS;
        private float _targetLinearSpeed;
        private float _targetAngularSpeed;
        private float _currentLinearSpeed;
        private float _currentAngularSpeed;
        private bool _isRosSubscribed;
        private string _fullCmdVelTopic;
        
        // Mode de contrôle
        public enum ControlMode
        {
            Keyboard,   // Contrôle clavier uniquement
            ROS,        // Contrôle ROS uniquement
            Hybrid      // Les deux (ROS prioritaire si message reçu)
        }
        
        public ControlMode CurrentMode => controlMode;
        
        #region Unity Lifecycle
        
        private void Start()
        {
            Initialize();
        }
        
        private void Update()
        {
            // Gestion du changement de mode
            if (Input.GetKeyDown(toggleModeKey))
            {
                ToggleControlMode();
            }
            
            // Contrôle clavier
            if (controlMode == ControlMode.Keyboard || controlMode == ControlMode.Hybrid)
            {
                HandleKeyboardInput();
            }
        }
        
        private void FixedUpdate()
        {
            // Appliquer le mouvement
            ApplyMovement();
        }
        
        private void OnDestroy()
        {
            // Nettoyer la subscription ROS
            if (_envROS != null && _isRosSubscribed)
            {
                // Note: ROSConnection n'a pas de Unsubscribe, on laisse juste
                _isRosSubscribed = false;
            }
        }
        
        #endregion
        
        #region Initialization
        
        private void Initialize()
        {
            // Récupérer le composant Robot
            _robot = GetComponent<Robot>();
            if (_robot == null)
            {
                Debug.LogError($"[{name}] Robot component not found!");
                enabled = false;
                return;
            }
            
            // Récupérer EnvROS pour les commandes ROS
            _envROS = FindObjectOfType<EnvROS>();
            
            if (_envROS != null && (controlMode == ControlMode.ROS || controlMode == ControlMode.Hybrid))
            {
                SubscribeToROS();
            }
            else if (controlMode == ControlMode.ROS || controlMode == ControlMode.Hybrid)
            {
                Debug.LogWarning($"[{name}] EnvROS not found! ROS control will not work.");
            }
            
            if (showDebugInfo)
            {
                Debug.Log($"[{name}] Initialized with mode: {controlMode}");
            }
        }
        
        private void SubscribeToROS()
        {
            if (_envROS == null) return;
            
            // Détecter le préfixe
            string prefix = "";
            if (autoDetectPrefix && _envROS != null)
            {
                prefix = _envROS.Prefix;
            }
            else if (!string.IsNullOrEmpty(customPrefix))
            {
                prefix = customPrefix;
            }
            
            _fullCmdVelTopic = string.IsNullOrEmpty(prefix) ? cmdVelTopic : $"/{prefix}{cmdVelTopic}";
            
            // S'abonner au topic cmd_vel
            _envROS.RegisterSubscriber<RosMessageTypes.Geometry.TwistMsg>(_fullCmdVelTopic, OnRosCommandReceived);
            _isRosSubscribed = true;
            
            if (showDebugInfo)
            {
                Debug.Log($"[{name}] Subscribed to ROS topic: {_fullCmdVelTopic}");
            }
        }
        
        #endregion
        
        #region Keyboard Control
        
        private void HandleKeyboardInput()
        {
            float linear = 0f;
            float angular = 0f;
            
            // Mouvement avant/arrière
            if (Input.GetKey(forwardKey))
                linear = maxLinearSpeed;
            else if (Input.GetKey(backwardKey))
                linear = -maxLinearSpeed;
            
            // Rotation gauche/droite
            if (Input.GetKey(leftKey))
                angular = -maxAngularSpeed;
            else if (Input.GetKey(rightKey))
                angular = maxAngularSpeed;
            
            // Stop immédiat
            if (Input.GetKeyDown(stopKey))
            {
                linear = 0f;
                angular = 0f;
                _currentLinearSpeed = 0f;
                _currentAngularSpeed = 0f;
            }
            
            // Mettre à jour les cibles
            _targetLinearSpeed = Mathf.Clamp(linear, -maxLinearSpeed, maxLinearSpeed);
            _targetAngularSpeed = Mathf.Clamp(angular, -maxAngularSpeed, maxAngularSpeed);
            
            // En mode hybrid, on n'écrase que si pas de commande ROS récente
            // (la commande ROS gère ses propres cibles)
        }
        
        private void ApplyMovement()
        {
            // Accélération/décélération progressive
            _currentLinearSpeed = Mathf.MoveTowards(
                _currentLinearSpeed,
                _targetLinearSpeed,
                (_targetLinearSpeed > _currentLinearSpeed ? acceleration : deceleration) * Time.fixedDeltaTime
            );
            
            _currentAngularSpeed = Mathf.MoveTowards(
                _currentAngularSpeed,
                _targetAngularSpeed,
                (_targetAngularSpeed > _currentAngularSpeed ? acceleration : deceleration) * Time.fixedDeltaTime
            );
            
            // Appliquer au robot
            _robot.SetVelocity(_currentLinearSpeed, _currentAngularSpeed);
        }
        
        #endregion
        
        #region ROS Control
        
        private float _lastRosCommandTime;
        private float _rosCommandTimeout = 0.5f; // Timeout en secondes
        
        private void OnRosCommandReceived(RosMessageTypes.Geometry.TwistMsg msg)
        {
            if (msg == null) return;
            
            _lastRosCommandTime = Time.time;
            
            if (controlMode == ControlMode.ROS)
            {
                // Mode ROS uniquement
                _targetLinearSpeed = (float)msg.linear.x;
                _targetAngularSpeed = -(float)msg.angular.z;
            }
            else if (controlMode == ControlMode.Hybrid)
            {
                // Mode hybride : ROS prend le dessus si commande récente
                _targetLinearSpeed = (float)msg.linear.x;
                _targetAngularSpeed = -(float)msg.angular.z;
            }
            
            if (showDebugInfo && Time.frameCount % 60 == 0)
            {
                Debug.Log($"[{name}] ROS command: linear={_targetLinearSpeed:F2}, angular={_targetAngularSpeed:F2}");
            }
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Change le mode de contrôle
        /// </summary>
        public void SetControlMode(ControlMode newMode)
        {
            controlMode = newMode;
            
            // Arrêter le robot lors du changement
            _targetLinearSpeed = 0f;
            _targetAngularSpeed = 0f;
            _currentLinearSpeed = 0f;
            _currentAngularSpeed = 0f;
            
            // Réinitialiser le timeout ROS
            _lastRosCommandTime = 0;
            
            if (showDebugInfo)
            {
                Debug.Log($"[{name}] Control mode changed to: {controlMode}");
            }
        }
        
        /// <summary>
        /// Alterne entre les modes
        /// </summary>
        public void ToggleControlMode()
        {
            switch (controlMode)
            {
                case ControlMode.Keyboard:
                    SetControlMode(ControlMode.ROS);
                    break;
                case ControlMode.ROS:
                    SetControlMode(ControlMode.Hybrid);
                    break;
                case ControlMode.Hybrid:
                    SetControlMode(ControlMode.Keyboard);
                    break;
            }
        }
        
        /// <summary>
        /// Force un arrêt d'urgence du robot
        /// </summary>
        public void EmergencyStop()
        {
            _targetLinearSpeed = 0f;
            _targetAngularSpeed = 0f;
            _currentLinearSpeed = 0f;
            _currentAngularSpeed = 0f;
            
            if (showDebugInfo)
            {
                Debug.Log($"[{name}] Emergency stop activated!");
            }
        }
        
        /// <summary>
        /// Envoie une commande de vitesse manuellement
        /// </summary>
        public void SendVelocityCommand(float linear, float angular)
        {
            _targetLinearSpeed = Mathf.Clamp(linear, -maxLinearSpeed, maxLinearSpeed);
            _targetAngularSpeed = Mathf.Clamp(angular, -maxAngularSpeed, maxAngularSpeed);
        }
        
        #endregion
        
        #region UI Helpers
        
        /// <summary>
        /// Retourne une chaîne descriptive du mode actuel
        /// </summary>
        public string GetModeString()
        {
            switch (controlMode)
            {
                case ControlMode.Keyboard:
                    return "KEYBOARD";
                case ControlMode.ROS:
                    return "ROS";
                case ControlMode.Hybrid:
                    return "HYBRID (ROS優先)";
                default:
                    return "UNKNOWN";
            }
        }
        
        /// <summary>
        /// Retourne les commandes actuelles pour l'affichage
        /// </summary>
        public Vector2 GetCurrentCommands()
        {
            return new Vector2(_targetLinearSpeed, _targetAngularSpeed);
        }
        
        #endregion
        
        #region Debug GUI
        
        private void OnGUI()
        {
            if (!showDebugInfo) return;
            
            // Position de l'affichage
            float x = 10;
            float y = 50;
            float width = 250;
            float height = 100;
            
            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.white;
            style.fontSize = 14;
            style.fontStyle = FontStyle.Bold;
            
            // Fond semi-transparent
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(new Rect(x - 5, y - 5, width + 10, height + 10), Texture2D.whiteTexture);
            GUI.color = Color.white;
            
            // Texte d'information
            GUILayout.BeginArea(new Rect(x, y, width, height));
            GUILayout.Label($"Robot Control: {GetModeString()}", style);
            GUILayout.Label($"Linear: {_targetLinearSpeed:F2} m/s", style);
            GUILayout.Label($"Angular: {_targetAngularSpeed:F2} rad/s", style);
            GUILayout.Label($"Press '{toggleModeKey}' to change mode", style);
            GUILayout.EndArea();
        }
        
        #endregion
        
        #region Editor Utilities
        
        [ContextMenu("Switch to Keyboard Mode")]
        private void EditorSwitchToKeyboard()
        {
            SetControlMode(ControlMode.Keyboard);
        }
        
        [ContextMenu("Switch to ROS Mode")]
        private void EditorSwitchToROS()
        {
            SetControlMode(ControlMode.ROS);
        }
        
        [ContextMenu("Switch to Hybrid Mode")]
        private void EditorSwitchToHybrid()
        {
            SetControlMode(ControlMode.Hybrid);
        }
        
        [ContextMenu("Emergency Stop")]
        private void EditorEmergencyStop()
        {
            EmergencyStop();
        }
        
        #endregion
    }
}