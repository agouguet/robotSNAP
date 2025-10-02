using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using System.Collections;
using UnityEngine.AI;
using Unity.AI.Navigation;

namespace ROS_DRL
{
    public abstract class EnvCreator : MonoBehaviour
    {
        [HideInInspector]
        public float resolution;
        [HideInInspector]
        public Texture2D texture;

        protected GameObject floor;

        protected int noWalkableArea;

        public bool isEnvironmentReady = false;

        public ScenarioCase scenario;

        public RobotTask robotTask;

        public bool hasValidTask =>
            robotTask != null && robotTask.IsValid;

        public bool hasValidScenario =>
            scenario != null;

        protected void Start()
        {
            noWalkableArea = UnityEngine.AI.NavMesh.GetAreaFromName("Not Walkable");
        }

        public abstract IEnumerator CreateEnvironment();

        public float GetFloorRadius()
        {
            if (floor == null) { return 0.0f; }
            if (floor.TryGetComponent(out BoxCollider box))
                return Mathf.Min(box.size.x * floor.transform.localScale.x, box.size.z * floor.transform.localScale.z) * 0.5f;

            if (floor.TryGetComponent(out MeshRenderer rend))
                return Mathf.Min(rend.bounds.size.x, rend.bounds.size.z) * 0.5f;

            // Default fallback (ex: Unity Plane)
            return Mathf.Min(floor.transform.localScale.x, floor.transform.localScale.z) * 5f;
        }

        public bool ArePointsConnected(Vector3 start, Vector3 end)
        {
            return false;
        }

        protected void Clear()
        {
            if (floor != null)
                Destroy(floor);
            Resources.UnloadUnusedAssets();
            GC.Collect();
        }

        protected void OnDestroy()
        {
            Clear();
        }

    }
}
