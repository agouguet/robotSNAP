using System.Collections.Generic;
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
        [SerializeField] private string cmdVelTopic = RobotSNAPTopics.CmdVel;
        [SerializeField] private float rosCommandTimeout = 0.5f;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;
        [SerializeField] private EnvROS _envROS;
        [SerializeField] private Supervisor _supervisor;

        private Robot _robot;
        /// <summary>
        /// Every command stream this robot listens to: its own id, plus the legacy `/cmd_vel` for the first
        /// robot, so an old client drives robot 1 and a new one can address any robot by id.
        /// </summary>
        private readonly List<string> _fullCmdVelTopics = new List<string>(2);
        private float _lastRosCommandTime;
        private float _targetLinear;
        private float _targetAngular;
        private bool _rosSubscribed;

        /// <summary>
        /// True while a movement key is held. A keyboard that is doing nothing has nothing to say to the
        /// robot, and it used to say it anyway: it wrote a zero velocity on every physics step, which
        /// cancelled the route the scenario had just given the robot. Nobody noticed while a session was
        /// always driven from outside; the moment a scenario drives several robots, all of them stand still.
        /// </summary>
        private bool _keyboardDriving;

        // Détection du changement d'état de pause
        private bool _wasPaused = false;

        public enum ControlMode { Keyboard, ROS, Hybrid, Scenario }
        public ControlMode CurrentMode => controlMode;

        private void Start()
        {
            _robot = GetComponent<Robot>();
            if (_robot == null) { Debug.LogError("Robot component missing"); enabled = false; return; }
            _supervisor ??= Supervisor.Instance;
            _envROS ??= FindAnyObjectByType<EnvROS>();
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

            // A robot handed to a client belongs to that client, not to the scenario: a session that asked
            // for ROS control keeps it even when a scenario hands the robot a route.
            if (controlMode == ControlMode.ROS && _robot.HasGoal)
                _robot.ClearGoal();

            if (controlMode == ControlMode.Scenario) return;

            if (controlMode == ControlMode.Hybrid && Time.time - _lastRosCommandTime > rosCommandTimeout)
            {
                if (showDebugInfo && Time.frameCount % 60 == 0)
                    Debug.Log("[RobotInputController] ROS timeout, fallback to keyboard");
                HandleKeyboardInput();
            }

            // Keyboard mode with no key held: the scenario route of this robot is what drives it.
            if (controlMode == ControlMode.Keyboard && !_keyboardDriving)
                return;

            // Hybrid mode, client still talking and no key held: the client drives.
            if (controlMode == ControlMode.Hybrid && !_keyboardDriving &&
                Time.time - _lastRosCommandTime <= rosCommandTimeout)
                return;

            // Appliquer la vitesse au robot (les valeurs sont conservées)
            _robot.SetVelocity(_targetLinear, _targetAngular);
        }

        private void SubscribeToROS()
        {
            // The connector appends a callback to its topic state without deduplicating it, so a second
            // subscription would run every velocity command twice.
            if (_rosSubscribed || _envROS == null || !_envROS.IsInitialized)
                return;

            string prefix = "";
            if (autoDetectPrefix && _envROS != null) prefix = _envROS.Prefix;
            else if (!string.IsNullOrEmpty(customPrefix)) prefix = customPrefix;
            // Joined by the topic table. This line used to concatenate the prefix and the name and rely on
            // the leading slash of `/cmd_vel` to separate them, so a topic configured without one came out
            // as `/myenvcmd_vel`.
            _fullCmdVelTopics.Clear();
            _fullCmdVelTopics.AddRange(RobotIdentity.StreamNamesFor(this, cmdVelTopic, prefix));
            foreach (string topic in _fullCmdVelTopics)
                _envROS.RegisterSubscriber<RosMessageTypes.Geometry.TwistMsg>(topic, OnRosCommandReceived);
            _rosSubscribed = true;
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

            // The hand takes this robot away from the route it was given, so the two never steer it at once.
            _keyboardDriving = Mathf.Abs(_targetLinear) > 0.01f || Mathf.Abs(_targetAngular) > 0.01f;
            if (_keyboardDriving)
                _robot.ClearGoal();
        }

        public void SetControlMode(ControlMode newMode)
        {
            controlMode = newMode;
            _targetLinear = 0f;
            _targetAngular = 0f;
            _keyboardDriving = false;

            // A robot handed to a client - or to the keyboard - stops walking the route the scenario gave
            // it: without this the scenario and the driver would steer it at the same time.
            if (newMode != ControlMode.Scenario)
                _robot.ClearGoal();

            // The subscription used to be created once, in Start, and only when the Inspector already said
            // ROS or Hybrid. A session switched over the bridge therefore drove the robot with a topic
            // nobody was listening to, which is the one thing a Python or ROS2 client does first.
            if (newMode == ControlMode.ROS || newMode == ControlMode.Hybrid)
            {
                _envROS ??= FindAnyObjectByType<EnvROS>();
                SubscribeToROS();
            }

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
