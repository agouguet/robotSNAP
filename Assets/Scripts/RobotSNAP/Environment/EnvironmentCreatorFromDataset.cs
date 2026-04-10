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
    /// Créateur d'environnement à partir d'une image d'occupation (PNG uniquement)
    /// NE charge PLUS les tâches (positions) - c'est maintenant géré par les scénarios YAML
    /// </summary>
    public class EnvironmentCreatorFromDataset : EnvironmentCreator
    {
        [Header("Dataset Settings")]
        [SerializeField] private string _folderPath = "Maps";
        [SerializeField] private float _scale = 1f;
        [SerializeField] private int _downscaleFactor = 4;
        
        [Header("References")]
        [SerializeField] private Transform _environmentParent;
        
        private bool[,] _grid;
        private string _currentMapName;
        
        public string FolderPath
        {
            get => _folderPath;
            set
            {
                _folderPath = value;
            }
        }
        
        public string CurrentMapName => _currentMapName;
        
        protected override void Awake()
        {
            base.Awake();
            Debug.Log($"[EnvCreator] Initialized. Map folder: {_folderPath}");
        }
        
        /// <summary>
        /// Charge une map spécifique par son nom
        /// </summary>
        public IEnumerator LoadMap(string mapName)
        {
            IsEnvironmentReady = false;
            Clear();
            yield return null;
            
            _currentMapName = mapName;
            
            // Charger la texture
            Texture = LoadTexture(mapName);
            if (Texture == null)
            {
                Debug.LogError($"[EnvCreator] Failed to load PNG for: {mapName}");
                yield break;
            }
            
            Debug.Log($"[EnvCreator] Loaded texture: {Texture.width}x{Texture.height}");
            
            // Calculer la résolution
            Resolution = CalculateResolution(Texture);
            
            // Downscale si nécessaire
            if (_downscaleFactor > 1)
            {
                Texture = DownscaleTexture(Texture, _downscaleFactor);
                Resolution *= _downscaleFactor;
                Debug.Log($"[EnvCreator] Downscaled to: {Texture.width}x{Texture.height}, Resolution: {Resolution:F3}");
            }
            
            // Générer l'environnement
            GenerateEnvironment(Texture, Resolution);
            yield return null;
            
            IsEnvironmentReady = true;
            
            Debug.Log($"[EnvCreator] Environment created from {mapName} - Size: {Texture.width}x{Texture.height}, Resolution: {Resolution:F3}");
        }
        
        /// <summary>
        /// Charge une map aléatoire
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
        
        private string GetMapFolderPath()
        {
            string basePath = Application.streamingAssetsPath;
            string path = Path.Combine(basePath, _folderPath);
            return path.Replace('\\', '/');
        }
        
        private string GetRandomMapName()
        {
            string folderPath = GetMapFolderPath();
            
            if (!Directory.Exists(folderPath))
            {
                Debug.LogError($"[EnvCreator] Directory not found: {folderPath}");
                return null;
            }
            
            string[] files = Directory.GetFiles(folderPath, "*.png");
            if (files.Length == 0)
            {
                Debug.LogWarning($"[EnvCreator] No PNG files found in: {folderPath}");
                return null;
            }
            
            int index = UnityEngine.Random.Range(0, files.Length);
            string fileName = Path.GetFileNameWithoutExtension(files[index]);
            Debug.Log($"[EnvCreator] Selected map: {fileName}");
            return fileName;
        }
        
        private Texture2D LoadTexture(string mapName)
        {
            string folderPath = GetMapFolderPath();
            string path = Path.Combine(folderPath, mapName + ".png");
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
        
        #endregion
        
        #region Environment Generation
        
        private float CalculateResolution(Texture2D texture)
        {
            // Valeur par défaut - peut être ajustée
            return 0.1f;
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
            Transform parent = _environmentParent ?? transform;
            
            if (floor != null) Destroy(floor);
            
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.parent = parent;
            floor.transform.localPosition = new Vector3(width * resolution / 2f, -0.05f, height * resolution / 2f);
            floor.transform.localScale = new Vector3(width * resolution, 0.1f, height * resolution);
            floor.layer = LayerMask.NameToLayer("Floor");
            floor.tag = "Floor";
            
            var renderer = floor.GetComponent<Renderer>();
            renderer.material = floorMaterial;
            renderer.material.mainTexture = texture;
        }
        
        private void GenerateWallsOptimized(int width, int height, float resolution)
        {
            Transform parent = _environmentParent ?? transform;
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
                            (x + maxX) * resolution / 2f,
                            wallHeight / 2f,
                            (y + maxY) * resolution / 2f
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
        
        private bool CheckRow(int startX, int endX, int y)
        {
            for (int x = startX; x < endX; x++)
            {
                if (!_grid[x, y]) return false;
            }
            return true;
        }
        
        #endregion
    }
}