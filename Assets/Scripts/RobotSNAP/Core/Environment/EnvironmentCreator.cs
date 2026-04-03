using UnityEngine;
using System.Collections;
using RobotSNAP.Core;

namespace RobotSNAP.Environment
{
    /// <summary>
    /// Classe abstraite pour la création d'environnements
    /// </summary>
    public abstract class EnvironmentCreator : MonoBehaviour
    {
        [Header("Environment Settings")]
        [SerializeField] protected Material wallMaterial;
        [SerializeField] protected Material floorMaterial;
        [SerializeField] protected float wallHeight = 2f;
        
        [Header("NavMesh Settings")]
        [SerializeField] protected int noWalkableArea = -1;
        
        protected GameObject floor;
        protected GameObject walls;
        
        public bool IsEnvironmentReady { get; protected set; }
        public float Resolution { get; protected set; }
        public Texture2D Texture { get; protected set; }
        
        public ScenarioData Scenario { get; protected set; }
        public RobotTaskData RobotTask { get; protected set; }
        
        public bool HasValidTask => RobotTask != null && RobotTask.IsValid;
        public bool HasValidScenario => Scenario != null;
        
        protected virtual void Awake()
        {
            noWalkableArea = UnityEngine.AI.NavMesh.GetAreaFromName("Not Walkable");
            if (noWalkableArea == -1) noWalkableArea = 0;
        }
        
        public abstract IEnumerator CreateEnvironment();
        
        public float GetFloorRadius()
        {
            if (floor == null) return 0f;
            
            if (floor.TryGetComponent(out BoxCollider box))
                return Mathf.Min(box.size.x * floor.transform.localScale.x, 
                                box.size.z * floor.transform.localScale.z) * 0.5f;
            
            if (floor.TryGetComponent(out MeshRenderer rend))
                return Mathf.Min(rend.bounds.size.x, rend.bounds.size.z) * 0.5f;
            
            return Mathf.Min(floor.transform.localScale.x, floor.transform.localScale.z) * 5f;
        }
        
        protected virtual void Clear()
        {
            if (floor != null) Destroy(floor);
            if (walls != null) Destroy(walls);
            if (Texture != null) Destroy(Texture);
            
            Scenario = null;
            RobotTask = null;
            
            Resources.UnloadUnusedAssets();
            System.GC.Collect();
        }
        
        protected virtual void OnDestroy()
        {
            Clear();
        }
    }
}