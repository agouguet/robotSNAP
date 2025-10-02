using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using System.Collections;
using Unity.AI.Navigation;

namespace ROS_DRL
{
    public class EnvCreatorFromDataset : EnvCreator
    {

        [SerializeField]
        private string _folderPath;

        public string folderPath
        {
            get => _folderPath;
            set
            {
                _folderPath = Path.Combine(Application.streamingAssetsPath, value.Replace("Assets/", ""));;
                UpdatePaths();
            }
        }
        private string dirJsonPath;
        private string dirMapPath;
        private string dirTaskPath;

        public float scale = 1.0f;
        public float wallHeight = 2.0f;
        public float searchRadius = 20f;

        public Material wallMaterial;
        public Material floorMaterial;

        private bool[,] grid;
        GameObject finalWalls;

        void Start()
        {
            base.Start();
            UpdatePaths();
        }

        private void UpdatePaths()
        {
            dirJsonPath = folderPath + "/json/";
            dirMapPath  = folderPath + "/png/";
            dirTaskPath = folderPath + "/task/";
        }

        public override IEnumerator CreateEnvironment()
        {
            isEnvironmentReady = false;

            Clear();
            yield return null;

            string name = GetRandomJsonFileName();
            MapJson mapJson = GetMapJsonFromName(name);
            if (mapJson == null) { yield break; }
            mapJson.ConvertVerts();
            yield return null;


            texture = GetImageFromName(name);

            int width = texture.width;
            int height = texture.height;
            resolution = GetResolution(texture, mapJson);
            int factor = 4;


            Texture2D oldTexture = texture;
            texture = DownscaleTexture(texture, factor);
            Destroy(oldTexture);
            resolution *= factor;
            resolution *= scale;


            GenerateMaze(texture, resolution);
            yield return null;

            Scenario s = GetScenario(name);
            if (s != null)
            {
                scenario = s.GetRandomCase();
                robotTask = scenario.GetRandomRobotTask();
            }
                
            isEnvironmentReady = true;
        }

        public MapJson GetMapJsonFromName(string name)
        {
            string path = Path.Combine(dirJsonPath, name + ".json");
            if (!File.Exists(path))
            {
                return null;
            }
            string jsonContent = File.ReadAllText(path);
            MapJson map = JsonConvert.DeserializeObject<MapJson>(jsonContent);
            return map;
        }

        public string GetRandomJsonFileName()
        {
            if (Directory.Exists(dirJsonPath))
            {
                string[] files = Directory.GetFiles(dirJsonPath, "*.json");
                if (files.Length > 0)
                {
                    System.Random random = new System.Random();
                    int index = random.Next(0, files.Length); 
                    return Path.GetFileNameWithoutExtension(files[index]); 
                }
            }
            return null;
        }

        public Scenario GetScenario(string name)
        {
            string path = Path.Combine(dirTaskPath, name + ".json");

            if (!File.Exists(path))
            {
                Debug.LogError($"Fichier de tâche non trouvé : {path}");
                return null;
            }

            string jsonContent = File.ReadAllText(path);
            Scenario scenario = JsonConvert.DeserializeObject<Scenario>(jsonContent);
            return scenario;
        }

        public Texture2D GetImageFromName(string name)
        {
            Texture2D texture = new Texture2D(2, 2);
            texture.LoadImage(File.ReadAllBytes(dirMapPath + name + ".png")); // Load image
            return texture;
        }

        public float GetResolution(Texture2D texture, MapJson jsonData)
        {
            int width = texture.width;
            int height = texture.height;
            float jsonWidth = jsonData.bbox.Max.x - jsonData.bbox.Min.x;
            float jsonHeight = jsonData.bbox.Max.y - jsonData.bbox.Min.y;
            return (jsonWidth / width + jsonHeight / height) / 2;
        }

        public static Texture2D DownscaleTexture(Texture2D original, int factor)
        {
            if (factor <= 0)
            {
                Debug.LogError("Factor must be greater than 0");
                return original;
            }

            int newWidth = original.width / factor;
            int newHeight = original.height / factor;

            RenderTexture rt = new RenderTexture(newWidth, newHeight, 24);
            RenderTexture.active = rt;
            Graphics.Blit(original, rt);

            Texture2D result = new Texture2D(newWidth, newHeight);
            result.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
            result.Apply();

            RenderTexture.active = null;
            rt.Release();
            Destroy(rt);

            return result;
        }


        void GenerateMaze(Texture2D mazeTexture, float resolution)
        {
            int width = mazeTexture.width;
            int height = mazeTexture.height;
            grid = new bool[width, height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color pixel = mazeTexture.GetPixel(x, y);
                    grid[x, y] = pixel.grayscale < 0.5f; 
                }
            }

            // if (floor == null)
            GenerateFloor(mazeTexture, width, height, resolution);
            GenerateWallsOptimized(width, height, resolution);
        }

        void GenerateFloor(Texture2D texture, int width, int height, float resolution)
        {
            if (floor != null)
                Destroy(floor);
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(transform.position.x, -0.05f, transform.position.z);
            floor.transform.localScale = new Vector3(width * resolution, 0.1f, height * resolution);
            floor.GetComponent<Renderer>().material = floorMaterial;
            floor.GetComponent<Renderer>().material.mainTexture = texture;
            floor.name = "Floor";
            floor.layer = LayerMask.NameToLayer("Floor");
            floor.tag = "Floor";
            floor.transform.parent = transform;
        }

        void GenerateWallsOptimized(int width, int height, float resolution)
        {
            List<CombineInstance> combineInstances = new List<CombineInstance>();
            MeshFilter meshFilter;

            bool[,] visited = new bool[width, height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (grid[x, y] && !visited[x, y])
                    {
                        int maxX = x;
                        while (maxX < width && grid[maxX, y]) maxX++;

                        int maxY = y;
                        while (maxY < height && CheckRow(x, maxX, maxY)) maxY++;

                        for (int i = x; i < maxX; i++)
                            for (int j = y; j < maxY; j++)
                                visited[i, j] = true;

                        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        wall.transform.position = new Vector3(-(x + maxX) * resolution / 2, wallHeight / 2, -(y + maxY) * resolution / 2);
                        wall.transform.localScale = new Vector3((maxX - x) * resolution, wallHeight, (maxY - y) * resolution);
                        wall.GetComponent<Renderer>().material = wallMaterial;

                        meshFilter = wall.GetComponent<MeshFilter>();
                        CombineInstance combine = new CombineInstance
                        {
                            mesh = meshFilter.mesh,
                            transform = wall.transform.localToWorldMatrix
                        };
                        combineInstances.Add(combine);
                        Destroy(wall);
                    }
                }
            }

            if (finalWalls != null)
                Destroy(finalWalls);

            finalWalls = new GameObject("OptimizedWalls");
            finalWalls.layer = LayerMask.NameToLayer("Wall");
            finalWalls.tag = "Wall";
            finalWalls.transform.parent = transform;
            finalWalls.transform.position = new Vector3(transform.position.x + width * resolution / 2, 0, transform.position.z + height * resolution / 2);

            // Add MeshFilter and Renderer
            MeshFilter finalMeshFilter = finalWalls.AddComponent<MeshFilter>();
            finalWalls.AddComponent<MeshRenderer>().material = wallMaterial;

            // Create combined mesh
            Mesh finalMesh = new Mesh();
            if (combineInstances.Count > 0)
            {
                finalMesh.CombineMeshes(combineInstances.ToArray());
                finalMeshFilter.sharedMesh = finalMesh;

                MeshCollider meshCollider = finalWalls.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = finalMesh;

                NavMeshModifier modifier = finalWalls.AddComponent<NavMeshModifier>();
                modifier.overrideArea = true;
                modifier.area = noWalkableArea;
            }
            else
            {
                Debug.LogWarning("No walls to combine! Check maze texture or threshold logic.");
            }

        }

        bool CheckRow(int startX, int endX, int y)
        {
            for (int x = startX; x < endX; x++)
            {
                if (!grid[x, y]) return false;
            }
            return true;
        }

        void Clear()
        {
            if (scenario != null)
                scenario = null;
            if (robotTask != null)
                robotTask = null;
            if (texture != null)
                Destroy(texture);
            if (finalWalls != null)
            {
                MeshFilter mf = finalWalls.GetComponent<MeshFilter>();
                if (mf != null && mf.mesh != null)
                {
                    Destroy(mf.mesh);
                }
                Destroy(finalWalls);
                finalWalls = null;
            }
            if (floor != null)
                Destroy(floor);
            Resources.UnloadUnusedAssets();
            GC.Collect();
        }

        void OnDestroy()
        {
            Clear();
        }

    }
}
