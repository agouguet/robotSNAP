using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using System.Collections.Generic;

namespace RobotSNAP.Core
{
    /// <summary>
    /// Visualiseur générique pour NavMeshSurface - Assignez manuellement la surface dans l'inspecteur
    /// </summary>
    public class NavMeshSurfaceVisualizer : MonoBehaviour
    {
        [Header("NavMesh Surface (à assigner manuellement)")]
        [SerializeField] private NavMeshSurface targetSurface;
        
        [Header("Visual Settings")]
        [SerializeField] private Color surfaceColor = new Color(0, 1, 0, 0.4f);
        [SerializeField] private float yOffset = 0f; // Décalage vertical pour éviter la superposition
        [SerializeField] private bool showWireframe = false;
        [SerializeField] private Color wireframeColor = Color.white;
        
        [Header("Runtime")]
        [SerializeField] private KeyCode toggleKey = KeyCode.None;
        [SerializeField] private bool visibleByDefault = true;
        [SerializeField] private bool autoUpdateOnBuild = true;
        
        [Header("Performance")]
        [SerializeField] private float updateDelay = 0.5f;
        
        private GameObject visualObject;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private bool isVisible;
        private float lastUpdateTime;
        private bool needsUpdate = true;
        
        private void Start()
        {
            if (targetSurface == null)
            {
                Debug.LogError($"NavMeshSurfaceVisualizer sur {gameObject.name} : Aucune surface assignée !");
                enabled = false;
                return;
            }
            
            isVisible = visibleByDefault;
            CreateVisualObject();
            
            if (autoUpdateOnBuild)
            {
                TrySubscribeToBuildEvent();
            }
            
            if (isVisible)
            {
                UpdateVisualization();
            }
        }
        
        private void Update()
        {
            if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
            {
                ToggleVisualization();
            }
            
            if (needsUpdate && Time.time - lastUpdateTime >= updateDelay)
            {
                UpdateVisualization();
                lastUpdateTime = Time.time;
                needsUpdate = false;
            }
        }
        
        private void OnDestroy()
        {
            TryUnsubscribeFromBuildEvent();
            if (visualObject != null)
                Destroy(visualObject);
        }
        
        private void TrySubscribeToBuildEvent()
        {
            try
            {
                EventBus.Instance.Subscribe<NavMeshBuiltEvent>(OnNavMeshBuilt);
            }
            catch
            {
                // Pas de EventBus disponible
            }
        }
        
        private void TryUnsubscribeFromBuildEvent()
        {
            try
            {
                EventBus.Instance.Unsubscribe<NavMeshBuiltEvent>(OnNavMeshBuilt);
            }
            catch { }
        }
        
        private void OnNavMeshBuilt(NavMeshBuiltEvent evt)
        {
            if (evt.success)
            {
                needsUpdate = true;
            }
        }
        
        private void CreateVisualObject()
        {
            visualObject = new GameObject($"NavMeshVisualizer_{targetSurface.name}");
            visualObject.transform.SetParent(transform);
            visualObject.transform.localPosition = Vector3.zero + Vector3.up * yOffset;
            visualObject.transform.localRotation = Quaternion.identity;
            
            meshFilter = visualObject.AddComponent<MeshFilter>();
            meshRenderer = visualObject.AddComponent<MeshRenderer>();
            
            // Créer un matériel transparent
            Shader transparentShader = Shader.Find("Unlit/NavMeshVisualizer");
            if (transparentShader == null) transparentShader = Shader.Find("Transparent/Diffuse");
            
            Material mat = new Material(transparentShader);
            mat.color = surfaceColor;
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            
            meshRenderer.material = mat;
            visualObject.SetActive(isVisible);
        }
        
        public void UpdateVisualization()
        {
            if (targetSurface == null || targetSurface.navMeshData == null)
            {
                if (visualObject != null && visualObject.activeSelf)
                    visualObject.SetActive(false);
                return;
            }
            
            Mesh mesh = ExtractMeshFromSurface();
            
            if (mesh != null && mesh.vertexCount > 0)
            {
                meshFilter.mesh = mesh;
                visualObject.SetActive(isVisible);
                Debug.Log($"Visualisation mise à jour pour {targetSurface.name} : {mesh.vertexCount} vertices");
            }
            else
            {
                visualObject.SetActive(false);
                Debug.LogWarning($"Aucun mesh extrait pour {targetSurface.name}");
            }
        }
        
        private Mesh ExtractMeshFromSurface()
        {
            Mesh mesh = new Mesh();
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            
            // Obtenir la triangulation globale
            var triangulation = NavMesh.CalculateTriangulation();
            
            if (triangulation.vertices == null || triangulation.vertices.Length == 0)
                return null;
            
            // Obtenir les bounds de cette surface dans l'espace monde
            Bounds surfaceBounds = targetSurface.navMeshData.sourceBounds;
            surfaceBounds.center += targetSurface.transform.position;
            
            // Ajouter une petite marge
            surfaceBounds.Expand(0.3f);
            
            // Filtrer les triangles qui appartiennent à cette surface
            for (int i = 0; i < triangulation.indices.Length; i += 3)
            {
                int idx1 = triangulation.indices[i];
                int idx2 = triangulation.indices[i + 1];
                int idx3 = triangulation.indices[i + 2];
                
                Vector3 v1 = triangulation.vertices[idx1];
                Vector3 v2 = triangulation.vertices[idx2];
                Vector3 v3 = triangulation.vertices[idx3];
                
                // Vérifier si le triangle est dans les bounds de la surface
                if (surfaceBounds.Contains(v1) || surfaceBounds.Contains(v2) || surfaceBounds.Contains(v3))
                {
                    // Ajouter les vertices (en les transformant dans l'espace local du visualObject)
                    Vector3 localV1 = visualObject.transform.InverseTransformPoint(v1);
                    Vector3 localV2 = visualObject.transform.InverseTransformPoint(v2);
                    Vector3 localV3 = visualObject.transform.InverseTransformPoint(v3);
                    
                    int baseIndex = vertices.Count;
                    vertices.Add(localV1);
                    vertices.Add(localV2);
                    vertices.Add(localV3);
                    
                    triangles.Add(baseIndex);
                    triangles.Add(baseIndex + 1);
                    triangles.Add(baseIndex + 2);
                }
            }
            
            if (vertices.Count > 0 && triangles.Count > 0)
            {
                mesh.vertices = vertices.ToArray();
                mesh.triangles = triangles.ToArray();
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
            
            return null;
        }
        
        public void ToggleVisualization()
        {
            isVisible = !isVisible;
            if (visualObject != null)
            {
                visualObject.SetActive(isVisible);
            }
            
            if (isVisible)
            {
                UpdateVisualization();
            }
            
            Debug.Log($"{targetSurface?.name} visualization: {(isVisible ? "ON" : "OFF")}");
        }
        
        public void SetVisibility(bool visible)
        {
            isVisible = visible;
            if (visualObject != null)
            {
                visualObject.SetActive(visible);
            }
            
            if (visible)
            {
                UpdateVisualization();
            }
        }
        
        public void SetSurface(NavMeshSurface newSurface)
        {
            targetSurface = newSurface;
            needsUpdate = true;
        }
        
        public void ForceUpdate()
        {
            needsUpdate = true;
        }
        
        // Méthode pour changer la couleur en runtime
        public void SetColor(Color newColor)
        {
            surfaceColor = newColor;
            if (meshRenderer != null && meshRenderer.material != null)
            {
                meshRenderer.material.color = newColor;
            }
        }
        
        // Méthode pour changer le décalage en runtime
        public void SetYOffset(float offset)
        {
            yOffset = offset;
            if (visualObject != null)
            {
                visualObject.transform.localPosition = Vector3.up * yOffset;
            }
        }
        
        #if UNITY_EDITOR
        private void OnValidate()
        {
            // Quand on change la couleur dans l'éditeur, mettre à jour le matériel
            if (meshRenderer != null && meshRenderer.material != null)
            {
                meshRenderer.material.color = surfaceColor;
            }
        }
        
        private void OnDrawGizmosSelected()
        {
            if (targetSurface == null || targetSurface.navMeshData == null) return;
            
            // Afficher les bounds dans l'éditeur quand sélectionné
            Gizmos.color = surfaceColor;
            Bounds bounds = targetSurface.navMeshData.sourceBounds;
            bounds.center += targetSurface.transform.position;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
        #endif
    }
}