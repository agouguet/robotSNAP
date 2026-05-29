using UnityEngine;

namespace RobotSNAP
{
    [RequireComponent(typeof(ArticulationBody))]
    public class FreightController : MonoBehaviour
    {
        [Header("Wheels")]
        [SerializeField] private ArticulationBody leftWheel;
        [SerializeField] private ArticulationBody rightWheel;
        [SerializeField] private float wheelRadius = 0.1f;
        
        [Header("Movement")]
        [SerializeField] private float maxLinearSpeed = 1.2f;
        [SerializeField] private float maxAngularSpeed = 2.0f;
        [SerializeField] private float acceleration = 2.0f;
        
        [Header("PID for speed control (optional)")]
        [SerializeField] private float kp = 100f;
        [SerializeField] private float ki = 0f;
        [SerializeField] private float kd = 10f;
        
        private ArticulationBody baseArticulation;
        private float targetLeftVel, targetRightVel;
        private float currentLeftVel, currentRightVel;
        
        private void Start()
        {
            baseArticulation = GetComponent<ArticulationBody>();
            if (leftWheel == null || rightWheel == null)
            {
                Debug.LogError("Wheels not assigned!");
                enabled = false;
            }
        }
        
        private void FixedUpdate()
        {
            // Appliquer les vitesses cibles aux roues avec PID (optionnel)
            Debug.Log($"Applying velocities - Target Left: {targetLeftVel:F2}, Target Right: {targetRightVel:F2}");
            ApplyWheelVelocity(leftWheel, targetLeftVel);
            ApplyWheelVelocity(rightWheel, targetRightVel);
            
            // Lire les vitesses réelles (pour l'odométrie)
            currentLeftVel = GetWheelVelocity(leftWheel);
            currentRightVel = GetWheelVelocity(rightWheel);
        }
        
        /// <summary>
        /// Calcule les vitesses de roue à partir d'une commande (linéaire, angulaire)
        /// Modèle différentiel : v_left = (linear - angular * L/2) / radius
        ///                       v_right = (linear + angular * L/2) / radius
        /// L = distance entre roues (track width)
        /// </summary>
        public void SetVelocity(float linear, float angular, float trackWidth = 0.6f)
        {
            linear = Mathf.Clamp(linear, -maxLinearSpeed, maxLinearSpeed);
            angular = Mathf.Clamp(angular, -maxAngularSpeed, maxAngularSpeed);
            
            float leftRadPerSec = (linear - angular * trackWidth / 2f) / wheelRadius;
            float rightRadPerSec = (linear + angular * trackWidth / 2f) / wheelRadius;
            
            targetLeftVel = leftRadPerSec;
            targetRightVel = rightRadPerSec;
        }
        
        private void ApplyWheelVelocity(ArticulationBody wheel, float targetVelocity)
        {
            var drive = wheel.xDrive;
            drive.targetVelocity = targetVelocity;
            drive.stiffness = 500f;
            drive.damping = 10f;
            drive.forceLimit = 1000f;
            wheel.xDrive = drive;
        }
        
        private float GetWheelVelocity(ArticulationBody wheel)
        {
            return wheel.jointVelocity[0]; // vitesse angulaire autour de l'axe principal
        }
        
        // Optionnel : arrêt d'urgence
        public void Stop() => SetVelocity(0, 0);

        public Vector3 GetVelocity()
        {
            // Récupère la vitesse linéaire du corps principal (ArticulationBody racine)
            return GetComponent<ArticulationBody>().linearVelocity;
        }

        public float CurrentLinearSpeed => GetVelocity().magnitude;

        public float CurrentAngularSpeed => GetComponent<ArticulationBody>().angularVelocity.magnitude;
    }
}