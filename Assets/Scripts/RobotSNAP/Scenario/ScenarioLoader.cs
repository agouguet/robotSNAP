// Scripts/RobotSNAP/Core/Scenario/ScenarioLoader.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using VYaml.Serialization;

namespace RobotSNAP.Core.Scenario
{
    /// <summary>
    /// Chargeur de scénarios YAML
    /// Gère le chargement, le parsing, le caching et l'export des scénarios
    /// </summary>
    public sealed class ScenarioLoader : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Paths")]
        [Tooltip("Dossier contenant les scénarios (relatif à StreamingAssets)")]
        [SerializeField] private string _scenariosFolder = "Scenarios";
        
        [Tooltip("Dossier contenant les maps (relatif à StreamingAssets)")]
        [SerializeField] private string _mapsFolder = "Maps";

        [Header("Settings")]
        [Tooltip("Active le caching des scénarios chargés")]
        [SerializeField] private bool _enableCaching = true;
        
        [Tooltip("Active les logs de débogage")]
        [SerializeField] private bool _logEvents = true;
        
        [Tooltip("Extensions de fichiers YAML acceptées")]
        [SerializeField] private string[] _validExtensions = { ".yaml", ".yml" };

        [Header("Fallback")]
        [Tooltip("Scénario par défaut si aucun n'est trouvé")]
        [SerializeField] private TextAsset _fallbackYamlAsset;

        #endregion

        #region Private Fields

        private readonly Dictionary<string, ScenarioData> _loadedScenarios = new();
        private readonly Dictionary<string, ScenarioInfo> _scenarioInfoCache = new();
        private readonly Dictionary<string, Texture2D> _loadedMaps = new();
        private string _scenariosPath;
        private string _mapsPath;
        private bool _isInitialized;

        #endregion

        #region Public Properties

        /// <summary>
        /// Chemin complet du dossier des scénarios
        /// </summary>
        public string ScenariosPath => _scenariosPath;
        
        /// <summary>
        /// Chemin complet du dossier des maps
        /// </summary>
        public string MapsPath => _mapsPath;
        
        /// <summary>
        /// Nombre de scénarios en cache
        /// </summary>
        public int CacheSize => _loadedScenarios.Count;

        #endregion

        #region Events

        /// <summary>
        /// Événement déclenché quand un scénario est chargé
        /// </summary>
        public event Action<ScenarioData> OnScenarioLoaded;
        
        /// <summary>
        /// Événement déclenché quand une erreur survient
        /// </summary>
        public event Action<string> OnScenarioError;
        
        /// <summary>
        /// Événement déclenché quand une map est chargée
        /// </summary>
        public event Action<string, Texture2D> OnMapLoaded;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            InitializePaths();
        }

        private void InitializePaths()
        {
            _scenariosPath = Path.Combine(Application.streamingAssetsPath, _scenariosFolder);
            _mapsPath = Path.Combine(Application.streamingAssetsPath, _mapsFolder);
            
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

        #endregion

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
                return null;
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
            
            // Créer un scénario par défaut minimal
            var defaultScenario = CreateMinimalScenario();
            OnScenarioLoaded?.Invoke(defaultScenario);
            return defaultScenario;
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

        #endregion

        #region Public API - Map Loading

        /// <summary>
        /// Charge une image de map
        /// </summary>
        /// <param name="mapName">Nom de la map (sans extension)</param>
        /// <returns>Texture2D de la map ou null</returns>
        public Texture2D LoadMap(string mapName)
        {
            if (string.IsNullOrEmpty(mapName))
                return null;
            
            // Vérifier le cache
            if (_loadedMaps.TryGetValue(mapName, out var cached))
            {
                return cached;
            }
            
            // Trouver le fichier
            string filePath = FindMapFile(mapName);
            if (filePath == null)
            {
                if (_logEvents) Debug.LogWarning($"[ScenarioLoader] Map not found: {mapName}");
                return null;
            }
            
            try
            {
                byte[] imageData = File.ReadAllBytes(filePath);
                Texture2D texture = new Texture2D(2, 2);
                texture.LoadImage(imageData);
                texture.name = mapName;
                
                _loadedMaps[mapName] = texture;
                OnMapLoaded?.Invoke(mapName, texture);
                
                if (_logEvents)
                {
                    Debug.Log($"[ScenarioLoader] Loaded map: {mapName} ({texture.width}x{texture.height})");
                }
                
                return texture;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ScenarioLoader] Failed to load map '{mapName}': {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Charge la map associée à un scénario
        /// </summary>
        /// <param name="scenario">Scénario</param>
        /// <returns>Texture2D de la map ou null</returns>
        public Texture2D LoadMapForScenario(ScenarioData scenario)
        {
            if (scenario == null || string.IsNullOrEmpty(scenario.MapImage))
                return null;
            
            return LoadMap(scenario.MapImage);
        }

        #endregion

        #region Public API - Queries

        /// <summary>
        /// Liste tous les scénarios disponibles
        /// </summary>
        /// <returns>Liste des noms de scénarios (sans extension)</returns>
        public List<string> GetAvailableScenarios()
        {
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
        
        /// <summary>
        /// Crée un scénario minimal par défaut
        /// </summary>
        public ScenarioData CreateMinimalScenario()
        {
            var scenario = new ScenarioData
            {
                Info = new ScenarioInfo
                {
                    Name = "default",
                    Description = "Default minimal scenario",
                    Version = "1.0",
                    Author = "RobotSNAP",
                    Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Tags = new[] { "default", "minimal" }
                },
                Points = new Dictionary<string, RefPoint>
                {
                    ["origin"] = RefPoint.FromVector3(Vector3.zero),
                    ["goal"] = RefPoint.FromVector3(new Vector3(5, 0, 5))
                },
                Simulation = new SimulationConfigData
                {
                    Duration = 60f,
                    RandomSeed = 42,
                    MinHumans = 3,
                    MaxHumans = 8
                },
                Robot = new RobotScenarioConfig
                {
                    StartRef = "origin",
                    GoalRef = "goal",
                    Behavior = "normal",
                    Speed = 1.2f
                },
                Humans = new List<HumanScenarioConfig>
                {
                    new HumanScenarioConfig
                    {
                        Id = "walker",
                        Count = 3,
                        Spawn = new SpawnConfig
                        {
                            Type = "random",
                            Reference = "origin"
                        },
                        Goal = new GoalConfig
                        {
                            Type = "point",
                            Reference = "goal"
                        },
                        Speed = 1.0f
                    }
                }
            };
            
            return scenario;
        }
        
        /// <summary>
        /// Crée un scénario d'exemple complet
        /// </summary>
        public ScenarioData CreateExampleScenario()
        {
            var scenario = new ScenarioData
            {
                Info = new ScenarioInfo
                {
                    Name = "example_crossing",
                    Description = "Example scenario with crossing humans",
                    Version = "1.0",
                    Author = "RobotSNAP",
                    Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Tags = new[] { "example", "crossing", "demo" },
                    MapImage = "office",
                    DatasetPath = "ETH"
                },
                Points = new Dictionary<string, RefPoint>
                {
                    ["entrance"] = RefPoint.FromVector3(new Vector3(-8, 0, 0)),
                    ["exit"] = RefPoint.FromVector3(new Vector3(8, 0, 0)),
                    ["center"] = RefPoint.FromVector3(new Vector3(0, 0, 0)),
                    ["zone_north"] = RefPoint.FromBounds(new Vector3(0, 0, 5), new Vector3(6, 0, 4)),
                    ["zone_south"] = RefPoint.FromBounds(new Vector3(0, 0, -5), new Vector3(6, 0, 4)),
                    ["waiting_area"] = RefPoint.FromBounds(new Vector3(3, 0, 2), new Vector3(4, 0, 4))
                },
                Simulation = new SimulationConfigData
                {
                    Duration = 60f,
                    RandomSeed = 42,
                    TimeScale = 1f,
                    MinHumans = 5,
                    MaxHumans = 10
                },
                Robot = new RobotScenarioConfig
                {
                    StartRef = "entrance",
                    GoalRef = "exit",
                    Behavior = "normal",
                    Speed = 1.2f
                },
                Humans = new List<HumanScenarioConfig>
                {
                    new HumanScenarioConfig
                    {
                        Id = "walkers",
                        Count = 4,
                        Spawn = new SpawnConfig
                        {
                            Type = "random",
                            Reference = "zone_north"
                        },
                        Goal = new GoalConfig
                        {
                            Type = "point",
                            Reference = "exit"
                        },
                        Behavior = "normal",
                        Speed = 1.0f,
                        Color = new float[] { 0.2f, 0.6f, 1.0f }
                    },
                    new HumanScenarioConfig
                    {
                        Id = "wanderers",
                        Count = 3,
                        Spawn = new SpawnConfig
                        {
                            Type = "random",
                            Reference = "waiting_area"
                        },
                        Goal = new GoalConfig
                        {
                            Type = "wander",
                            Radius = 4f
                        },
                        Behavior = "social",
                        Speed = 0.8f,
                        Color = new float[] { 1.0f, 0.5f, 0.0f },
                        Personality = new PersonalityConfig
                        {
                            Assertiveness = 0.3f,
                            PersonalSpace = 0.9f,
                            ReactionTime = 0.4f
                        }
                    },
                    new HumanScenarioConfig
                    {
                        Id = "followers",
                        Count = 2,
                        Spawn = new SpawnConfig
                        {
                            Type = "formation",
                            Formation = "line",
                            Spacing = 1.5f,
                            RelativeTo = "robot"
                        },
                        Goal = new GoalConfig
                        {
                            Type = "follow",
                            Target = "robot"
                        },
                        Behavior = "timid",
                        Speed = 1.1f,
                        Color = new float[] { 0.8f, 0.2f, 0.8f },
                        Personality = new PersonalityConfig
                        {
                            Assertiveness = 0.2f,
                            PersonalSpace = 1.0f
                        }
                    }
                }
            };
            
            return scenario;
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
        
        [ContextMenu("Create Example Scenario File")]
        private void EditorCreateExampleScenarioFile()
        {
            var example = CreateExampleScenario();
            ExportScenario(example, "example_crossing.yaml");
            Debug.Log("[ScenarioLoader] Created example scenario file");
        }
        
        [ContextMenu("Create Minimal Scenario File")]
        private void EditorCreateMinimalScenarioFile()
        {
            var minimal = CreateMinimalScenario();
            ExportScenario(minimal, "default.yaml");
            Debug.Log("[ScenarioLoader] Created minimal scenario file");
        }
        
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
            if (string.IsNullOrEmpty(reference)) return Vector3.zero;
            
            if (scenario.Points != null && scenario.Points.TryGetValue(reference, out var point))
                return point.ToVector3();
            
            // Essayer comme ID entier
            if (int.TryParse(reference, out int id) && scenario.Points != null)
            {
                string key = id.ToString();
                if (scenario.Points.TryGetValue(key, out point))
                    return point.ToVector3();
            }
            
            return Vector3.zero;
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