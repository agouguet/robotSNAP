using UnityEngine;

namespace RobotSNAP
{
    public class SmoothFollow : MonoBehaviour
    {
        private Camera camera;
        public int displayIndex = 1;

        [Header("Suivi du joueur")]
        public Transform target;
        public float smoothTime = 0.3f;
        private Vector3 velocity = Vector3.zero;

        [Header("Zoom caméra")]
        public float zoomSpeed = 5f;         // Vitesse du zoom
        public float zoomStep = 1f;          // Zoom par touche
        public float minZoom = 3f;
        public float maxZoom = 15f;
        private float targetZoom;            // Le zoom visé (orthographicSize)


        void Start()
        {
            camera = GetComponent<Camera>();
            // EnvController foundScript = Utils.FindScriptInParents<EnvController>(transform);
            // if (foundScript != null) { displayIndex = foundScript.env_id; }
            // camera.targetDisplay = displayIndex;
            targetZoom = camera.orthographicSize;
        }

        void LateUpdate()
        {
            FollowTarget();
            HandleZoom();
        }

        void FollowTarget()
        {
            if (target != null)
            {
                Vector3 targetPosition = new Vector3(target.position.x, transform.position.y, target.position.z);
                transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref velocity, smoothTime);
            }
        }

        void HandleZoom()
        {
            // Molette souris
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0f)
            {
                targetZoom -= scroll * zoomSpeed;
            }

            // Touches + et -
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
            {
                targetZoom -= zoomStep;
            }
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
            {
                targetZoom += zoomStep;
            }

            // Clamp du zoom et interpolation fluide
            targetZoom = Mathf.Clamp(targetZoom, minZoom, maxZoom);
            camera.orthographicSize = Mathf.Lerp(camera.orthographicSize, targetZoom, Time.deltaTime * zoomSpeed);
        }
    }
}