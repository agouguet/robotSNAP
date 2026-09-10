using UnityEngine;

namespace RobotSNAP
{
    public class RaycastLaserScanner : LaserScanner
    {
        private Ray[] rays;
        private RaycastHit[] raycastHits;
        private Vector3[] directions;
        private LaserScanVisualizer[] laserScanVisualizers;

        [Tooltip("Sélectionnez les layers à détecter par le LIDAR.")]
        public LayerMask detectableLayers;

        [Header("Laser Settings")]
        public int samples = 180;
        public int update_rate = 1800;
        public float angle_min = 0;
        public float angle_max = 6.28f;
        public float angle_increment = 0.0349066f; // approx. 2 deg
        public float time_increment = 0;
        public float scan_time = 0;
        public float range_min = 0.12f;
        public float range_max = 3.5f;
        public float default_value = 0.0f;
        [Header("Laser Height")]
        [Tooltip("Décalage vertical du laser par rapport à l'objet")]
        public float laserHeight = 0.5f;

        [Header("Visualization")]
        public bool gizmo = true;
        public bool visualizerEnabled = false;

        private float[] ranges;
        private float[] intensities;

        public float[] Ranges => ranges;
        public Vector3[] Directions => directions;
        public Vector3 LaserOrigin => transform.position + Vector3.up * laserHeight;

        public void Start()
        {
            Init();
        }

        public void Init()
        {
            // Calcul automatique de l'angle_increment
            if (samples > 1)
                angle_increment = (angle_max - angle_min) / (samples - 1);
            else
                angle_increment = 0f;

            rays = new Ray[samples];
            raycastHits = new RaycastHit[samples];
            directions = new Vector3[samples];
            ranges = new float[samples];
            intensities = new float[samples];

            if (visualizerEnabled)
            {
                laserScanVisualizers = GetComponents<LaserScanVisualizer>();
            }
        }

        public override RosMessageTypes.Sensor.LaserScanMsg InitializeMessage(string FrameId)
        {
            return new RosMessageTypes.Sensor.LaserScanMsg
            {
                header = new RosMessageTypes.Std.HeaderMsg { frame_id = FrameId.TrimStart('/') },
                angle_min = angle_min,
                angle_max = angle_max,
                angle_increment = angle_increment,
                time_increment = time_increment,
                range_min = range_min,
                range_max = range_max,
                ranges = ranges,
                intensities = intensities
            };
        }

        public override float ScanPeriod()
        {
            return (float)samples / update_rate;
        }

        public override float[] Scan()
        {
            MeasureDistance();

            if (visualizerEnabled && laserScanVisualizers != null)
            {
                foreach (LaserScanVisualizer vis in laserScanVisualizers)
                {
                    vis.SetSensorData(transform, directions, ranges, range_min, range_max);
                }
            }

            return ranges;
        }

        private void MeasureDistance()
        {
            float angle;
            Vector3 origin = transform.position + Vector3.up * laserHeight;
            Quaternion rotation = transform.rotation;

            for (int i = 0; i < samples; i++)
            {
                angle = Mathf.Rad2Deg * (angle_min + i * angle_increment);
                Vector3 localDirection = Quaternion.Euler(0, angle, 0) * Vector3.forward;
                Vector3 worldDirection = rotation * localDirection;

                directions[i] = worldDirection;
                rays[i].origin = origin;
                rays[i].direction = worldDirection;

                ranges[i] = Mathf.Infinity; //default_value;

                if (Physics.Raycast(rays[i], out raycastHits[i], range_max, detectableLayers))
                {
                    float dist = raycastHits[i].distance;
                    if (dist >= range_min && dist <= range_max)
                    {
                        ranges[i] = dist;
                    }
                }
            }
        }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!gizmo || rays == null || ranges == null || directions == null)
            return;

        for (int i = 0; i < samples; i++)
        {
            Vector3 origin = transform.position + Vector3.up * laserHeight;
            Vector3 direction = directions[i];

            // t va de 0 (premier laser) à 1 (dernier laser)
            float t = (samples > 1) ? (float)i / (samples - 1) : 0f;

            // Option 1 : dégradé Bleu -> Rouge
            Color rayColor = Color.Lerp(Color.blue, Color.red, t);

            // Option 2 : dégradé HSV (arc-en-ciel) - plus visuel
            // Color rayColor = Color.HSVToRGB(t, 1f, 1f);

            if (ranges[i] > 0f && ranges[i] <= range_max)
            {
                // Rayon qui touche : couleur pleine
                Gizmos.color = rayColor;
                Gizmos.DrawLine(origin, origin + direction.normalized * ranges[i]);
                Gizmos.DrawSphere(origin + direction.normalized * ranges[i], 0.02f);
            }
            else
            {
                // Rayon qui ne touche rien : même couleur mais transparente
                Gizmos.color = new Color(rayColor.r, rayColor.g, rayColor.b, 0.2f);
                Gizmos.DrawLine(origin, origin + direction.normalized * range_max);
            }
        }
    }
#endif
    }
}
