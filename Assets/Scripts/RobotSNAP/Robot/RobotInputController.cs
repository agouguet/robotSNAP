using UnityEngine;
using RobotSNAP.Core;
using RobotSNAP.ROS;

namespace RobotSNAP
{
    /// <summary>
    /// Contrôleur de robot avec support clavier et ROS.
    /// Délègue le mouvement au composant Robot (qui gère le lissage).
    /// Possède un mode "Scenario" pour ne pas interférer avec la navigation autonome.
    /// </summary>
    public class RobotInputController : MonoBehaviour
    {
        [Header("Control Mode")]
        [SerializeField] private ControlMode controlMode = ControlMode.Keyboard;
        [SerializeField] private KeyCode toggleModeKey = KeyCode.M;
        
        [Header("Keyboard Settings")]
        [SerializeField] private float maxLinearSpeed = 2f;
        [SerializeField] private float maxAngularSpeed = 2f;
        
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
        [SerializeField] private float rosCommandTimeout = 0.5f;
        
        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;
        
        private Robot _robot;
        private EnvROS _envROS;
        private bool _isRosSubscribed;
        private string _fullCmdVelTopic;
        private float _lastRosCommandTime;
        
        // Commandes cibles (brutes, sans lissage)
        private float _targetLinear;
        private float _targetAngular;
        
        public enum ControlMode
        {
            Keyboard,   // Contrôle clavier uniquement
            ROS,        // Contrôle ROS uniquement
            Hybrid,     // ROS prioritaire avec timeout, sinon clavier
            Scenario    // Pas de contrôle externe (robot autonome via SetGoal)
        }
        
        public ControlMode CurrentMode => controlMode;
        
        #region Unity Lifecycle
        
        private void Start()
        {
            Initialize();
        }
        
        private void Update()
        {
            // Changement de mode
            if (Input.GetKeyDown(toggleModeKey))
                ToggleControlMode();
            
            // Pas de traitement en mode Scenario
            if (controlMode == ControlMode.Scenario)
                return;
            
            // Entrées clavier (pour modes Keyboard et Hybrid)
            if (controlMode == ControlMode.Keyboard || controlMode == ControlMode.Hybrid)
                HandleKeyboardInput();
        }
        
        private void FixedUpdate()
        {
            // Pas de commande en mode Scenario
            if (controlMode == ControlMode.Scenario)
                return;
            
            // En mode Hybrid, gérer le timeout ROS
            if (controlMode == ControlMode.Hybrid && Time.time - _lastRosCommandTime > rosCommandTimeout)
            {
                // Timeout : utiliser le clavier (ou arrêter)
                if (showDebugInfo && Time.frameCount % 60 == 0)
                    Debug.Log("[RobotInputController] ROS timeout, fallback to keyboard");
                HandleKeyboardInput(); // recalcule _targetLinear/Angular
            }
            
            // Envoyer la commande au robot (le robot fera son propre lissage)
            _robot.SetVelocity(_targetLinear, _targetAngular);
        }
        
        private void OnDestroy()
        {
            if (_envROS != null && _isRosSubscribed)
                _isRosSubscribed = false;
        }
        
        #endregion
        
        #region Initialization
        
        private void Initialize()
        {
            _robot = GetComponent<Robot>();
            if (_robot == null)
            {
                Debug.LogError($"[{name}] Robot component not found!");
                enabled = false;
                return;
            }
            
            // Récupérer la configuration du robot (pour les vitesses max, mais on garde nos propres max pour le contrôle)
            // On pourrait les lire depuis _robot, mais on laisse indépendant pour plus de flexibilité.
            
            _envROS = FindObjectOfType<EnvROS>();
            if (_envROS != null && (controlMode == ControlMode.ROS || controlMode == ControlMode.Hybrid))
                SubscribeToROS();
            else if ((controlMode == ControlMode.ROS || controlMode == ControlMode.Hybrid))
                Debug.LogWarning($"[{name}] EnvROS not found! ROS control will not work.");
            
            if (showDebugInfo)
                Debug.Log($"[{name}] Initialized with mode: {controlMode}");
        }
        
        private void SubscribeToROS()
        {
            if (_envROS == null) return;
            
            string prefix = "";
            if (autoDetectPrefix && _envROS != null)
                prefix = _envROS.Prefix;
            else if (!string.IsNullOrEmpty(customPrefix))
                prefix = customPrefix;
            
            // Construire le topic complet (éviter double slash)
            _fullCmdVelTopic = string.IsNullOrEmpty(prefix) ? cmdVelTopic : $"/{prefix.TrimStart('/')}{cmdVelTopic}";
            
            _envROS.RegisterSubscriber<RosMessageTypes.Geometry.TwistMsg>(_fullCmdVelTopic, OnRosCommandReceived);
            _isRosSubscribed = true;
            
            if (showDebugInfo)
                Debug.Log($"[{name}] Subscribed to ROS topic: {_fullCmdVelTopic}");
        }
        
        #endregion
        
        #region Keyboard Control
        
        private void HandleKeyboardInput()
        {
            float linear = 0f;
            float angular = 0f;
            
            if (Input.GetKey(forwardKey))
                linear = maxLinearSpeed;
            else if (Input.GetKey(backwardKey))
                linear = -maxLinearSpeed;
            
            if (Input.GetKey(leftKey))
                angular = -maxAngularSpeed;
            else if (Input.GetKey(rightKey))
                angular = maxAngularSpeed;
            
            // Stop immédiat
            if (Input.GetKeyDown(stopKey))
            {
                linear = 0f;
                angular = 0f;
            }
            
            _targetLinear = Mathf.Clamp(linear, -maxLinearSpeed, maxLinearSpeed);
            _targetAngular = Mathf.Clamp(angular, -maxAngularSpeed, maxAngularSpeed);
        }
        
        #endregion
        
        #region ROS Control
        
        private void OnRosCommandReceived(RosMessageTypes.Geometry.TwistMsg msg)
        {
            if (msg == null) return;
            
            _lastRosCommandTime = Time.time;
            
            if (controlMode == ControlMode.ROS)
            {
                // Mode ROS : appliquer directement
                _targetLinear = Mathf.Clamp((float)msg.linear.x, -maxLinearSpeed, maxLinearSpeed);
                _targetAngular = Mathf.Clamp(-(float)msg.angular.z, -maxAngularSpeed, maxAngularSpeed);
            }
            else if (controlMode == ControlMode.Hybrid)
            {
                // Mode hybride : ROS prend la main
                _targetLinear = Mathf.Clamp((float)msg.linear.x, -maxLinearSpeed, maxLinearSpeed);
                _targetAngular = Mathf.Clamp(-(float)msg.angular.z, -maxAngularSpeed, maxAngularSpeed);
            }
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Change le mode de contrôle.
        /// </summary>
        public void SetControlMode(ControlMode newMode)
        {
            controlMode = newMode;
            // Arrêter le robot lors du changement
            _targetLinear = 0f;
            _targetAngular = 0f;
            _robot.SetVelocity(0f, 0f);
            
            if (showDebugInfo)
                Debug.Log($"[{name}] Control mode changed to: {controlMode}");
        }
        
        /// <summary>
        /// Alterne entre les modes (Keyboard → ROS → Hybrid → Scenario → Keyboard...)
        /// </summary>
        public void ToggleControlMode()
        {
            ControlMode next = (ControlMode)(((int)controlMode + 1) % System.Enum.GetValues(typeof(ControlMode)).Length);
            SetControlMode(next);
        }
        
        /// <summary>
        /// Force un arrêt d'urgence.
        /// </summary>
        public void EmergencyStop()
        {
            _targetLinear = 0f;
            _targetAngular = 0f;
            _robot.SetVelocity(0f, 0f);
            if (showDebugInfo)
                Debug.Log($"[{name}] Emergency stop!");
        }
        
        /// <summary>
        /// Envoie une commande de vitesse manuellement (ignorée en mode Scenario).
        /// </summary>
        public void SendVelocityCommand(float linear, float angular)
        {
            if (controlMode == ControlMode.Scenario) return;
            _targetLinear = Mathf.Clamp(linear, -maxLinearSpeed, maxLinearSpeed);
            _targetAngular = Mathf.Clamp(angular, -maxAngularSpeed, maxAngularSpeed);
        }
        
        /// <summary>
        /// Passe automatiquement en mode Scenario (utilisé par ScenarioApplier).
        /// </summary>
        public void EnableScenarioMode()
        {
            SetControlMode(ControlMode.Scenario);
        }
        
        /// <summary>
        /// Réactive le contrôle (dernier mode non-Scenario ou Keyboard par défaut).
        /// </summary>
        public void DisableScenarioMode()
        {
            // Retour au mode clavier par défaut, ou on pourrait stocker le mode précédent
            SetControlMode(ControlMode.Keyboard);
        }
        
        #endregion
        
        #region UI Helpers
        
        public string GetModeString()
        {
            switch (controlMode)
            {
                case ControlMode.Keyboard: return "KEYBOARD";
                case ControlMode.ROS: return "ROS";
                case ControlMode.Hybrid: return "HYBRID";
                case ControlMode.Scenario: return "SCENARIO (autonome)";
                default: return "UNKNOWN";
            }
        }
        
        public Vector2 GetCurrentCommands() => new Vector2(_targetLinear, _targetAngular);
        
        #endregion
        
        #region Debug GUI
        
        private void OnGUI()
        {
            if (!showDebugInfo) return;
            
            float x = 10;
            float y = 50;
            float width = 280;
            float height = 120;
            
            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.white;
            style.fontSize = 14;
            style.fontStyle = FontStyle.Bold;
            
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(new Rect(x - 5, y - 5, width + 10, height + 10), Texture2D.whiteTexture);
            GUI.color = Color.white;
            
            GUILayout.BeginArea(new Rect(x, y, width, height));
            GUILayout.Label($"Robot Control: {GetModeString()}", style);
            GUILayout.Label($"Linear target: {_targetLinear:F2} m/s", style);
            GUILayout.Label($"Angular target: {_targetAngular:F2} rad/s", style);
            GUILayout.Label($"Press '{toggleModeKey}' to change mode", style);
            GUILayout.EndArea();
        }
        
        #endregion
        
        #region Editor Utilities
        
        [ContextMenu("Switch to Keyboard Mode")]
        private void EditorSwitchToKeyboard() => SetControlMode(ControlMode.Keyboard);
        
        [ContextMenu("Switch to ROS Mode")]
        private void EditorSwitchToROS() => SetControlMode(ControlMode.ROS);
        
        [ContextMenu("Switch to Hybrid Mode")]
        private void EditorSwitchToHybrid() => SetControlMode(ControlMode.Hybrid);
        
        [ContextMenu("Switch to Scenario Mode")]
        private void EditorSwitchToScenario() => SetControlMode(ControlMode.Scenario);
        
        [ContextMenu("Emergency Stop")]
        private void EditorEmergencyStop() => EmergencyStop();
        
        #endregion
    }
}