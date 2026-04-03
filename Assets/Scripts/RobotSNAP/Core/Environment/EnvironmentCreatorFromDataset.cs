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
    /// Créateur d'environnement à partir de dataset (image + JSON)
    /// </summary>
    public class EnvironmentCreatorFromDataset : EnvironmentCreator
    {
        [Header("Dataset Settings")]
        [SerializeField] private string folderPath = "Dataset";
        [SerializeField] private float scale = 1f;
        [SerializeField] private int downscaleFactor = 4;
        [SerializeField] private float searchRadius = 20f;
        
        [Header("References")]
        [SerializeField] private Transform environmentParent;
        
        private string _jsonPath;
        private string _mapPath;
        private string _taskPath;
        private bool[,] _grid;
        
        public string FolderPath
        {
            get => folderPath;
            set
            {
                folderPath = value;
                UpdatePaths();
            }
        }
        
        protected override void Awake()
        {
            base.Awake();
            UpdatePaths();
            
            Debug.Log($"[EnvCreator] Initialized. Paths: JSON={_jsonPath}, PNG={_mapPath}, TASK={_taskPath}");
        }
        
        private void UpdatePaths()
        {
            // Nettoyer le chemin
            string cleanPath = folderPath.Replace("Assets/", "").Replace("StreamingAssets/", "");
            
            // Construire le chemin complet vers StreamingAssets
            string basePath = Application.streamingAssetsPath;
            
            _jsonPath = Path.Combine(basePath, cleanPath, "json");
            _mapPath = Path.Combine(basePath, cleanPath, "png");
            _taskPath = Path.Combine(basePath, cleanPath, "task");
            
            // Normaliser les séparateurs de chemin pour Windows
            _jsonPath = _jsonPath.Replace('\\', '/');
            _mapPath = _mapPath.Replace('\\', '/');
            _taskPath = _taskPath.Replace('\\', '/');
        }
        
        public override IEnumerator CreateEnvironment()
        {
            IsEnvironmentReady = false;
            Clear();
            yield return null;
            
            // Vérifier que les dossiers existent
            if (!Directory.Exists(_jsonPath))
            {
                Debug.LogError($"[EnvCreator] JSON folder not found: {_jsonPath}");
                yield break;
            }
            
            if (!Directory.Exists(_mapPath))
            {
                Debug.LogError($"[EnvCreator] PNG folder not found: {_mapPath}");
                yield break;
            }
            
            // Charger un fichier aléatoire
            string name = GetRandomJsonFileName();
            if (string.IsNullOrEmpty(name))
            {
                Debug.LogError("[EnvCreator] No JSON files found!");
                yield break;
            }
            
            Debug.Log($"[EnvCreator] Loading environment: {name}");
            
            // Charger les données JSON
            MapJsonData mapJson = GetMapJsonFromName(name);
            if (mapJson == null)
            {
                Debug.LogError($"[EnvCreator] Failed to load JSON for: {name}");
                yield break;
            }
            mapJson.ConvertVerts();
            yield return null;
            
            // Charger la texture
            Texture = GetImageFromName(name);
            if (Texture == null)
            {
                Debug.LogError($"[EnvCreator] Failed to load PNG for: {name}");
                yield break;
            }
            
            Debug.Log($"[EnvCreator] Loaded texture: {Texture.width}x{Texture.height}");
            
            // Calculer la résolution
            Resolution = CalculateResolution(Texture, mapJson) * scale;
            
            // Downscale la texture si nécessaire
            if (downscaleFactor > 1)
            {
                Texture = DownscaleTexture(Texture, downscaleFactor);
                Resolution *= downscaleFactor;
                Debug.Log($"[EnvCreator] Downscaled to: {Texture.width}x{Texture.height}, Resolution: {Resolution:F3}");
            }
            
            // Générer l'environnement
            GenerateEnvironment(Texture, Resolution);
            yield return null;
            
            // Charger le scénario
            Scenario = GetScenario(name);
            if (Scenario != null)
            {
                var scenarioCase = Scenario.GetRandomCase();
                if (scenarioCase != null)
                {
                    RobotTask = scenarioCase.GetRandomRobotTask();
                    if (RobotTask != null)
                    {
                        Debug.Log($"[EnvCreator] Robot task loaded: Start={RobotTask.StartPosition}, End={RobotTask.EndPosition}");
                    }
                }
            }
            
            IsEnvironmentReady = true;
            
            Debug.Log($"[EnvCreator] Environment created from {name} - Size: {Texture.width}x{Texture.height}, Resolution: {Resolution:F3}");
        }
        
        #region Loading Methods
        
        private string GetRandomJsonFileName()
        {
            if (!Directory.Exists(_jsonPath))
            {
                Debug.LogError($"[EnvCreator] Directory not found: {_jsonPath}");
                return null;
            }
            
            string[] files = Directory.GetFiles(_jsonPath, "*.json");
            if (files.Length == 0)
            {
                Debug.LogWarning($"[EnvCreator] No JSON files found in: {_jsonPath}");
                return null;
            }
            
            int index = UnityEngine.Random.Range(0, files.Length);
            string fileName = Path.GetFileNameWithoutExtension(files[index]);
            Debug.Log($"[EnvCreator] Selected file: {fileName}");
            return fileName;
        }
        
        private MapJsonData GetMapJsonFromName(string name)
        {
            string path = Path.Combine(_jsonPath, name + ".json");
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
                return JsonConvert.DeserializeObject<MapJsonData>(jsonContent);
            }
            catch (Exception e)
            {
                Debug.LogError($"[EnvCreator] Failed to parse JSON: {e.Message}");
                return null;
            }
        }
        
        private Texture2D GetImageFromName(string name)
        {
            string path = Path.Combine(_mapPath, name + ".png");
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
        
        private ScenarioData GetScenario(string name)
        {
            string path = Path.Combine(_taskPath, name + ".json");
            path = path.Replace('\\', '/');
            
            Debug.Log($"[EnvCreator] Loading task from: {path}");
            
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[EnvCreator] Task file not found: {path}");
                return null;
            }
            
            try
            {
                string jsonContent = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<ScenarioData>(jsonContent);
            }
            catch (Exception e)
            {
                Debug.LogError($"[EnvCreator] Failed to parse task JSON: {e.Message}");
                return null;
            }
        }
        
        #endregion
        
        #region Environment Generation
        
        private float CalculateResolution(Texture2D texture, MapJsonData mapJson)
        {
            int width = texture.width;
            int height = texture.height;
            float jsonWidth = mapJson.bbox.Max.x - mapJson.bbox.Min.x;
            float jsonHeight = mapJson.bbox.Max.y - mapJson.bbox.Min.y;
            
            return (jsonWidth / width + jsonHeight / height) / 2f;
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
        
        private void GenerateEnvironment(Texture2D mazeTexture, float resolution)
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
                    _grid[x, y] = pixel.grayscale < 0.5f;
                }
            }
            
            Debug.Log($"[EnvCreator] Grid generated: {width}x{height}, Walls: {CountWalls()} cells");
            
            // Générer le sol
            GenerateFloor(width, height, resolution, mazeTexture);
            
            // Générer les murs optimisés
            GenerateWallsOptimized(width, height, resolution);
        }
        
        private int CountWalls()
        {
            int count = 0;
            for (int x = 0; x < _grid.GetLength(0); x++)
                for (int y = 0; y < _grid.GetLength(1); y++)
                    if (_grid[x, y]) count++;
            return count;
        }
        
        private void GenerateFloor(int width, int height, float resolution, Texture2D texture)
        {
            Transform parent = environmentParent ?? transform;
            
            if (floor != null) Destroy(floor);
            
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.parent = parent;
            floor.transform.localPosition = new Vector3(0.0f, -0.05f, 0.0f);
            // floor.transform.localPosition = new Vector3(width * resolution / 2f, -0.05f, height * resolution / 2f);
            floor.transform.localScale = new Vector3(width * resolution, 0.1f, height * resolution);
            floor.layer = LayerMask.NameToLayer("Floor");
            floor.tag = "Floor";
            
            var renderer = floor.GetComponent<Renderer>();
            renderer.material = floorMaterial;
            renderer.material.mainTexture = texture;
        }
        
        private void GenerateWallsOptimized(int width, int height, float resolution)
        {
            Transform parent = environmentParent ?? transform;
            var combineInstances = new List<CombineInstance>();
            bool[,] visited = new bool[width, height];
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (_grid[x, y] && !visited[x, y])
                    {
                        // Trouver la largeur du rectangle
                        int maxX = x;
                        while (maxX < width && _grid[maxX, y]) maxX++;
                        
                        // Trouver la hauteur du rectangle
                        int maxY = y;
                        while (maxY < height && CheckRow(x, maxX, maxY)) maxY++;
                        
                        // Marquer comme visité
                        for (int i = x; i < maxX; i++)
                            for (int j = y; j < maxY; j++)
                                visited[i, j] = true;
                        
                        // Créer un cube temporaire pour le mesh
                        GameObject tempWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        tempWall.transform.localPosition = new Vector3(
                            (x + maxX) * resolution / 2f - width * resolution / 2f,
                            wallHeight / 2f,
                            (y + maxY) * resolution / 2f - height * resolution / 2f
                        );
                        tempWall.transform.localScale = new Vector3(
                            (maxX - x) * resolution,
                            wallHeight,
                            (maxY - y) * resolution
                        );
                        
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
            
            // Créer le mesh combiné
            if (walls != null) Destroy(walls);
            
            walls = new GameObject("Walls");
            walls.transform.parent = parent;
            walls.transform.localPosition = Vector3.zero;
            walls.transform.Rotate(0.0f, 180.0f, 0.0f, Space.Self);
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
            else
            {
                Debug.LogWarning("[EnvCreator] No walls generated!");
            }
        }
        
        private bool CheckRow(int startX, int endX, int y)
        {
            for (int x = startX; x < endX; x++)
            {
                if (!_grid[x, y]) return false;
            }
            return true;
        }
        
        #endregion
        
        #region Cleanup
        
        protected override void Clear()
        {
            base.Clear();
            _grid = null;
        }
        
        #endregion
    }
}