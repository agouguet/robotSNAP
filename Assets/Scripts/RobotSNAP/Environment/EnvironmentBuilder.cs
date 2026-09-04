// // Scripts/RobotSNAP/Environment/GridEnvironmentBuilder.cs
// using UnityEngine;
// using System.Collections.Generic;
// using Unity.AI.Navigation;

// namespace RobotSNAP.Environment
// {
//     /// <summary>
//     /// Construit un environnement (sol + murs) à partir d'une texture d'occupation et d'une bounding box.
//     /// </summary>
//     public class GridEnvironmentBuilder : MonoBehaviour
//     {
//         [Header("Materials")]
//         [SerializeField] private Material wallMaterial;
//         [SerializeField] private Material floorMaterial;

//         [Header("Settings")]
//         [SerializeField] private float wallHeight = 2f;
//         [SerializeField] private int noWalkableArea = -1;

//         private GameObject _floor;
//         private GameObject _walls;

//         private void Awake()
//         {
//             if (noWalkableArea == -1)
//                 noWalkableArea = UnityEngine.AI.NavMesh.GetAreaFromName("Not Walkable");
//         }

//         /// <summary>
//         /// Construit l'environnement à partir de la texture d'occupation.
//         /// </summary>
//         /// <param name="occupancyTexture">Texture en niveaux de gris (blanc = libre, noir = mur)</param>
//         /// <param name="worldBounds">Bounding box réelle (en mètres) qui correspond à la texture</param>
//         public void BuildFromTexture(Texture2D occupancyTexture, Bounds worldBounds)
//         {
//             Clear();

//             int width = occupancyTexture.width;
//             int height = occupancyTexture.height;
//             float resolutionX = worldBounds.size.x / width;
//             float resolutionZ = worldBounds.size.z / height;
//             float resolution = (resolutionX + resolutionZ) / 2f;

//             // Convertir la texture en grille (true = mur)
//             bool[,] grid = new bool[width, height];
//             for (int y = 0; y < height; y++)
//             {
//                 for (int x = 0; x < width; x++)
//                 {
//                     Color pixel = occupancyTexture.GetPixel(x, y);
//                     grid[x, y] = pixel.grayscale < 0.5f;
//                 }
//             }

//             // Générer sol et murs
//             GenerateFloor(width, height, resolution, worldBounds, occupancyTexture);
//             GenerateWallsOptimized(grid, width, height, resolution, worldBounds);
//         }

//         private void GenerateFloor(int width, int height, float resolution, Bounds worldBounds, Texture2D texture)
//         {
//             _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
//             _floor.name = "Floor";
//             _floor.transform.parent = transform;
//             // _floor.transform.position = new Vector3(worldBounds.center.x, -0.05f, worldBounds.center.z);
//             _floor.transform.position = new Vector3(_floor.transform.parent.position.x, -0.05f, _floor.transform.parent.position.z);
//             _floor.transform.localScale = new Vector3(worldBounds.size.x, 0.1f, worldBounds.size.z);
//             _floor.layer = LayerMask.NameToLayer("Floor");
//             _floor.tag = "Floor";

//             var renderer = _floor.GetComponent<Renderer>();
//             renderer.material = floorMaterial;
//             renderer.material.mainTexture = texture;
//         }

//         private void GenerateWallsOptimized(bool[,] grid, int width, int height, float resolution, Bounds worldBounds)
//         {
//             var combineInstances = new List<CombineInstance>();
//             bool[,] visited = new bool[width, height];
//             Vector3 offset = worldBounds.min;

//             for (int y = 0; y < height; y++)
//             {
//                 for (int x = 0; x < width; x++)
//                 {
//                     if (grid[x, y] && !visited[x, y])
//                     {
//                         int maxX = x;
//                         while (maxX < width && grid[maxX, y]) maxX++;
//                         int maxY = y;
//                         while (maxY < height && CheckRow(grid, x, maxX, maxY)) maxY++;

//                         for (int i = x; i < maxX; i++)
//                             for (int j = y; j < maxY; j++)
//                                 visited[i, j] = true;

//                         float centerX = offset.x + (x + maxX) * resolution / 2f;
//                         float centerZ = offset.z + (y + maxY) * resolution / 2f;
//                         float widthM = (maxX - x) * resolution;
//                         float depthM = (maxY - y) * resolution;

//                         // GameObject tempWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
//                         // tempWall.transform.position = new Vector3(centerX, wallHeight / 2f, centerZ);
//                         // tempWall.transform.localScale = new Vector3(widthM, wallHeight, depthM);
//                         GameObject tempWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
//                         tempWall.transform.position = new Vector3(-(x + maxX) * resolution / 2, wallHeight / 2, -(y + maxY) * resolution / 2);
//                         tempWall.transform.localScale = new Vector3((maxX - x) * resolution, wallHeight, (maxY - y) * resolution);

//                         combineInstances.Add(new CombineInstance
//                         {
//                             mesh = tempWall.GetComponent<MeshFilter>().mesh,
//                             transform = tempWall.transform.localToWorldMatrix
//                         });
//                         Destroy(tempWall);
//                     }
//                 }
//             }

//             _walls = new GameObject("Walls");
//             _walls.transform.parent = transform;
//             _walls.transform.position = new Vector3(_walls.transform.parent.position.x + width * resolution / 2, 0, _walls.transform.parent.position.z + height * resolution / 2);
//             _walls.layer = LayerMask.NameToLayer("Wall");
//             _walls.tag = "Wall";

//             var finalMesh = new Mesh();
//             finalMesh.CombineMeshes(combineInstances.ToArray());
//             _walls.AddComponent<MeshFilter>().sharedMesh = finalMesh;
//             _walls.AddComponent<MeshRenderer>().material = wallMaterial;
//             var collider = _walls.AddComponent<MeshCollider>();
//             collider.sharedMesh = finalMesh;

//             var navMeshModifier = _walls.AddComponent<NavMeshModifier>();
//             navMeshModifier.overrideArea = true;
//             navMeshModifier.area = noWalkableArea;
//         }

//         private bool CheckRow(bool[,] grid, int startX, int endX, int y)
//         {
//             for (int x = startX; x < endX; x++)
//                 if (!grid[x, y]) return false;
//             return true;
//         }

//         private void Clear()
//         {
//             if (_floor != null) Destroy(_floor);
//             if (_walls != null) Destroy(_walls);
//         }
//     }
// }



























































// Scripts/RobotSNAP/Environment/EnvironmentBuilder.cs
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine.SceneManagement;
using RobotSNAP.Core.Scenario;

namespace RobotSNAP.Environment
{
    public class EnvironmentBuilder : MonoBehaviour
    {
        [Header("Materials (for image-based maps)")]
        [SerializeField] private Material wallMaterial;
        [SerializeField] private Material floorMaterial;

        [Header("Settings")]
        [SerializeField] private float wallHeight = 2f;
        [SerializeField] private int noWalkableArea = -1;

        private GameObject _currentEnvironmentInstance;
        private GameObject _floor;
        private GameObject _walls;
        private string _loadedSceneName; // pour les Scenes

        private ScenarioLoader _loader;

        private void Awake()
        {
            if (noWalkableArea == -1)
                noWalkableArea = UnityEngine.AI.NavMesh.GetAreaFromName("Not Walkable");

            _loader = GetComponent<ScenarioLoader>();
            if (_loader == null)
                _loader = FindObjectOfType<ScenarioLoader>();
        }

        /// <summary>
        /// Construit l'environnement. Le type est détecté automatiquement.
        /// </summary>
        public IEnumerator BuildEnvironment(string mapName)
        {
            // 1. Nettoyer l'ancien
            yield return StartCoroutine(ClearEnvironmentCoroutine());

            if (string.IsNullOrEmpty(mapName)) yield break;

            if (_loader == null)
                _loader = FindObjectOfType<ScenarioLoader>();

            var asset = _loader.LoadMap(mapName);

            switch (asset.Kind)
            {
                case MapAssetKind.Image:
                    BuildFromTexture(asset.Texture, asset.Bounds);
                    yield break;

                case MapAssetKind.Prefab:
                    BuildFromPrefab(asset.Prefab);
                    yield break;

                case MapAssetKind.Scene:
                    yield return StartCoroutine(LoadSceneAdditive(asset.SceneName));
                    break;

                default:
                    Debug.LogError($"[EnvironmentBuilder] Failed to load map: {mapName}");
                    break;
            }
        }

        private IEnumerator ClearEnvironmentCoroutine()
        {
            // Nettoyer l'ancien prefab ou l'ancienne image
            if (_currentEnvironmentInstance != null) Destroy(_currentEnvironmentInstance);
            if (_floor != null) Destroy(_floor);
            if (_walls != null) Destroy(_walls);
            _currentEnvironmentInstance = null;
            _floor = null;
            _walls = null;

            // Nettoyer l'ancienne scene additive
            if (!string.IsNullOrEmpty(_loadedSceneName))
            {
                Scene scene = SceneManager.GetSceneByName(_loadedSceneName);
                if (scene.isLoaded)
                {
                    yield return SceneManager.UnloadSceneAsync(scene);
                }
                _loadedSceneName = null;
            }

            yield return null;
        }

        private IEnumerator LoadSceneAdditive(string sceneName)
        {
            // Vérifier si la scene est déjà chargée (éviter les doublons)
            Scene existingScene = SceneManager.GetSceneByName(sceneName);
            if (existingScene.isLoaded)
            {
                Debug.LogWarning($"[EnvironmentBuilder] Scene {sceneName} is already loaded.");
                _loadedSceneName = sceneName;
                yield break;
            }

            // Charger la scene en additif
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            while (!asyncLoad.isDone)
                yield return null;

            // Récupérer la scene chargée
            Scene loadedScene = SceneManager.GetSceneByName(sceneName);
            if (loadedScene.isLoaded)
            {
                _loadedSceneName = sceneName;
                // Optionnel : déplacer la scene sous le parent de ce builder pour organiser la hiérarchie ?
                // SceneManager.MoveGameObjectToScene(loadedScene.GetRootGameObjects()[0], this.gameObject.scene);
                Debug.Log($"[EnvironmentBuilder] Scene {sceneName} loaded additively.");
            }
            else
            {
                Debug.LogError($"[EnvironmentBuilder] Failed to load scene: {sceneName}");
            }
        }

        /// <summary>
        /// Construit l'environnement à partir de la texture d'occupation.
        /// </summary>
        /// <param name="occupancyTexture">Texture en niveaux de gris (blanc = libre, noir = mur)</param>
        /// <param name="worldBounds">Bounding box réelle (en mètres) qui correspond à la texture</param>
        public void BuildFromTexture(Texture2D occupancyTexture, Bounds worldBounds)
        {
            Clear();

            int width = occupancyTexture.width;
            int height = occupancyTexture.height;
            float resolutionX = worldBounds.size.x / width;
            float resolutionZ = worldBounds.size.z / height;
            float resolution = (resolutionX + resolutionZ) / 2f;

            // Convertir la texture en grille (true = mur)
            bool[,] grid = new bool[width, height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color pixel = occupancyTexture.GetPixel(x, y);
                    grid[x, y] = pixel.grayscale < 0.5f;
                }
            }

            // Générer sol et murs
            GenerateFloor(width, height, resolution, worldBounds, occupancyTexture);
            GenerateWallsOptimized(grid, width, height, resolution, worldBounds);
        }

        private void GenerateFloor(int width, int height, float resolution, Bounds worldBounds, Texture2D texture)
        {
            _floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floor.name = "Floor";
            _floor.transform.parent = transform;
            // _floor.transform.position = new Vector3(worldBounds.center.x, -0.05f, worldBounds.center.z);
            _floor.transform.position = new Vector3(_floor.transform.parent.position.x, -0.05f, _floor.transform.parent.position.z);
            _floor.transform.localScale = new Vector3(worldBounds.size.x, 0.1f, worldBounds.size.z);
            _floor.layer = LayerMask.NameToLayer("Floor");
            _floor.tag = "Floor";

            var renderer = _floor.GetComponent<Renderer>();
            renderer.material = floorMaterial;
            renderer.material.mainTexture = texture;
        }

        private void GenerateWallsOptimized(bool[,] grid, int width, int height, float resolution, Bounds worldBounds)
        {
            var combineInstances = new List<CombineInstance>();
            bool[,] visited = new bool[width, height];
            Vector3 offset = worldBounds.min;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (grid[x, y] && !visited[x, y])
                    {
                        int maxX = x;
                        while (maxX < width && grid[maxX, y]) maxX++;
                        int maxY = y;
                        while (maxY < height && CheckRow(grid, x, maxX, maxY)) maxY++;

                        for (int i = x; i < maxX; i++)
                            for (int j = y; j < maxY; j++)
                                visited[i, j] = true;

                        float centerX = offset.x + (x + maxX) * resolution / 2f;
                        float centerZ = offset.z + (y + maxY) * resolution / 2f;
                        float widthM = (maxX - x) * resolution;
                        float depthM = (maxY - y) * resolution;

                        // GameObject tempWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        // tempWall.transform.position = new Vector3(centerX, wallHeight / 2f, centerZ);
                        // tempWall.transform.localScale = new Vector3(widthM, wallHeight, depthM);
                        GameObject tempWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        tempWall.transform.position = new Vector3(-(x + maxX) * resolution / 2, wallHeight / 2, -(y + maxY) * resolution / 2);
                        tempWall.transform.localScale = new Vector3((maxX - x) * resolution, wallHeight, (maxY - y) * resolution);

                        combineInstances.Add(new CombineInstance
                        {
                            mesh = tempWall.GetComponent<MeshFilter>().mesh,
                            transform = tempWall.transform.localToWorldMatrix
                        });
                        Destroy(tempWall);
                    }
                }
            }

            _walls = new GameObject("Walls");
            _walls.transform.parent = transform;
            _walls.transform.position = new Vector3(_walls.transform.parent.position.x + width * resolution / 2, 0, _walls.transform.parent.position.z + height * resolution / 2);
            _walls.layer = LayerMask.NameToLayer("Obstacle");
            _walls.tag = "Wall";

            var finalMesh = new Mesh();
            finalMesh.CombineMeshes(combineInstances.ToArray());
            _walls.AddComponent<MeshFilter>().sharedMesh = finalMesh;
            _walls.AddComponent<MeshRenderer>().material = wallMaterial;
            var collider = _walls.AddComponent<MeshCollider>();
            collider.sharedMesh = finalMesh;

            var navMeshModifier = _walls.AddComponent<NavMeshModifier>();
            navMeshModifier.overrideArea = true;
            navMeshModifier.area = noWalkableArea;
        }

        private bool CheckRow(bool[,] grid, int startX, int endX, int y)
        {
            for (int x = startX; x < endX; x++)
                if (!grid[x, y]) return false;
            return true;
        }

        

        // --- Construction Prefab (nouveau) ---
        private void BuildFromPrefab(GameObject prefab)
        {
            _currentEnvironmentInstance = Instantiate(prefab, transform);
            _currentEnvironmentInstance.name = "Map_Prefab";

            // Optionnel : si vous voulez calculer les bounds globales du prefab pour les spawns,
            // vous pouvez le faire ici et les stocker dans une variable publique.
            // Exemple : Bounds totalBounds = CalculateBounds(_currentEnvironmentInstance);
        }

        // (Les méthodes GenerateFloor, GenerateWallsOptimized, CheckRow restent telles quelles)

        private void Clear()
        {
            if (_floor != null) Destroy(_floor);
            if (_walls != null) Destroy(_walls);
        }

        private void OnDestroy()
        {
            // Décharger la scène additive si elle est chargée
            if (!string.IsNullOrEmpty(_loadedSceneName))
            {
                Scene scene = SceneManager.GetSceneByName(_loadedSceneName);
                if (scene.isLoaded)
                {
                    SceneManager.UnloadSceneAsync(scene);
                    Debug.Log($"[EnvironmentBuilder] Unloaded additive scene: {_loadedSceneName}");
                }
                _loadedSceneName = null;
            }
        }
    }
}