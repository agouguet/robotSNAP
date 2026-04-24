// Scripts/RobotSNAP/Environment/EnvironmentCreatorFromDataset.cs
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Unity.AI.Navigation;
using RobotSNAP.Core;

namespace RobotSNAP.Environment
{
    /// <summary>
    /// Créateur d'environnement à partir d'un dataset contenant des images PNG (occupation) et des fichiers JSON (topologie).
    /// Structure attendue : <FolderPath>/png/<name>.png et <FolderPath>/json/<name>.json
    /// Le fichier JSON doit contenir un champ "bbox" avec min et max (ex: "bbox": {"min": [xmin, ymin], "max": [xmax, ymax]}).
    /// </summary>
    public class EnvironmentCreatorFromDataset : EnvironmentCreator
    {
        [Header("Dataset Settings")]
        [Tooltip("Chemin du dossier dataset (doit contenir les sous-dossiers 'png' et 'json')")]
        [SerializeField] private string _folderPath = "Dataset";
        [Tooltip("Facteur de downscale de l'image (réduit le nombre de murs)")]
        [SerializeField] private int _downscaleFactor = 4;
        
        [Header("References")]
        [SerializeField] private Transform _environmentParent;
        
        private bool[,] _grid;
        private string _currentMapName;
        private Bounds _worldBounds; // Bounding box réelle (en mètres) extraite du JSON
        private float _resolution;   // mètres par pixel
        
        public string FolderPath
        {
            get => _folderPath;
            set => _folderPath = value;
        }
        
        public string CurrentMapName => _currentMapName;
        
        protected override void Awake()
        {
            base.Awake();
            Debug.Log($"[EnvCreator] Initialized. Dataset folder: {_folderPath}");
        }
        
        /// <summary>
        /// Charge une map spécifique par son nom (sans extension). Cherche le PNG et le JSON correspondants.
        /// </summary>
        public IEnumerator LoadMap(string mapName)
        {
            IsEnvironmentReady = false;
            Clear();
            yield return null;
            
            _currentMapName = mapName;
            
            // 1. Charger la texture PNG
            Texture2D originalTexture = LoadTexture(mapName);
            if (originalTexture == null)
            {
                Debug.LogError($"[EnvCreator] Failed to load PNG for: {mapName}");
                yield break;
            }
            
            // 2. Charger le JSON pour obtenir la bounding box et la résolution
            MapMetadata metadata = LoadMetadata(mapName);
            if (metadata == null)
            {
                Debug.LogError($"[EnvCreator] Failed to load JSON metadata for: {mapName}");
                Destroy(originalTexture);
                yield break;
            }
            
            // 3. Calculer la résolution originale (m/pixel) à partir de la bounding box et de la taille de l'image
            float originalResolutionX = (metadata.bbox.max[0] - metadata.bbox.min[0]) / originalTexture.width;
            float originalResolutionY = (metadata.bbox.max[1] - metadata.bbox.min[1]) / originalTexture.height;
            _resolution = (originalResolutionX + originalResolutionY) / 2f;
            
            Debug.Log($"[EnvCreator] Original resolution: {_resolution:F4} m/px, bbox: {metadata.bbox}");
            
            // 4. Appliquer le downscale si nécessaire
            Texture2D finalTexture = originalTexture;
            float finalResolution = _resolution;
            if (_downscaleFactor > 1)
            {
                finalTexture = DownscaleTexture(originalTexture, _downscaleFactor);
                finalResolution = _resolution * _downscaleFactor;
                Debug.Log($"[EnvCreator] Downscaled to: {finalTexture.width}x{finalTexture.height}, new resolution: {finalResolution:F4} m/px");
                Destroy(originalTexture);
            }
            
            Texture = finalTexture;
            Resolution = finalResolution;
            
            // 5. Définir les bounds du monde (pour le placement)
            _worldBounds = metadata.bbox.ToBounds();
            
            // 6. Générer l'environnement (sol + murs)
            GenerateEnvironment(finalTexture, finalResolution, metadata.bbox.ToBounds());
            yield return null;
            
            IsEnvironmentReady = true;
            Debug.Log($"[EnvCreator] Environment created from {mapName} - Size: {finalTexture.width}x{finalTexture.height}, Resolution: {finalResolution:F4}");
        }
        
        /// <summary>
        /// Charge une map aléatoire parmi celles disponibles.
        /// </summary>
        public override IEnumerator CreateEnvironment()
        {
            string mapName = GetRandomMapName();
            if (string.IsNullOrEmpty(mapName))
            {
                Debug.LogError("[EnvCreator] No map files found!");
                yield break;
            }
            yield return StartCoroutine(LoadMap(mapName));
        }
        
        #region Loading Methods
        
        private string GetPngFolderPath()
        {
            string basePath = Application.streamingAssetsPath;
            string path = Path.Combine(basePath, _folderPath, "png");
            return path.Replace('\\', '/');
        }
        
        private string GetJsonFolderPath()
        {
            string basePath = Application.streamingAssetsPath;
            string path = Path.Combine(basePath, _folderPath, "json");
            return path.Replace('\\', '/');
        }
        
        private string GetRandomMapName()
        {
            string pngFolder = GetPngFolderPath();
            if (!Directory.Exists(pngFolder))
            {
                Debug.LogError($"[EnvCreator] PNG folder not found: {pngFolder}");
                return null;
            }
            
            string[] files = Directory.GetFiles(pngFolder, "*.png");
            if (files.Length == 0)
            {
                Debug.LogWarning($"[EnvCreator] No PNG files found in: {pngFolder}");
                return null;
            }
            
            int index = UnityEngine.Random.Range(0, files.Length);
            string fileName = Path.GetFileNameWithoutExtension(files[index]);
            Debug.Log($"[EnvCreator] Selected map: {fileName}");
            return fileName;
        }
        
        private Texture2D LoadTexture(string mapName)
        {
            string pngFolder = GetPngFolderPath();
            string path = Path.Combine(pngFolder, mapName + ".png");
            path = path.Replace('\\', '/');
            
            Debug.Log($"[EnvCreator] Loading PNG from: {path}");
            
            if (!File.Exists(path))
            {
                Debug.LogError($"[EnvCreator] PNG file not found: {path}");
                return null;
            }
            
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var texture = new Texture2D(2, 2);
                texture.LoadImage(bytes);
                return texture;
            }
            catch (Exception e)
            {
                Debug.LogError($"[EnvCreator] Failed to load PNG: {e.Message}");
                return null;
            }
        }
        
        private MapMetadata LoadMetadata(string mapName)
        {
            string jsonFolder = GetJsonFolderPath();
            string path = Path.Combine(jsonFolder, mapName + ".json");
            path = path.Replace('\\', '/');
            
            Debug.Log($"[EnvCreator] Loading JSON from: {path}");
            
            if (!File.Exists(path))
            {
                Debug.LogError($"[EnvCreator] JSON file not found: {path}");
                return null;
            }
            
            try
            {
                string jsonContent = File.ReadAllText(path);
                var metadata = JsonConvert.DeserializeObject<MapMetadata>(jsonContent);
                return metadata;
            }
            catch (Exception e)
            {
                Debug.LogError($"[EnvCreator] Failed to parse JSON: {e.Message}");
                return null;
            }
        }
        
        #endregion
        
        #region Environment Generation
        
        private void GenerateEnvironment(Texture2D mazeTexture, float resolution, Bounds worldBounds)
        {
            int width = mazeTexture.width;
            int height = mazeTexture.height;
            _grid = new bool[width, height];
            
            // Convertir la texture en grille (true = mur)
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color pixel = mazeTexture.GetPixel(x, y);
                    // Considérer comme mur si le pixel est sombre (grayscale < 0.5)
                    _grid[x, y] = pixel.grayscale < 0.5f;
                }
            }
            
            Debug.Log($"[EnvCreator] Grid generated: {width}x{height}, Walls: {CountWalls()} cells");
            
            // Générer le sol et les murs en utilisant les bounds réelles
            GenerateFloor(width, height, resolution, worldBounds, mazeTexture);
            GenerateWallsOptimized(width, height, resolution, worldBounds);
        }
        
        private int CountWalls()
        {
            int count = 0;
            for (int x = 0; x < _grid.GetLength(0); x++)
                for (int y = 0; y < _grid.GetLength(1); y++)
                    if (_grid[x, y]) count++;
            return count;
        }
        
        private void GenerateFloor(int width, int height, float resolution, Bounds worldBounds, Texture2D texture)
        {
            Transform parent = _environmentParent ?? transform;
            
            if (floor != null) Destroy(floor);
            
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.parent = parent;
            
            // Calcul du centre du sol : centre de la bounding box
            // Vector3 floorCenter = worldBounds.center;
            Vector3 floorCenter = new Vector3(parent.position.x, -0.05f, parent.position.z);
            // floorCenter.y = -0.05f; // légèrement sous le sol
            floor.transform.position = floorCenter;
            
            // Taille du sol : dimensions de la bounding box
            Vector3 floorSize = worldBounds.size;
            floorSize.y = 0.1f; // épaisseur
            floor.transform.localScale = floorSize;
            
            floor.layer = LayerMask.NameToLayer("Floor");
            floor.tag = "Floor";
            
            var renderer = floor.GetComponent<Renderer>();
            renderer.material = floorMaterial;
            renderer.material.mainTexture = texture;
            // Ajuster le tiling de la texture pour qu'elle couvre tout le sol
            // renderer.material.mainTextureScale = new Vector2(floorSize.x / 10f, floorSize.z / 10f);
        }
        
        private void GenerateWallsOptimized(int width, int height, float resolution, Bounds worldBounds)
        {
            Transform parent = _environmentParent ?? transform;
            var combineInstances = new List<CombineInstance>();
            bool[,] visited = new bool[width, height];
            
            // Décalage pour passer des coordonnées image (pixels) aux coordonnées monde
            Vector3 offset = worldBounds.min;
            offset.y = 0;
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (_grid[x, y] && !visited[x, y])
                    {
                        // Trouver la largeur du rectangle (même ligne)
                        int maxX = x;
                        while (maxX < width && _grid[maxX, y]) maxX++;
                        
                        // Trouver la hauteur du rectangle (même colonne, lignes suivantes)
                        int maxY = y;
                        while (maxY < height && CheckColumn(x, maxX, maxY)) maxY++;
                        
                        // Marquer comme visité
                        for (int i = x; i < maxX; i++)
                            for (int j = y; j < maxY; j++)
                                visited[i, j] = true;
                        
                        // Position et taille en mètres
                        float centerX = offset.x + (x + maxX) * resolution / 2f;
                        float centerZ = offset.z + (y + maxY) * resolution / 2f;
                        float widthM = (maxX - x) * resolution;
                        float depthM = (maxY - y) * resolution;
                        
                        // Créer un cube temporaire pour récupérer son mesh
                        GameObject tempWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        tempWall.transform.position = new Vector3(-(x + maxX) * resolution / 2, wallHeight / 2, -(y + maxY) * resolution / 2);
                        tempWall.transform.localScale = new Vector3((maxX - x) * resolution, wallHeight, (maxY - y) * resolution);
                        // tempWall.transform.position = new Vector3(centerX, wallHeight / 2f, centerZ);
                        // tempWall.transform.localScale = new Vector3(widthM, wallHeight, depthM);
                        
                        var meshFilter = tempWall.GetComponent<MeshFilter>();
                        combineInstances.Add(new CombineInstance
                        {
                            mesh = meshFilter.mesh,
                            transform = tempWall.transform.localToWorldMatrix
                        });
                        
                        Destroy(tempWall);
                    }
                }
            }
            
            // Créer le mesh combiné des murs
            if (walls != null) Destroy(walls);
            
            walls = new GameObject("Walls");
            walls.transform.parent = parent;
            // walls.transform.position = Vector3.zero;
            walls.transform.position = new Vector3(parent.position.x + width * resolution / 2, 0, parent.position.z + height * resolution / 2);
            walls.layer = LayerMask.NameToLayer("Wall");
            walls.tag = "Wall";
            
            var finalMeshFilter = walls.AddComponent<MeshFilter>();
            var finalMesh = new Mesh();
            
            if (combineInstances.Count > 0)
            {
                finalMesh.CombineMeshes(combineInstances.ToArray());
                finalMeshFilter.sharedMesh = finalMesh;
                
                walls.AddComponent<MeshRenderer>().material = wallMaterial;
                
                var meshCollider = walls.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = finalMesh;
                
                var navMeshModifier = walls.AddComponent<NavMeshModifier>();
                navMeshModifier.overrideArea = true;
                navMeshModifier.area = noWalkableArea;
                
                Debug.Log($"[EnvCreator] Generated {combineInstances.Count} combined wall meshes");
            }
        }
        
        private bool CheckColumn(int startX, int endX, int y)
        {
            for (int x = startX; x < endX; x++)
            {
                if (!_grid[x, y]) return false;
            }
            return true;
        }
        
        private Texture2D DownscaleTexture(Texture2D original, int factor)
        {
            if (factor <= 1) return original;
            
            int newWidth = original.width / factor;
            int newHeight = original.height / factor;
            
            RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight, 24);
            RenderTexture.active = rt;
            Graphics.Blit(original, rt);
            
            Texture2D result = new Texture2D(newWidth, newHeight);
            result.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
            result.Apply();
            
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            
            return result;
        }
        
        #endregion
        
        #region Cleanup
        
        protected override void Clear()
        {
            base.Clear();
            _grid = null;
        }
        
        #endregion
        
        // Classes pour la désérialisation du JSON
        [Serializable]
        private class MapMetadata
        {
            public BBox bbox;
            // autres champs optionnels (verts, id, room_category, etc.) non utilisés ici
        }
        
        [Serializable]
        private class BBox
        {
            public float[] min; // [x, y]
            public float[] max; // [x, y]

            public Bounds ToBounds()
            {
                if (min == null || max == null || min.Length < 2 || max.Length < 2)
                {
                    Debug.LogError("[EnvCreator] Invalid bbox data");
                    return new Bounds(Vector3.zero, Vector3.zero);
                }
                Vector3 center = new Vector3((min[0] + max[0]) / 2f, 0, (min[1] + max[1]) / 2f);
                Vector3 size = new Vector3(max[0] - min[0], 0, max[1] - min[1]);
                return new Bounds(center, size);
            }
            
            public override string ToString() => $"[{min[0]},{min[1]}] -> [{max[0]},{max[1]}]";
        }
    }
}