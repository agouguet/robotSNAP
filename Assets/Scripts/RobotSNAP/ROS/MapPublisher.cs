using System.Collections.Generic;
using UnityEngine;
using NavMsgs = RosMessageTypes.Nav;
using StdMsgs = RosMessageTypes.Std;
using RobotSNAP.Core;
using RobotSNAP.Environment;

namespace RobotSNAP.ROS
{
    /// <summary>
    /// Publishes the environment map to ROS.
    /// Converts Unity terrain/texture to ROS OccupancyGrid message.
    /// </summary>
    public class MapPublisher : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private EnvironmentBuilder environmentBuilder;
        
        [Header("Map Settings")]
        [SerializeField] private string mapTopicName = "/map";
        [SerializeField] private float publishInterval = 1f;
        [SerializeField] private bool publishOnStart = true;
        
        [Header("Conversion Settings")]
        [SerializeField] private byte occupiedThreshold = 50;
        [SerializeField] private byte freeThreshold = 200;
        
        [Header("Map Generation")]
        [SerializeField] private int mapWidth = 100;
        [SerializeField] private int mapHeight = 100;
        [SerializeField] private float cellSize = 0.05f;
        
        [Header("Debug")]
        [SerializeField] private bool logPublishEvents = false;
        
        private EnvROS _envROS;
        private string _fullTopicName;
        private float _lastPublishTime;
        
        // Cached map data
        private sbyte[] _cachedMapData;
        private int _cachedWidth;
        private int _cachedHeight;
        private float _cachedResolution;
        
        #region Unity Lifecycle
        
        private void Start()
        {
            Initialize();
            
            if (publishOnStart)
            {
                StartPublishing(publishInterval);
            }
        }
        
        private void Update()
        {
            if (_envROS != null && _envROS.IsInitialized && publishInterval > 0)
            {
                if (Time.time >= _lastPublishTime + publishInterval)
                {
                    PublishMap();
                    _lastPublishTime = Time.time;
                }
            }
        }
        
        private void OnDestroy()
        {
            StopPublishing();
        }
        
        #endregion
        
        #region Initialization
        
        public void Initialize()
        {
            // Find EnvROS
            _envROS = FindObjectOfType<EnvROS>();
            
            if (_envROS == null)
            {
                Debug.LogError($"[{name}] EnvROS not found! Map will not be published.");
                enabled = false;
                return;
            }
            
            // Get prefix
            string prefix = _envROS.Prefix;
            _fullTopicName = string.IsNullOrEmpty(prefix) ? mapTopicName : $"/{prefix}{mapTopicName}";
            
            // Find EnvironmentBuilder if not assigned
            if (environmentBuilder == null)
            {
                environmentBuilder = FindObjectOfType<EnvironmentBuilder>();
            }
            
            // Generate initial map
            GenerateMapFromEnvironment();
            
            // Register publisher
            _envROS.RegisterPublisher<NavMsgs.OccupancyGridMsg>(_fullTopicName);
            
            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Initialized. Publishing to {_fullTopicName}");
            }
        }
        
        #endregion
        
        #region Publishing Control
        
        public void StartPublishing(float interval = -1)
        {
            if (interval > 0)
            {
                publishInterval = interval;
            }
            
            _lastPublishTime = Time.time;
            
            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Started publishing every {publishInterval}s");
            }
        }
        
        public void StopPublishing()
        {
            publishInterval = 0;
            
            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Stopped publishing");
            }
        }
        
        #endregion
        
        #region Map Generation
        
        /// <summary>
        /// Generate map from the environment
        /// </summary>
        public void GenerateMapFromEnvironment()
        {
            if (environmentBuilder != null)
            {
                // Get map from EnvironmentBuilder
                _cachedMapData = GenerateMapFromBuilder(environmentBuilder);
                _cachedWidth = mapWidth;
                _cachedHeight = mapHeight;
                _cachedResolution = cellSize;
            }
            else
            {
                // Generate default empty map
                GenerateEmptyMap();
            }
            
            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Generated map: {_cachedWidth}x{_cachedHeight}, resolution={_cachedResolution}");
            }
        }
        
        private sbyte[] GenerateMapFromBuilder(EnvironmentBuilder builder)
        {
            // Create a grid based on environment bounds
            float floorRadius = builder.GetFloorRadius();
            int gridSize = Mathf.CeilToInt(floorRadius * 2 / cellSize);
            
            _cachedWidth = gridSize;
            _cachedHeight = gridSize;
            _cachedResolution = cellSize;
            
            sbyte[] mapData = new sbyte[gridSize * gridSize];
            
            // Initialize all cells as unknown
            for (int i = 0; i < mapData.Length; i++)
            {
                mapData[i] = -1;
            }
            
            // Calculate world bounds
            Vector3 center = builder.transform.position;
            float halfSize = floorRadius;
            
            // Perform raycasts to determine occupancy
            for (int y = 0; y < gridSize; y++)
            {
                for (int x = 0; x < gridSize; x++)
                {
                    // Convert grid coordinates to world position
                    float worldX = center.x - halfSize + (x + 0.5f) * cellSize;
                    float worldZ = center.z - halfSize + (y + 0.5f) * cellSize;
                    Vector3 worldPos = new Vector3(worldX, 0.5f, worldZ);
                    
                    // Check if position is within floor radius
                    float distToCenter = Vector3.Distance(new Vector3(worldX, 0, worldZ), center);
                    
                    if (distToCenter <= floorRadius)
                    {
                        // Inside floor area - free space
                        mapData[y * gridSize + x] = 0;
                    }
                    else
                    {
                        // Outside floor - occupied (wall)
                        mapData[y * gridSize + x] = 100;
                    }
                }
            }
            
            return mapData;
        }
        
        private void GenerateEmptyMap()
        {
            _cachedWidth = mapWidth;
            _cachedHeight = mapHeight;
            _cachedResolution = cellSize;
            _cachedMapData = new sbyte[mapWidth * mapHeight];
            
            // Initialize all cells as unknown
            for (int i = 0; i < _cachedMapData.Length; i++)
            {
                _cachedMapData[i] = -1;
            }
        }
        
        #endregion
        
        #region Map Publishing
        
        public void PublishMap()
        {
            if (_envROS == null || !_envROS.IsInitialized)
            {
                return;
            }
            
            if (_cachedMapData == null)
            {
                GenerateMapFromEnvironment();
            }
            
            // Create ROS message
            var msg = new NavMsgs.OccupancyGridMsg();
            
            // Header
            msg.header = new StdMsgs.HeaderMsg();
            msg.header.frame_id = mapTopicName.TrimStart('/');
            ROSTimeUtils.UpdateHeader(msg.header);
            
            // Map metadata
            msg.info = new NavMsgs.MapMetaDataMsg();
            msg.info.resolution = _cachedResolution;
            msg.info.width = (uint)_cachedWidth;
            msg.info.height = (uint)_cachedHeight;
            
            // Calculate origin (bottom-left corner)
            Vector3 origin = CalculateMapOrigin();
            msg.info.origin = Util.Geometry.GetMapOriginPose(
                new Vector2(origin.x, origin.z), 
                _cachedResolution, 
                _cachedWidth, 
                _cachedHeight
            );
            
            // Copy map data
            msg.data = new sbyte[_cachedMapData.Length];
            System.Array.Copy(_cachedMapData, msg.data, _cachedMapData.Length);
            
            // Publish
            _envROS.PublishMap(msg);
            
            if (logPublishEvents)
            {
                Debug.Log($"[{name}] Published map: {_cachedWidth}x{_cachedHeight}, resolution={_cachedResolution}");
            }
        }
        
        private Vector3 CalculateMapOrigin()
        {
            if (environmentBuilder != null)
            {
                float floorRadius = environmentBuilder.GetFloorRadius();
                Vector3 center = environmentBuilder.transform.position;
                return new Vector3(center.x - floorRadius, 0, center.z - floorRadius);
            }
            
            return Vector3.zero;
        }
        
        /// <summary>
        /// Force an immediate map publish with regeneration
        /// </summary>
        public void ForcePublish()
        {
            GenerateMapFromEnvironment();
            PublishMap();
        }
        
        /// <summary>
        /// Update map from current environment and publish
        /// </summary>
        public void UpdateAndPublish()
        {
            GenerateMapFromEnvironment();
            PublishMap();
        }
        
        #endregion
        
        #region Map Manipulation
        
        /// <summary>
        /// Set a cell value in the map
        /// </summary>
        public void SetCell(int x, int y, sbyte value)
        {
            if (x >= 0 && x < _cachedWidth && y >= 0 && y < _cachedHeight)
            {
                _cachedMapData[y * _cachedWidth + x] = value;
            }
        }
        
        /// <summary>
        /// Get a cell value from the map
        /// </summary>
        public sbyte GetCell(int x, int y)
        {
            if (x >= 0 && x < _cachedWidth && y >= 0 && y < _cachedHeight)
            {
                return _cachedMapData[y * _cachedWidth + x];
            }
            return -1;
        }
        
        /// <summary>
        /// Add an obstacle to the map
        /// </summary>
        public void AddObstacle(Vector3 position, float radius)
        {
            if (_cachedMapData == null) return;
            
            Vector3 origin = CalculateMapOrigin();
            
            int minX = Mathf.Max(0, Mathf.FloorToInt((position.x - radius - origin.x) / _cachedResolution));
            int maxX = Mathf.Min(_cachedWidth - 1, Mathf.FloorToInt((position.x + radius - origin.x) / _cachedResolution));
            int minY = Mathf.Max(0, Mathf.FloorToInt((position.z - radius - origin.z) / _cachedResolution));
            int maxY = Mathf.Min(_cachedHeight - 1, Mathf.FloorToInt((position.z + radius - origin.z) / _cachedResolution));
            
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float worldX = origin.x + (x + 0.5f) * _cachedResolution;
                    float worldZ = origin.z + (y + 0.5f) * _cachedResolution;
                    float dist = Vector2.Distance(new Vector2(worldX, worldZ), new Vector2(position.x, position.z));
                    
                    if (dist <= radius)
                    {
                        _cachedMapData[y * _cachedWidth + x] = 100; // Occupied
                    }
                }
            }
        }
        
        #endregion
        
        #region Editor Utilities
        
        [ContextMenu("Force Publish Map")]
        private void EditorForcePublish()
        {
            ForcePublish();
            Debug.Log($"[{name}] Force published map");
        }
        
        [ContextMenu("Regenerate Map")]
        private void EditorRegenerateMap()
        {
            GenerateMapFromEnvironment();
            Debug.Log($"[{name}] Map regenerated");
        }
        
        [ContextMenu("Start Publishing")]
        private void EditorStartPublishing()
        {
            StartPublishing(publishInterval);
        }
        
        [ContextMenu("Stop Publishing")]
        private void EditorStopPublishing()
        {
            StopPublishing();
        }
        
        #endregion
        
        #region Gizmos
        
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || _cachedMapData == null) return;
            
            Vector3 origin = CalculateMapOrigin();
            
            for (int y = 0; y < _cachedHeight; y++)
            {
                for (int x = 0; x < _cachedWidth; x++)
                {
                    sbyte value = _cachedMapData[y * _cachedWidth + x];
                    
                    if (value == 100) // Occupied
                    {
                        Gizmos.color = Color.red;
                    }
                    else if (value == 0) // Free
                    {
                        Gizmos.color = Color.green;
                    }
                    else // Unknown
                    {
                        Gizmos.color = Color.gray;
                    }
                    
                    Vector3 cellCenter = origin + new Vector3((x + 0.5f) * _cachedResolution, 0.1f, (y + 0.5f) * _cachedResolution);
                    Gizmos.DrawCube(cellCenter, new Vector3(_cachedResolution, 0.05f, _cachedResolution));
                }
            }
        }
        
        #endregion
    }
}