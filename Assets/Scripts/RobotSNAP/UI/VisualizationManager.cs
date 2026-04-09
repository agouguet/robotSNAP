using UnityEngine;
using System.Collections.Generic;
using RobotSNAP.Core;

namespace RobotSNAP.UI
{
    public class VisualizationManager : MonoBehaviour
    {
        [Header("Visualization Settings")]
        public bool showGrid = true;
        public bool showAxes = false;
        public bool showFloor = true;
        public bool showWalls = true;
        public bool wireframeMode = false;
        
        [Header("Robot Visualization")]
        public bool showRobotTrajectory = true;
        public bool showRobotPath = true;
        public bool showRobotGoal = true;
        public bool showRobotVelocityVector = false;
        public bool showRobotSensorRays = true;
        public int trajectoryLength = 100;
        public Color robotTrajectoryColor = Color.cyan;
        public Color robotPathColor = Color.green;
        public Color robotGoalColor = Color.yellow;
        public Color robotVelocityColor = Color.red;
        
        [Header("Human Visualization")]
        public bool showHumanTrajectories = true;
        public bool showHumanGoals = true;
        public bool showHumanVelocityVectors = false;
        public bool showHumanInteractionRadius = true;
        public bool colorHumansByState = true;
        public float humanOpacity = 1f;
        public Color humanDefaultColor = Color.blue;
        public Color humanAlertColor = Color.yellow;
        public Color humanDangerColor = Color.red;
        
        [Header("Debug Visualization")]
        public bool showColliders = false;
        public bool showNavMesh = false;
        public bool showOccupancyGrid = false;
        public bool showCostmap = false;
        public bool showLaserScans = true;
        public bool showBoundingBoxes = false;
        public bool showAgentIDs = true;
        public bool showDistanceLabels = false;
        
        [Header("Grid Settings")]
        public float gridSize = 50f;
        public int gridDivisions = 50;
        public Color gridColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
        public Material gridMaterial;
        public Material axisMaterial;
        
        [Header("References")]
        public GameObject gridObject;
        public GameObject axesObject;
        public GameObject floorObject;
        
        private Dictionary<int, LineRenderer> _trajectoryRenderers = new Dictionary<int, LineRenderer>();
        private Dictionary<int, LineRenderer> _pathRenderers = new Dictionary<int, LineRenderer>();
        private Dictionary<int, GameObject> _goalMarkers = new Dictionary<int, GameObject>();
        private Dictionary<int, LineRenderer> _velocityVectors = new Dictionary<int, LineRenderer>();
        private Dictionary<int, GameObject> _interactionRadiusVisuals = new Dictionary<int, GameObject>();
        private Dictionary<int, TextMesh> _agentIdLabels = new Dictionary<int, TextMesh>();
        
        private Material _wireframeMaterial;
        private Material _originalMaterial;
        private Dictionary<Renderer, Material[]> _originalMaterials = new Dictionary<Renderer, Material[]>();
        
        private Supervisor _supervisor;
        private List<Vector3> _laserScanPoints = new List<Vector3>();
        private LineRenderer _laserScanRenderer;
        
        #region Unity Lifecycle
        
        private void Start()
        {
            Initialize();
        }
        
        private void Update()
        {
            UpdateVisualizations();
        }
        
        private void OnDestroy()
        {
            CleanupVisualizations();
        }
        
        #endregion
        
        #region Initialization
        
        private void Initialize()
        {
            _supervisor = Supervisor.Instance;
            
            CreateGrid();
            CreateAxes();
            
            // Create wireframe material
            _wireframeMaterial = new Material(Shader.Find("Standard"));
            _wireframeMaterial.color = Color.green;
            
            // Create laser scan renderer
            CreateLaserScanRenderer();
        }
        
        private void CreateGrid()
        {
            if (gridObject == null)
            {
                gridObject = new GameObject("Grid");
                gridObject.transform.SetParent(transform);
                
                LineRenderer gridRenderer = gridObject.AddComponent<LineRenderer>();
                gridRenderer.positionCount = (gridDivisions * 4) + 4;
                gridRenderer.startWidth = 0.02f;
                gridRenderer.endWidth = 0.02f;
                gridRenderer.material = gridMaterial ?? new Material(Shader.Find("Sprites/Default"));
                gridRenderer.startColor = gridColor;
                gridRenderer.endColor = gridColor;
                
                UpdateGridRenderer();
            }
            
            gridObject.SetActive(showGrid);
        }
        
        private void UpdateGridRenderer()
        {
            if (gridObject == null) return;
            
            LineRenderer renderer = gridObject.GetComponent<LineRenderer>();
            if (renderer == null) return;
            
            List<Vector3> points = new List<Vector3>();
            float halfSize = gridSize / 2f;
            float step = gridSize / gridDivisions;
            
            // Parallel lines along X axis
            for (int i = 0; i <= gridDivisions; i++)
            {
                float z = -halfSize + i * step;
                points.Add(new Vector3(-halfSize, 0.01f, z));
                points.Add(new Vector3(halfSize, 0.01f, z));
            }
            
            // Parallel lines along Z axis
            for (int i = 0; i <= gridDivisions; i++)
            {
                float x = -halfSize + i * step;
                points.Add(new Vector3(x, 0.01f, -halfSize));
                points.Add(new Vector3(x, 0.01f, halfSize));
            }
            
            renderer.positionCount = points.Count;
            renderer.SetPositions(points.ToArray());
        }
        
        private void CreateAxes()
        {
            if (axesObject == null)
            {
                axesObject = new GameObject("Axes");
                axesObject.transform.SetParent(transform);
                
                LineRenderer axesRenderer = axesObject.AddComponent<LineRenderer>();
                axesRenderer.positionCount = 6;
                axesRenderer.startWidth = 0.05f;
                axesRenderer.endWidth = 0.05f;
                axesRenderer.material = axisMaterial ?? new Material(Shader.Find("Sprites/Default"));
                
                // X axis (Red)
                axesRenderer.SetPosition(0, Vector3.zero);
                axesRenderer.SetPosition(1, Vector3.right * 5f);
                
                // Y axis (Green)
                axesRenderer.SetPosition(2, Vector3.zero);
                axesRenderer.SetPosition(3, Vector3.up * 5f);
                
                // Z axis (Blue)
                axesRenderer.SetPosition(4, Vector3.zero);
                axesRenderer.SetPosition(5, Vector3.forward * 5f);
                
                // Set colors
                axesRenderer.startColor = Color.white;
                axesRenderer.endColor = Color.white;
            }
            
            axesObject.SetActive(showAxes);
        }
        
        private void CreateLaserScanRenderer()
        {
            GameObject laserObj = new GameObject("LaserScanRenderer");
            laserObj.transform.SetParent(transform);
            
            _laserScanRenderer = laserObj.AddComponent<LineRenderer>();
            _laserScanRenderer.startWidth = 0.03f;
            _laserScanRenderer.endWidth = 0.03f;
            _laserScanRenderer.material = new Material(Shader.Find("Sprites/Default"));
            _laserScanRenderer.startColor = Color.red;
            _laserScanRenderer.endColor = Color.red;
            _laserScanRenderer.loop = false;
            _laserScanRenderer.useWorldSpace = true;
        }
        
        #endregion
        
        #region Visualization Updates
        
        private void UpdateVisualizations()
        {
            if (!Application.isPlaying) return;
            
            // Update grid visibility
            if (gridObject != null)
                gridObject.SetActive(showGrid);
            
            // Update axes visibility
            if (axesObject != null)
                axesObject.SetActive(showAxes);
            
            // Update floor visibility
            if (floorObject != null)
                floorObject.SetActive(showFloor);
            
            // Update robot visualizations
            UpdateRobotVisualizations();
            
            // Update human visualizations
            UpdateHumanVisualizations();
            
            // Update debug visualizations
            UpdateDebugVisualizations();
            
            // Update wireframe mode
            UpdateWireframeMode();
        }
        
        private void UpdateRobotVisualizations()
        {
            GameObject robot = GameObject.FindGameObjectWithTag("Robot");
            if (robot == null) robot = GameObject.Find("Robot");
            if (robot == null) return;
            
            int robotId = robot.GetInstanceID();
            
            // Trajectory
            if (showRobotTrajectory)
            {
                LineRenderer trajRenderer = GetOrCreateTrajectoryRenderer(robotId, robotTrajectoryColor);
                UpdateTrajectoryRenderer(robot, trajRenderer);
            }
            else if (_trajectoryRenderers.ContainsKey(robotId))
            {
                _trajectoryRenderers[robotId].enabled = false;
            }
            
            // Path
            if (showRobotPath)
            {
                LineRenderer pathRenderer = GetOrCreatePathRenderer(robotId, robotPathColor);
                UpdatePathRenderer(robot, pathRenderer);
            }
            else if (_pathRenderers.ContainsKey(robotId))
            {
                _pathRenderers[robotId].enabled = false;
            }
            
            // Goal
            if (showRobotGoal)
            {
                GameObject goalMarker = GetOrCreateGoalMarker(robotId, robotGoalColor);
                UpdateGoalMarker(robot, goalMarker);
            }
            else if (_goalMarkers.ContainsKey(robotId))
            {
                _goalMarkers[robotId].SetActive(false);
            }
            
            // Velocity Vector
            if (showRobotVelocityVector)
            {
                LineRenderer velRenderer = GetOrCreateVelocityVector(robotId, robotVelocityColor);
                UpdateVelocityVector(robot, velRenderer);
            }
            else if (_velocityVectors.ContainsKey(robotId))
            {
                _velocityVectors[robotId].enabled = false;
            }
            
            // Sensor Rays
            if (showRobotSensorRays)
            {
                UpdateLaserScanVisualization(robot);
            }
            else
            {
                _laserScanRenderer.enabled = false;
            }
            
            // Agent ID
            if (showAgentIDs)
            {
                TextMesh label = GetOrCreateAgentIdLabel(robotId, "Robot");
                UpdateAgentIdLabel(robot, label, "Robot", Color.cyan);
            }
        }
        
        private void UpdateHumanVisualizations()
        {
            GameObject[] humans = GameObject.FindGameObjectsWithTag("Human");
            
            foreach (var human in humans)
            {
                int humanId = human.GetInstanceID();
                var humanAgent = human.GetComponent<HumanAgent>();
                
                // Trajectory
                if (showHumanTrajectories)
                {
                    LineRenderer trajRenderer = GetOrCreateTrajectoryRenderer(humanId, GetHumanColor(human));
                    UpdateTrajectoryRenderer(human, trajRenderer);
                }
                
                // Goal
                if (showHumanGoals)
                {
                    GameObject goalMarker = GetOrCreateGoalMarker(humanId, GetHumanColor(human));
                    UpdateGoalMarker(human, goalMarker);
                }
                
                // Velocity Vector
                if (showHumanVelocityVectors)
                {
                    LineRenderer velRenderer = GetOrCreateVelocityVector(humanId, GetHumanColor(human));
                    UpdateVelocityVector(human, velRenderer);
                }
                
                // Interaction Radius
                if (showHumanInteractionRadius)
                {
                    GameObject radiusVisual = GetOrCreateInteractionRadius(humanId);
                    UpdateInteractionRadius(human, radiusVisual);
                }
                
                // Agent ID
                if (showAgentIDs)
                {
                    string idText = humanAgent != null ? $"H{humanAgent.agentId}" : "Human";
                    TextMesh label = GetOrCreateAgentIdLabel(humanId, idText);
                    UpdateAgentIdLabel(human, label, idText, GetHumanColor(human));
                }
                
                // Opacity
                UpdateHumanOpacity(human);
            }
        }
        
        private void UpdateDebugVisualizations()
        {
            // Colliders
            if (showColliders)
            {
                // Implementation depends on your collider visualization system
            }
            
            // Occupancy Grid / Costmap
            // Implementation depends on your navigation system
            
            // Bounding Boxes
            if (showBoundingBoxes)
            {
                DrawBoundingBoxes();
            }
            
            // Distance Labels
            if (showDistanceLabels)
            {
                UpdateDistanceLabels();
            }
        }
        
        private void UpdateWireframeMode()
        {
            if (wireframeMode)
            {
                // Store original materials and apply wireframe
                foreach (var renderer in FindObjectsOfType<Renderer>())
                {
                    if (!_originalMaterials.ContainsKey(renderer))
                    {
                        _originalMaterials[renderer] = renderer.materials;
                        
                        Material[] wireframeMats = new Material[renderer.materials.Length];
                        for (int i = 0; i < wireframeMats.Length; i++)
                        {
                            wireframeMats[i] = _wireframeMaterial;
                        }
                        renderer.materials = wireframeMats;
                    }
                }
            }
            else
            {
                // Restore original materials
                foreach (var kvp in _originalMaterials)
                {
                    if (kvp.Key != null)
                    {
                        kvp.Key.materials = kvp.Value;
                    }
                }
                _originalMaterials.Clear();
            }
        }
        
        #endregion
        
        #region Helper Methods
        
        private LineRenderer GetOrCreateTrajectoryRenderer(int id, Color color)
        {
            if (!_trajectoryRenderers.ContainsKey(id))
            {
                GameObject obj = new GameObject($"Trajectory_{id}");
                obj.transform.SetParent(transform);
                
                LineRenderer renderer = obj.AddComponent<LineRenderer>();
                renderer.startWidth = 0.05f;
                renderer.endWidth = 0.05f;
                renderer.material = new Material(Shader.Find("Sprites/Default"));
                renderer.startColor = color;
                renderer.endColor = color;
                renderer.positionCount = 0;
                
                _trajectoryRenderers[id] = renderer;
            }
            
            _trajectoryRenderers[id].enabled = true;
            return _trajectoryRenderers[id];
        }
        
        private void UpdateTrajectoryRenderer(GameObject obj, LineRenderer renderer)
        {
            // Get trajectory points from agent's history
            var history = GetPositionHistory(obj);
            if (history != null && history.Count > 0)
            {
                renderer.positionCount = Mathf.Min(history.Count, trajectoryLength);
                Vector3[] points = new Vector3[renderer.positionCount];
                
                int startIndex = Mathf.Max(0, history.Count - trajectoryLength);
                for (int i = 0; i < renderer.positionCount; i++)
                {
                    points[i] = history[startIndex + i] + Vector3.up * 0.1f;
                }
                
                renderer.SetPositions(points);
            }
        }
        
        private List<Vector3> GetPositionHistory(GameObject obj)
        {
            // This should be implemented based on your agent's history tracking
            // For now, return a simple list with current position
            var history = new List<Vector3>();
            history.Add(obj.transform.position);
            return history;
        }
        
        private LineRenderer GetOrCreatePathRenderer(int id, Color color)
        {
            if (!_pathRenderers.ContainsKey(id))
            {
                GameObject obj = new GameObject($"Path_{id}");
                obj.transform.SetParent(transform);
                
                LineRenderer renderer = obj.AddComponent<LineRenderer>();
                renderer.startWidth = 0.1f;
                renderer.endWidth = 0.1f;
                renderer.material = new Material(Shader.Find("Sprites/Default"));
                renderer.startColor = color;
                renderer.endColor = color;
                renderer.textureMode = LineTextureMode.Tile;
                
                _pathRenderers[id] = renderer;
            }
            
            _pathRenderers[id].enabled = true;
            return _pathRenderers[id];
        }
        
        private void UpdatePathRenderer(GameObject obj, LineRenderer renderer)
        {
            // Get planned path from agent's navigation system
            Vector3[] path = GetPlannedPath(obj);
            if (path != null && path.Length > 0)
            {
                renderer.positionCount = path.Length;
                for (int i = 0; i < path.Length; i++)
                {
                    path[i] += Vector3.up * 0.2f;
                }
                renderer.SetPositions(path);
            }
        }
        
        private Vector3[] GetPlannedPath(GameObject obj)
        {
            // This should be implemented based on your navigation system
            return new Vector3[] { obj.transform.position };
        }
        
        private GameObject GetOrCreateGoalMarker(int id, Color color)
        {
            if (!_goalMarkers.ContainsKey(id))
            {
                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = $"Goal_{id}";
                marker.transform.SetParent(transform);
                marker.transform.localScale = Vector3.one * 0.3f;
                
                Renderer rend = marker.GetComponent<Renderer>();
                rend.material = new Material(Shader.Find("Standard"));
                rend.material.color = color;
                
                Collider col = marker.GetComponent<Collider>();
                if (col != null) Destroy(col);
                
                _goalMarkers[id] = marker;
            }
            
            _goalMarkers[id].SetActive(true);
            return _goalMarkers[id];
        }
        
        private void UpdateGoalMarker(GameObject obj, GameObject marker)
        {
            Vector3 goal = GetAgentGoal(obj);
            if (goal != Vector3.zero)
            {
                marker.transform.position = goal + Vector3.up * 0.5f;
            }
        }
        
        private Vector3 GetAgentGoal(GameObject obj)
        {
            // This should be implemented based on your agent's goal system
            return obj.transform.position + obj.transform.forward * 5f;
        }
        
        private LineRenderer GetOrCreateVelocityVector(int id, Color color)
        {
            if (!_velocityVectors.ContainsKey(id))
            {
                GameObject obj = new GameObject($"Velocity_{id}");
                obj.transform.SetParent(transform);
                
                LineRenderer renderer = obj.AddComponent<LineRenderer>();
                renderer.startWidth = 0.05f;
                renderer.endWidth = 0.05f;
                renderer.material = new Material(Shader.Find("Sprites/Default"));
                renderer.startColor = color;
                renderer.endColor = color;
                renderer.positionCount = 2;
                
                _velocityVectors[id] = renderer;
            }
            
            _velocityVectors[id].enabled = true;
            return _velocityVectors[id];
        }
        
        private void UpdateVelocityVector(GameObject obj, LineRenderer renderer)
        {
            Vector3 velocity = GetAgentVelocity(obj);
            if (velocity.magnitude > 0.01f)
            {
                Vector3 start = obj.transform.position + Vector3.up;
                Vector3 end = start + velocity;
                
                renderer.SetPosition(0, start);
                renderer.SetPosition(1, end);
                
                // Add arrow head
                // Implementation depends on your arrow rendering system
            }
        }
        
        private Vector3 GetAgentVelocity(GameObject obj)
        {
            // This should be implemented based on your agent's movement system
            Rigidbody rb = obj.GetComponent<Rigidbody>();
            if (rb != null)
                return rb.linearVelocity;
            
            return obj.transform.forward * 1f;
        }
        
        private GameObject GetOrCreateInteractionRadius(int id)
        {
            if (!_interactionRadiusVisuals.ContainsKey(id))
            {
                GameObject radiusObj = new GameObject($"InteractionRadius_{id}");
                radiusObj.transform.SetParent(transform);
                
                LineRenderer renderer = radiusObj.AddComponent<LineRenderer>();
                renderer.startWidth = 0.03f;
                renderer.endWidth = 0.03f;
                renderer.material = new Material(Shader.Find("Sprites/Default"));
                renderer.startColor = new Color(1, 1, 0, 0.5f);
                renderer.endColor = new Color(1, 1, 0, 0.5f);
                renderer.loop = true;
                renderer.useWorldSpace = false;
                
                // Create circle points
                int segments = 32;
                renderer.positionCount = segments + 1;
                Vector3[] points = new Vector3[segments + 1];
                for (int i = 0; i <= segments; i++)
                {
                    float angle = i * 2f * Mathf.PI / segments;
                    points[i] = new Vector3(Mathf.Cos(angle), 0.05f, Mathf.Sin(angle));
                }
                renderer.SetPositions(points);
                
                _interactionRadiusVisuals[id] = radiusObj;
            }
            
            _interactionRadiusVisuals[id].SetActive(true);
            return _interactionRadiusVisuals[id];
        }
        
        private void UpdateInteractionRadius(GameObject obj, GameObject radiusVisual)
        {
            radiusVisual.transform.position = obj.transform.position;
            
            float radius = GetInteractionRadius(obj);
            radiusVisual.transform.localScale = Vector3.one * radius;
        }
        
        private float GetInteractionRadius(GameObject obj)
        {
            // This should come from your human configuration
            return 3f;
        }
        
        private TextMesh GetOrCreateAgentIdLabel(int id, string text)
        {
            if (!_agentIdLabels.ContainsKey(id))
            {
                GameObject labelObj = new GameObject($"Label_{id}");
                labelObj.transform.SetParent(transform);
                
                TextMesh textMesh = labelObj.AddComponent<TextMesh>();
                textMesh.text = text;
                textMesh.fontSize = 24;
                textMesh.characterSize = 0.2f;
                textMesh.anchor = TextAnchor.MiddleCenter;
                textMesh.alignment = TextAlignment.Center;
                textMesh.color = Color.white;
                textMesh.fontStyle = FontStyle.Bold;
                
                _agentIdLabels[id] = textMesh;
            }
            
            _agentIdLabels[id].gameObject.SetActive(true);
            return _agentIdLabels[id];
        }
        
        private void UpdateAgentIdLabel(GameObject obj, TextMesh label, string text, Color color)
        {
            label.transform.position = obj.transform.position + Vector3.up * 2.5f;
            label.transform.rotation = Camera.main != null ? Camera.main.transform.rotation : Quaternion.identity;
            label.text = text;
            label.color = color;
        }
        
        private Color GetHumanColor(GameObject human)
        {
            if (!colorHumansByState)
                return humanDefaultColor;
            
            // Determine color based on state (distance to robot, velocity, etc.)
            GameObject robot = GameObject.FindGameObjectWithTag("Robot");
            if (robot != null)
            {
                float distance = Vector3.Distance(human.transform.position, robot.transform.position);
                
                if (distance < 1.5f)
                    return humanDangerColor;
                else if (distance < 3f)
                    return humanAlertColor;
            }
            
            return humanDefaultColor;
        }
        
        private void UpdateHumanOpacity(GameObject human)
        {
            Renderer[] renderers = human.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                foreach (var mat in renderer.materials)
                {
                    Color color = mat.color;
                    color.a = humanOpacity;
                    mat.color = color;
                }
            }
        }
        
        private void UpdateLaserScanVisualization(GameObject robot)
        {
            // This should get actual laser scan data from your robot's sensors
            Vector3 robotPos = robot.transform.position;
            float maxRange = 10f;
            int numRays = 36;
            
            _laserScanPoints.Clear();
            _laserScanPoints.Add(robotPos + Vector3.up * 0.2f);
            
            for (int i = 0; i < numRays; i++)
            {
                float angle = i * 360f / numRays * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                
                RaycastHit hit;
                if (Physics.Raycast(robotPos + Vector3.up * 0.2f, direction, out hit, maxRange))
                {
                    _laserScanPoints.Add(hit.point);
                }
                else
                {
                    _laserScanPoints.Add(robotPos + Vector3.up * 0.2f + direction * maxRange);
                }
            }
            
            _laserScanRenderer.positionCount = _laserScanPoints.Count;
            _laserScanRenderer.SetPositions(_laserScanPoints.ToArray());
            _laserScanRenderer.enabled = true;
        }
        
        private void DrawBoundingBoxes()
        {
            // Implementation for drawing 3D bounding boxes around agents
        }
        
        private void UpdateDistanceLabels()
        {
            // Implementation for showing distances between agents
        }
        
        private void CleanupVisualizations()
        {
            foreach (var renderer in _trajectoryRenderers.Values)
                if (renderer != null) Destroy(renderer.gameObject);
            
            foreach (var renderer in _pathRenderers.Values)
                if (renderer != null) Destroy(renderer.gameObject);
            
            foreach (var marker in _goalMarkers.Values)
                if (marker != null) Destroy(marker);
            
            foreach (var renderer in _velocityVectors.Values)
                if (renderer != null) Destroy(renderer.gameObject);
            
            foreach (var visual in _interactionRadiusVisuals.Values)
                if (visual != null) Destroy(visual);
            
            foreach (var label in _agentIdLabels.Values)
                if (label != null) Destroy(label.gameObject);
        }
        
        #endregion
    }
}