using UnityEngine;
using RobotSNAP.Core;
using RobotSNAP.ROS;
using RobotSNAP.Agents;

namespace RobotSNAP
{
    public class RobotInputController : MonoBehaviour
    {
        [Header("Control Mode")]
        [SerializeField] private ControlMode controlMode = ControlMode.Keyboard;
        [SerializeField] private KeyCode toggleModeKey = KeyCode.M;

        [Header("Keyboard Settings (Arrow Keys)")]
        [SerializeField] private float maxLinearSpeed = 2f;
        [SerializeField] private float maxAngularSpeed = 2f;
        [SerializeField] private KeyCode forwardKey = KeyCode.UpArrow;
        [SerializeField] private KeyCode backwardKey = KeyCode.DownArrow;
        [SerializeField] private KeyCode leftKey = KeyCode.LeftArrow;
        [SerializeField] private KeyCode rightKey = KeyCode.RightArrow;
        [SerializeField] private KeyCode stopKey = KeyCode.Space;

        [Header("ROS Settings")]
        [SerializeField] private bool autoDetectPrefix = true;
        [SerializeField] private string customPrefix = "";
        [SerializeField] private string cmdVelTopic = "/cmd_vel";
        [SerializeField] private float rosCommandTimeout = 0.5f;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;
        [SerializeField] private EnvROS _envROS;
        [SerializeField] private Supervisor _supervisor;

        private Robot _robot;
        private bool _isRosSubscribed;
        private string _fullCmdVelTopic;
        private float _lastRosCommandTime;
        private float _targetLinear;
        private float _targetAngular;

        // Détection du changement d'état de pause
        private bool _wasPaused = false;

        public enum ControlMode { Keyboard, ROS, Hybrid, Scenario }
        public ControlMode CurrentMode => controlMode;

        private void Start()
        {
            _robot = GetComponent<Robot>();
            if (_robot == null) { Debug.LogError("Robot component missing"); enabled = false; return; }
            _supervisor ??= Supervisor.Instance;
            _envROS ??= FindFirstObjectByType<EnvROS>();
            if (_envROS != null && (controlMode == ControlMode.ROS || controlMode == ControlMode.Hybrid))
                SubscribeToROS();
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleModeKey)) ToggleControlMode();

            // --- Gestion de la pause ---
            bool isPaused = (_supervisor ??= Supervisor.Instance) != null && _supervisor.IsPaused;

            if (isPaused && !_wasPaused)
            {
                // Entrée en pause : on arrête immédiatement les roues,
                // mais on conserve les valeurs cibles pour la reprise.
                _robot.Stop();
                _wasPaused = true;
            }
            else if (!isPaused && _wasPaused)
            {
                // Sortie de pause : on laisse le prochain FixedUpdate réappliquer la vitesse.
                _wasPaused = false;
            }

            // Ne pas traiter les entrées clavier si en pause
            if (isPaused) return;

            if (controlMode == ControlMode.Scenario) return;
            if (controlMode == ControlMode.Keyboard || controlMode == ControlMode.Hybrid)
                HandleKeyboardInput();
        }

        private void FixedUpdate()
        {
            // Si en pause, on n'envoie aucune commande (les roues sont déjà à l'arrêt)
            if ((_supervisor ??= Supervisor.Instance) != null && _supervisor.IsPaused)
                return;

            if (controlMode == ControlMode.Scenario) return;

            if (controlMode == ControlMode.Hybrid && Time.time - _lastRosCommandTime > rosCommandTimeout)
            {
                if (showDebugInfo && Time.frameCount % 60 == 0)
                    Debug.Log("[RobotInputController] ROS timeout, fallback to keyboard");
                HandleKeyboardInput();
            }

            // Appliquer la vitesse au robot (les valeurs sont conservées)
            _robot.SetVelocity(_targetLinear, _targetAngular);
        }

        private void SubscribeToROS()
        {
            string prefix = "";
            if (autoDetectPrefix && _envROS != null) prefix = _envROS.Prefix;
            else if (!string.IsNullOrEmpty(customPrefix)) prefix = customPrefix;
            _fullCmdVelTopic = string.IsNullOrEmpty(prefix) ? cmdVelTopic : $"/{prefix.TrimStart('/')}{cmdVelTopic}";
            _envROS.RegisterSubscriber<RosMessageTypes.Geometry.TwistMsg>(_fullCmdVelTopic, OnRosCommandReceived);
            _isRosSubscribed = true;
        }

        private void OnRosCommandReceived(RosMessageTypes.Geometry.TwistMsg msg)
        {
            _lastRosCommandTime = Time.time;
            if (controlMode == ControlMode.ROS || controlMode == ControlMode.Hybrid)
            {
                _targetLinear = Mathf.Clamp((float)msg.linear.x, -maxLinearSpeed, maxLinearSpeed);
                _targetAngular = Mathf.Clamp(-(float)msg.angular.z, -maxAngularSpeed, maxAngularSpeed);
            }
        }

        private void HandleKeyboardInput()
        {
            float linear = 0f, angular = 0f;
            if (Input.GetKey(forwardKey)) linear = maxLinearSpeed;
            else if (Input.GetKey(backwardKey)) linear = -maxLinearSpeed;
            if (Input.GetKey(leftKey)) angular = maxAngularSpeed;
            else if (Input.GetKey(rightKey)) angular = -maxAngularSpeed;
            if (Input.GetKeyDown(stopKey)) { linear = 0f; angular = 0f; }
            _targetLinear = Mathf.Clamp(linear, -maxLinearSpeed, maxLinearSpeed);
            _targetAngular = Mathf.Clamp(angular, -maxAngularSpeed, maxAngularSpeed);
        }

        public void SetControlMode(ControlMode newMode)
        {
            controlMode = newMode;
            _targetLinear = 0f;
            _targetAngular = 0f;
            _robot.Stop();
        }

        public void ToggleControlMode() => SetControlMode((ControlMode)(((int)controlMode + 1) % System.Enum.GetValues(typeof(ControlMode)).Length));

        public void EmergencyStop()
        {
            _targetLinear = 0f;
            _targetAngular = 0f;
            _robot.Stop();
        }

        public void SendVelocityCommand(float linear, float angular)
        {
            if (controlMode != ControlMode.Scenario)
            {
                _targetLinear = Mathf.Clamp(linear, -maxLinearSpeed, maxLinearSpeed);
                _targetAngular = Mathf.Clamp(angular, -maxAngularSpeed, maxAngularSpeed);
            }
        }

        public void EnableScenarioMode() => SetControlMode(ControlMode.Scenario);
        public void DisableScenarioMode() => SetControlMode(ControlMode.Keyboard);
        public string GetModeString() => controlMode.ToString();
    }
}
