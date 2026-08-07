// Scripts/RobotSNAP/Core/Scenario/ScenarioLoader.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VYaml.Serialization;
using Newtonsoft.Json;

namespace RobotSNAP.Core.Scenario
{

    public enum MapAssetKind { None, Image, Prefab, Scene }

    public class MapAsset
    {
        public MapAssetKind Kind;
        public Texture2D Texture;
        public GameObject Prefab;
        public string SceneName;  // Nouveau : nom de la scène à charger
        public Bounds Bounds;
    }


    public sealed class ScenarioLoader : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private bool _enableCaching = true;
        [SerializeField] private bool _logEvents = true;
        [SerializeField] private string[] _validExtensions = { ".yaml", ".yml" };
        [SerializeField] private TextAsset _fallbackYamlAsset;

        private Dictionary<string, ScenarioData> _loadedScenarios = new();
        private Dictionary<string, ScenarioInfo> _scenarioInfoCache = new();
        private Dictionary<string, Texture2D> _loadedMaps = new();
        private string _scenariosPath;
        private string _mapsPath;
        private bool _isInitialized;

        public string ScenariosPath => _scenariosPath;
        public string MapsPath => _mapsPath;
        public int CacheSize => _loadedScenarios.Count;

        public event Action<ScenarioData> OnScenarioLoaded;
        public event Action<string> OnScenarioError;
        public event Action<string, Texture2D> OnMapLoaded;
        public event Action OnPathsUpdated;

        private void Awake()
        {
            var supervisor = Supervisor.Instance;
            if (supervisor != null)
            {
                supervisor.OnConfigChanged += OnConfigChanged;
            }
        }

        private void OnDestroy()
        {
            var supervisor = Supervisor.Instance;
            if (supervisor != null)
            {
                supervisor.OnConfigChanged -= OnConfigChanged;
            }
        }

         private void OnConfigChanged(SimulationConfig config)
        {
            Debug.Log("[ScenarioLoader] Config changed, refreshing paths.");
            RefreshPathsFromConfig();
            
            ClearScenarioCache();
            ClearMapCache();
            
            OnPathsUpdated?.Invoke();
        }

        /// <summary>
        /// Met à jour les chemins à partir de la SimulationConfig active.
        /// </summary>
        public void RefreshPathsFromConfig()
        {
            var config = Supervisor.Instance?.ActiveConfig;
            if (config == null)
            {
                Debug.LogError("[ScenarioLoader] No active SimulationConfig found");
                return;
            }

            string scenariosFolder = !string.IsNullOrEmpty(config.ScenariosFolder) ? config.ScenariosFolder : "Scenarios";
            string mapsFolder = !string.IsNullOrEmpty(config.DatasetPath) ? config.DatasetPath : "Dataset";

            _scenariosPath = Path.Combine(Application.streamingAssetsPath, scenariosFolder);
            _mapsPath = Path.Combine(Application.streamingAssetsPath, mapsFolder);
            Debug.Log($"[ScenarioLoader] Scenarios path set to: {_scenariosPath}");

            EnsureDirectoriesExist();
            _isInitialized = true;
        }

        private void EnsureDirectoriesExist()
        {
            if (!Directory.Exists(_scenariosPath))
            {
                Directory.CreateDirectory(_scenariosPath);
                if (_logEvents) Debug.Log($"[ScenarioLoader] Created scenarios directory: {_scenariosPath}");
            }
            if (!Directory.Exists(_mapsPath))
            {
                Directory.CreateDirectory(_mapsPath);
                if (_logEvents) Debug.Log($"[ScenarioLoader] Created maps directory: {_mapsPath}");
            }
        }

        #region Private Helper Methods

        private string FindScenarioFile(string scenarioName)
        {
            foreach (var ext in _validExtensions)
            {
                string path = Path.Combine(_scenariosPath, scenarioName + ext);
                if (File.Exists(path))
                {
                    return path;
                }
            }
            return null;
        }

        private string FindMapFile(string mapName)
        {
            string[] imageExtensions = { ".png", ".jpg", ".jpeg" };
            
            foreach (var ext in imageExtensions)
            {
                string path = Path.Combine(_mapsPath, mapName + ext);
                if (File.Exists(path))
                {
                    return path;
                }
            }
            return null;
        }

        private bool IsSceneExists(string sceneName)
        {
            Debug.Log($"[ScenarioLoader] Checking if scene exists: {sceneName}");
            Debug.Log($"[ScenarioLoader] Scene count in build settings: {SceneManager.sceneCountInBuildSettings}");
            #if UNITY_EDITOR
                foreach (var scene in EditorBuildSettings.scenes)
                {
                    if (scene.enabled)
                    {
                        string name = Path.GetFileNameWithoutExtension(scene.path);
                        if (name == sceneName)
                            return true;
                    }
                }
                return false;
            #else
                for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                {
                    Debug.Log($"[ScenarioLoader] Checking scene index {i} in build settings");
                    string path = SceneUtility.GetScenePathByBuildIndex(i);
                    string name = Path.GetFileNameWithoutExtension(path);
                    if (name == sceneName)
                        return true;
                }
                return false;
            #endif
        }

        private ScenarioData ParseYaml(byte[] yamlBytes, string sourceName)
        {
            try
            {
                var scenario = YamlSerializer.Deserialize<ScenarioData>(yamlBytes);
                
                if (scenario == null)
                {
                    throw new Exception("Deserialization returned null");
                }
                
                if (!scenario.IsValid(out string validationError))
                {
                    throw new Exception($"Validation failed: {validationError}");
                }
                
                return scenario;
            }
            catch (Exception e)
            {
                throw new Exception($"Failed to parse YAML from {sourceName}: {e.Message}");
            }
        }

        private ScenarioInfo ExtractScenarioInfo(byte[] yamlBytes)
        {
            try
            {
                // Pour l'extraction rapide des infos, on désérialise juste la partie scenario_info
                // Note: VYaml ne supporte pas facilement la désérialisation partielle
                // On désérialise donc complètement mais c'est acceptable pour l'info
                var scenario = YamlSerializer.Deserialize<ScenarioData>(yamlBytes);
                return scenario?.Info;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Public API - Loading

        /// <summary>
        /// Charge un scénario par son nom
        /// </summary>
        /// <param name="scenarioName">Nom du scénario (sans extension)</param>
        /// <returns>Le scénario chargé ou null</returns>
        public ScenarioData LoadScenario(string scenarioName)
        {
            if (!_isInitialized)
            {
                OnScenarioError?.Invoke("ScenarioLoader not initialized");
                RefreshPathsFromConfig();
                // return null;
            }
            
            // Vérifier le cache
            if (_enableCaching && _loadedScenarios.TryGetValue(scenarioName, out var cached))
            {
                if (_logEvents) Debug.Log($"[ScenarioLoader] Returning cached scenario: {scenarioName}");
                return cached;
            }
            
            // Trouver le fichier
            string filePath = FindScenarioFile(scenarioName);
            if (filePath == null)
            {
                string error = $"Scenario not found: {scenarioName} in {_scenariosPath}";
                OnScenarioError?.Invoke(error);
                Debug.LogError($"[ScenarioLoader] {error}");
                return null;
            }
            Debug.Log($"[ScenarioLoader] Found scenario file: {filePath}");
            try
            {
                // Lire et parser
                byte[] yamlBytes = File.ReadAllBytes(filePath);
                var scenario = ParseYaml(yamlBytes, filePath);
                
                // Mettre en cache
                if (_enableCaching)
                {
                    _loadedScenarios[scenarioName] = scenario;
                }
                
                // Mettre en cache les infos
                if (scenario.Info != null)
                {
                    _scenarioInfoCache[scenarioName] = scenario.Info;
                }
                
                OnScenarioLoaded?.Invoke(scenario);
                
                if (_logEvents)
                {
                    Debug.Log($"[ScenarioLoader] Loaded scenario: {scenario.Name} v{scenario.Version} from {filePath}");
                }
                
                return scenario;
            }
            catch (Exception e)
            {
                string error = $"Failed to load scenario '{scenarioName}': {e.Message}";
                OnScenarioError?.Invoke(error);
                Debug.LogError($"[ScenarioLoader] {error}");
                return null;
            }
        }
        
        /// <summary>
        /// Charge un scénario depuis un chemin de fichier absolu
        /// </summary>
        /// <param name="filePath">Chemin complet du fichier</param>
        /// <returns>Le scénario chargé ou null</returns>
        public ScenarioData LoadScenarioFromPath(string filePath)
        {
            if (!File.Exists(filePath))
            {
                OnScenarioError?.Invoke($"File not found: {filePath}");
                return null;
            }
            
            string scenarioName = Path.GetFileNameWithoutExtension(filePath);
            
            // Vérifier le cache
            if (_enableCaching && _loadedScenarios.TryGetValue(scenarioName, out var cached))
            {
                return cached;
            }
            
            try
            {
                byte[] yamlBytes = File.ReadAllBytes(filePath);
                var scenario = ParseYaml(yamlBytes, filePath);
                
                if (_enableCaching)
                {
                    _loadedScenarios[scenarioName] = scenario;
                }
                
                OnScenarioLoaded?.Invoke(scenario);
                
                if (_logEvents)
                {
                    Debug.Log($"[ScenarioLoader] Loaded scenario from path: {scenario.Name}");
                }
                
                return scenario;
            }
            catch (Exception e)
            {
                OnScenarioError?.Invoke($"Failed to load from path '{filePath}': {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Charge le scénario depuis un fichier YAML intégré (TextAsset)
        /// </summary>
        /// <param name="asset">TextAsset contenant le YAML</param>
        /// <returns>Le scénario chargé ou null</returns>
        public ScenarioData LoadScenarioFromAsset(TextAsset asset)
        {
            if (asset == null)
            {
                OnScenarioError?.Invoke("Cannot load null TextAsset");
                return null;
            }
            
            try
            {
                byte[] yamlBytes = System.Text.Encoding.UTF8.GetBytes(asset.text);
                var scenario = ParseYaml(yamlBytes, asset.name);
                
                OnScenarioLoaded?.Invoke(scenario);
                
                if (_logEvents)
                {
                    Debug.Log($"[ScenarioLoader] Loaded scenario from asset: {scenario.Name}");
                }
                
                return scenario;
            }
            catch (Exception e)
            {
                OnScenarioError?.Invoke($"Failed to load from asset '{asset.name}': {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Charge le scénario par défaut (fallback)
        /// </summary>
        /// <returns>Le scénario par défaut ou null</returns>
        public ScenarioData LoadFallbackScenario()
        {
            if (_fallbackYamlAsset != null)
            {
                return LoadScenarioFromAsset(_fallbackYamlAsset);
            }
            return null;
        }
        
        /// <summary>
        /// Obtient les informations d'un scénario sans charger toutes les données
        /// </summary>
        /// <param name="scenarioName">Nom du scénario</param>
        /// <returns>Informations du scénario ou null</returns>
        public ScenarioInfo GetScenarioInfo(string scenarioName)
        {
            // Vérifier le cache d'infos
            if (_scenarioInfoCache.TryGetValue(scenarioName, out var cachedInfo))
            {
                return cachedInfo;
            }
            
            // Trouver le fichier
            string filePath = FindScenarioFile(scenarioName);
            if (filePath == null)
            {
                return null;
            }
            
            try
            {
                byte[] yamlBytes = File.ReadAllBytes(filePath);
                var info = ExtractScenarioInfo(yamlBytes);
                
                if (info != null)
                {
                    _scenarioInfoCache[scenarioName] = info;
                }
                
                return info;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Charge une map (texture + bounds) à partir d'un identifiant.
        /// L'identifiant peut être :
        /// - "dataset" (ex: "basic") : une map aléatoire de ce dataset
        /// - "dataset/mapname" (ex: "basic/corner") : la map spécifique
        /// </summary>
        public bool LoadMapData(string mapIdentifier, out Texture2D texture, out Bounds bounds)
        {
            texture = null;
            bounds = new Bounds();

            string basePath = Path.Combine(Application.streamingAssetsPath, MapsPath);
            string mapName = mapIdentifier.Replace('\\', '/').Trim('/');

            string pngPath, jsonPath;

            if (mapName.Contains("/"))
            {
                // Format "dataset/mapname"
                string dataset = mapName.Substring(0, mapName.IndexOf('/'));
                string map = mapName.Substring(mapName.IndexOf('/') + 1);
                pngPath = Path.Combine(basePath, dataset, "png", map + ".png");
                jsonPath = Path.Combine(basePath, dataset, "json", map + ".json");
            }
            else
            {
                // Choisir une map aléatoire dans le dataset
                string dataset = mapName;
                string pngFolder = Path.Combine(basePath, dataset, "png");
                if (!Directory.Exists(pngFolder))
                {
                    Debug.LogError($"[ScenarioLoader] Dataset folder not found: {pngFolder}");
                    return false;
                }
                string[] pngFiles = Directory.GetFiles(pngFolder, "*.png");
                if (pngFiles.Length == 0)
                {
                    Debug.LogError($"[ScenarioLoader] No PNG files in {pngFolder}");
                    return false;
                }
                // Sélection aléatoire
                int index = UnityEngine.Random.Range(0, pngFiles.Length);
                string selected = Path.GetFileNameWithoutExtension(pngFiles[index]);
                pngPath = pngFiles[index];
                jsonPath = Path.Combine(basePath, dataset, "json", selected + ".json");
            }

            if (!File.Exists(pngPath) || !File.Exists(jsonPath))
            {
                Debug.LogError($"[ScenarioLoader] Map files missing: {pngPath} or {jsonPath}");
                return false;
            }

            // Charger texture
            byte[] pngData = File.ReadAllBytes(pngPath);
            texture = new Texture2D(2, 2);
            texture.LoadImage(pngData);

            // Charger JSON
            string json = File.ReadAllText(jsonPath);
            var metadata = JsonConvert.DeserializeObject<MapMetadata>(json);
            if (metadata?.bbox == null)
            {
                Debug.LogError($"[ScenarioLoader] Invalid JSON metadata for {mapIdentifier}");
                return false;
            }

            float[] min = metadata.bbox.min;
            float[] max = metadata.bbox.max;
            Vector3 center = new Vector3((min[0] + max[0]) / 2f, 0, (min[1] + max[1]) / 2f);
            Vector3 size = new Vector3(max[0] - min[0], 0, max[1] - min[1]);
            bounds = new Bounds(center, size);

            return true;
        }

        public GameObject LoadMapPrefab(string path)
        {
            // Si vous utilisez Resources, le chemin est sans extension
            GameObject prefab = Resources.Load<GameObject>(path);
            if (prefab == null)
                Debug.LogError($"[ScenarioLoader] Prefab not found at Resources path: {path}");
            return prefab;
        }

        // Classe interne pour la désérialisation
        [System.Serializable]
        private class MapMetadata
        {
            public BBox bbox;
        }

        [System.Serializable]
        private class BBox
        {
            public float[] min;
            public float[] max;
        }

        #endregion

        #region Public API - Map Loading

        /// <summary>
        /// Charge une image de map
        /// </summary>
        /// <param name="mapName">Nom de la map (sans extension)</param>
        /// <returns>Texture2D de la map ou null</returns>
        /// <summary>
        /// Charge une carte en essayant d'abord comme Texture2D, puis comme GameObject (Prefab).
        /// </summary>
        public MapAsset LoadMap(string mapName)
        {
            // 1. Tenter de charger comme une Scene (additive)
            if (IsSceneExists(mapName))
            {
                return new MapAsset
                {
                    Kind = MapAssetKind.Scene,
                    SceneName = mapName,
                    Texture = null,
                    Prefab = null,
                    Bounds = new Bounds(Vector3.zero, Vector3.one)
                };
            }

            // 2. Tenter de charger comme un Prefab (Resources)
            GameObject prefab = Resources.Load<GameObject>(mapName);
            if (prefab != null)
            {
                return new MapAsset
                {
                    Kind = MapAssetKind.Prefab,
                    Prefab = prefab,
                    Texture = null,
                    SceneName = null,
                    Bounds = new Bounds(Vector3.zero, Vector3.one)
                };
            }

            // 3. Tenter de charger comme une Image (comportement actuel)
            if (LoadMapData(mapName, out Texture2D texture, out Bounds bounds))
            {
                return new MapAsset
                {
                    Kind = MapAssetKind.Image,
                    Texture = texture,
                    Bounds = bounds,
                    Prefab = null,
                    SceneName = null
                };
            }

            Debug.LogError($"[ScenarioLoader] Map resource not found: {mapName}");
            return new MapAsset { Kind = MapAssetKind.None };
        }
        
        /// <summary>
        /// Charge la map associée à un scénario
        /// </summary>
        /// <param name="scenario">Scénario</param>
        /// <returns>Texture2D de la map ou null</returns>
        // public Texture2D LoadMapForScenario(ScenarioData scenario)
        // {
        //     if (scenario == null || string.IsNullOrEmpty(scenario.MapImage))
        //         return null;
            
        //     return LoadMap(scenario.MapImage);
        // }

        #endregion

        #region Public API - Queries

        /// <summary>
        /// Liste tous les scénarios disponibles
        /// </summary>
        /// <returns>Liste des noms de scénarios (sans extension)</returns>
        public List<string> GetAvailableScenarios()
        {
            Debug.LogWarning($"[ScenarioLoader] GetAvailableScenarios called, scenarios path: {_scenariosPath}" + $", exists: {Directory.Exists(_scenariosPath)}");
            if (!Directory.Exists(_scenariosPath))
            {
                return new List<string>();
            }
            
            var scenarios = new List<string>();
            
            foreach (var ext in _validExtensions)
            {
                string[] files = Directory.GetFiles(_scenariosPath, "*" + ext);
                foreach (var file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (!scenarios.Contains(name))
                    {
                        scenarios.Add(name);
                    }
                }
            }

            Debug.LogWarning($"[ScenarioLoader] Found {scenarios.Count} scenarios in {_scenariosPath}");
            
            return scenarios.OrderBy(x => x).ToList();
        }
        
        /// <summary>
        /// Liste toutes les maps disponibles
        /// </summary>
        /// <returns>Liste des noms de maps (sans extension)</returns>
        public List<string> GetAvailableMaps()
        {
            if (!Directory.Exists(_mapsPath))
            {
                return new List<string>();
            }
            
            var maps = new List<string>();
            string[] imageExtensions = { "*.png", "*.jpg", "*.jpeg" };
            
            foreach (var pattern in imageExtensions)
            {
                string[] files = Directory.GetFiles(_mapsPath, pattern);
                foreach (var file in files)
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (!maps.Contains(name))
                    {
                        maps.Add(name);
                    }
                }
            }
            
            return maps.OrderBy(x => x).ToList();
        }
        
        /// <summary>
        /// Vérifie si un scénario existe
        /// </summary>
        public bool ScenarioExists(string scenarioName)
        {
            return FindScenarioFile(scenarioName) != null;
        }
        
        /// <summary>
        /// Vérifie si une map existe
        /// </summary>
        public bool MapExists(string mapName)
        {
            return FindMapFile(mapName) != null;
        }

        #endregion

        #region Public API - Export

        /// <summary>
        /// Exporte un scénario en fichier YAML
        /// </summary>
        /// <param name="scenario">Scénario à exporter</param>
        /// <param name="fileName">Nom du fichier (sans extension)</param>
        /// <returns>True si l'export a réussi</returns>
        public bool ExportScenario(ScenarioData scenario, string fileName)
        {
            if (scenario == null)
            {
                OnScenarioError?.Invoke("Cannot export null scenario");
                return false;
            }
            
            EnsureDirectoriesExist();
            
            string fullPath = Path.Combine(_scenariosPath, fileName);
            if (!fullPath.EndsWith(".yaml") && !fullPath.EndsWith(".yml"))
            {
                fullPath += ".yaml";
            }
            
            try
            {
                // byte[] yamlBytes = YamlSerializer.Serialize(scenario);
                byte[] yamlBytes = YamlSerializer.Serialize(scenario).ToArray();
                // byte[] yamlBytes = YamlSerializer.Serialize(scenario).Span.ToArray();
                File.WriteAllBytes(fullPath, yamlBytes);
                
                if (_logEvents)
                {
                    Debug.Log($"[ScenarioLoader] Exported scenario to: {fullPath}");
                }
                
                return true;
            }
            catch (Exception e)
            {
                OnScenarioError?.Invoke($"Failed to export scenario: {e.Message}");
                return false;
            }
        }
        

        #endregion

        #region Public API - Cache Management

        /// <summary>
        /// Vide le cache des scénarios
        /// </summary>
        public void ClearScenarioCache()
        {
            _loadedScenarios.Clear();
            _scenarioInfoCache.Clear();
            
            if (_logEvents)
            {
                Debug.Log("[ScenarioLoader] Scenario cache cleared");
            }
        }
        
        /// <summary>
        /// Vide le cache des maps
        /// </summary>
        public void ClearMapCache()
        {
            foreach (var texture in _loadedMaps.Values)
            {
                if (texture != null)
                    Destroy(texture);
            }
            _loadedMaps.Clear();
            
            if (_logEvents)
            {
                Debug.Log("[ScenarioLoader] Map cache cleared");
            }
        }
        
        /// <summary>
        /// Vide tous les caches
        /// </summary>
        public void ClearAllCaches()
        {
            ClearScenarioCache();
            ClearMapCache();
        }

        #endregion

        #region Editor Utilities

        #if UNITY_EDITOR
        
        [ContextMenu("Clear All Caches")]
        private void EditorClearCaches() => ClearAllCaches();
        
        [ContextMenu("Log Available Scenarios")]
        private void EditorLogAvailableScenarios()
        {
            var scenarios = GetAvailableScenarios();
            Debug.Log($"[ScenarioLoader] Available scenarios ({scenarios.Count}):\n  {(scenarios.Count > 0 ? string.Join("\n  ", scenarios) : "none")}");
        }
        
        [ContextMenu("Log Available Maps")]
        private void EditorLogAvailableMaps()
        {
            var maps = GetAvailableMaps();
            Debug.Log($"[ScenarioLoader] Available maps ({maps.Count}):\n  {(maps.Count > 0 ? string.Join("\n  ", maps) : "none")}");
        }
        
        #endif

        #endregion

        private Vector3 GetPositionFromRef(ScenarioData scenario, string reference)
        {
            // Debug.Log($"[ScenarioLoader] Resolving position for reference: '{reference}'");
            if (string.IsNullOrEmpty(reference)) return Vector3.zero;
            
            if (scenario.Points != null && scenario.Points.TryGetValue(reference, out var point))
            {
                Debug.Log($"[ScenarioLoader] Found point for reference '{reference}': {point} at {point.ToVector3()}");
                return point.ToVector3();
            }
            
            // Essayer comme ID entier
            if (int.TryParse(reference, out int id) && scenario.Points != null)
            {
                string key = id.ToString();
                if (scenario.Points.TryGetValue(key, out point))
                    return point.ToVector3();
            }
            
            return Vector3.zero;
        }

        /// <summary>
        /// Obtient la position et la rotation à partir d'une référence (point nommé).
        /// </summary>
        public (Vector3 position, Quaternion rotation) GetPositionAndRotation(ScenarioData scenario, string reference)
        {
            if (string.IsNullOrEmpty(reference)) 
                return (Vector3.zero, Quaternion.identity);

            if (scenario.Points != null && scenario.Points.TryGetValue(reference, out var point))
            {
                return (point.ToVector3(), point.Rotation);
            }

            // Fallback : essayer comme ID entier
            if (int.TryParse(reference, out int id) && scenario.Points != null)
            {
                string key = id.ToString();
                if (scenario.Points.TryGetValue(key, out point))
                    return (point.ToVector3(), point.Rotation);
            }

            return (Vector3.zero, Quaternion.identity);
        }

        private Bounds GetBoundsFromRef(ScenarioData scenario, string reference)
        {
            if (string.IsNullOrEmpty(reference)) return new Bounds();
            
            if (scenario.Points != null && scenario.Points.TryGetValue(reference, out var point) && point.IsBounds)
                return point.ToBounds();
            
            return new Bounds();
        }

        /// <summary>
        /// Obtient une position Vector3 à partir d'une référence (point nommé ou zone)
        /// </summary>
        public Vector3 GetPosition(ScenarioData scenario, string reference)
        {
            return GetPositionFromRef(scenario, reference);
        }

        /// <summary>
        /// Obtient une zone Bounds à partir d'une référence (zone nommée)
        /// </summary>
        public Bounds GetBounds(ScenarioData scenario, string reference)
        {
            return GetBoundsFromRef(scenario, reference);
        }
    }
}