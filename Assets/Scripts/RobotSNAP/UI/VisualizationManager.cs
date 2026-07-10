using UnityEngine;
using System.Collections.Generic;
using RobotSNAP.Core;
using RobotSNAP.Agents;

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
        
        // Agent data tracking
        private Dictionary<int, Queue<Vector3>> _positionHistory = new Dictionary<int, Queue<Vector3>>();
        private Dictionary<int, LineRenderer> _trajectoryRenderers = new Dictionary<int, LineRenderer>();
        private Dictionary<int, LineRenderer> _pathRenderers = new Dictionary<int, LineRenderer>();
        private Dictionary<int, GameObject> _goalMarkers = new Dictionary<int, GameObject>();
        private Dictionary<int, LineRenderer> _velocityVectors = new Dictionary<int, LineRenderer>();
        private Dictionary<int, GameObject> _interactionRadiusVisuals = new Dictionary<int, GameObject>();
        private Dictionary<int, TextMesh> _agentIdLabels = new Dictionary<int, TextMesh>();
        
        private Material _wireframeMaterial;
        private Dictionary<Renderer, Material[]> _originalMaterials = new Dictionary<Renderer, Material[]>();
        
        private Supervisor _supervisor;
        private List<Vector3> _laserScanPoints = new List<Vector3>();
        private LineRenderer _laserScanRenderer;
        private float _lastLaserUpdateTime;
        private RaycastLaserScanner _robotLaserScanner;
        
        #region Unity Lifecycle
        
        private void Start()
        {
            Initialize();
        }
        
        private void Update()
        {
            UpdateVisualizations();
            UpdatePositionHistory();
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
            
            _wireframeMaterial = new Material(Shader.Find("Standard"));
            _wireframeMaterial.color = Color.green;
            
            CreateLaserScanRenderer();
        }
        
        private void CreateGrid()
        {
            if (gridObject == null)
            {
                gridObject = new GameObject("Grid");
                gridObject.transform.SetParent(transform);
                gridObject.transform.position = Vector3.zero;
                
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
            float y = 0.01f; // just above ground
            
            for (int i = 0; i <= gridDivisions; i++)
            {
                float z = -halfSize + i * step;
                points.Add(new Vector3(-halfSize, y, z));
                points.Add(new Vector3(halfSize, y, z));
            }
            for (int i = 0; i <= gridDivisions; i++)
            {
                float x = -halfSize + i * step;
                points.Add(new Vector3(x, y, -halfSize));
                points.Add(new Vector3(x, y, halfSize));
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
                
                axesRenderer.SetPosition(0, Vector3.zero);
                axesRenderer.SetPosition(1, Vector3.right * 5f);
                axesRenderer.SetPosition(2, Vector3.zero);
                axesRenderer.SetPosition(3, Vector3.up * 5f);
                axesRenderer.SetPosition(4, Vector3.zero);
                axesRenderer.SetPosition(5, Vector3.forward * 5f);
                
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
        
        #region Position History Tracking
        
        private void UpdatePositionHistory()
        {
            // Update robot history
            Robot robot = FindObjectOfType<Robot>();
            if (robot != null)
            {
                AddPositionToHistory(robot.GetInstanceID(), robot.Position);
            }
            // Update humans history
            HumanAgent[] humans = FindObjectsOfType<HumanAgent>();
            foreach (var human in humans)
            {
                AddPositionToHistory(human.GetInstanceID(), human.transform.position);
            }
        }
        
        private void AddPositionToHistory(int id, Vector3 pos)
        {
            if (!_positionHistory.ContainsKey(id))
                _positionHistory[id] = new Queue<Vector3>();
            var queue = _positionHistory[id];
            queue.Enqueue(pos);
            while (queue.Count > trajectoryLength)
                queue.Dequeue();
        }
        
        private List<Vector3> GetPositionHistory(int id)
        {
            if (_positionHistory.TryGetValue(id, out var queue))
                return new List<Vector3>(queue);
            return new List<Vector3>();
        }
        
        #endregion
        
        #region Visualization Updates
        
        private void UpdateVisualizations()
        {
            if (!Application.isPlaying) return;
            
            if (gridObject != null) gridObject.SetActive(showGrid);
            if (axesObject != null) axesObject.SetActive(showAxes);
            if (floorObject != null) floorObject.SetActive(showFloor);
            
            UpdateRobotVisualizations();
            UpdateHumanVisualizations();
            UpdateDebugVisualizations();
            UpdateWireframeMode();
        }

        
        private void UpdateRobotVisualizations()
        {
            Robot robot = FindObjectOfType<Robot>();
            if (robot == null) return;
            
            int id = robot.GetInstanceID();
            Vector3 robotPos = robot.Position; // baseLink position
            
            // Trajectory
            if (showRobotTrajectory)
            {
                var renderer = GetOrCreateLineRenderer(_trajectoryRenderers, id, $"Traj_Robot", robotTrajectoryColor, 0.05f);
                UpdateTrajectoryRenderer(id, renderer);
            }
            else HideRenderer(_trajectoryRenderers, id);
            
            // Path (planned navigation)
            if (showRobotPath)
            {
                var renderer = GetOrCreateLineRenderer(_pathRenderers, id, $"Path_Robot", robotPathColor, 0.1f);
                UpdatePlannedPathRenderer(robot, renderer);
            }
            else HideRenderer(_pathRenderers, id);
            
            // Goal marker
            if (showRobotGoal && robot.HasGoal)
            {
                var marker = GetOrCreateGoalMarker(id, robotGoalColor);
                marker.transform.position = robot.Goal + Vector3.up * 0.5f;
                marker.SetActive(true);
            }
            else if (_goalMarkers.ContainsKey(id)) _goalMarkers[id].SetActive(false);
            
            // Velocity vector
            if (showRobotVelocityVector && robot.Velocity.magnitude > 0.1f)
            {
                var renderer = GetOrCreateLineRenderer(_velocityVectors, id, $"Vel_Robot", robotVelocityColor, 0.05f);
                Vector3 start = robotPos + Vector3.up;
                Vector3 end = start + robot.Velocity;
                renderer.SetPosition(0, start);
                renderer.SetPosition(1, end);
                renderer.enabled = true;
            }
            else HideRenderer(_velocityVectors, id);
            
            // Laser scans
            if (showRobotSensorRays)
                UpdateLaserScanVisualization(robot);
            else if (_laserScanRenderer != null)
                _laserScanRenderer.enabled = false;
            
            // Agent ID label
            if (showAgentIDs)
            {
                var label = GetOrCreateAgentIdLabel(id, "Robot", Color.cyan);
                label.transform.position = robotPos + Vector3.up * 1.5f;
                label.text = "Robot";
                label.color = Color.cyan;
            }
        }
        
        private void UpdateHumanVisualizations()
        {
            HumanAgent[] humans = FindObjectsOfType<HumanAgent>();
            foreach (var human in humans)
            {
                int id = human.GetInstanceID();
                Vector3 pos = human.transform.position;
                Color humanColor = GetHumanColor(human);
                
                // Trajectory
                if (showHumanTrajectories)
                {
                    var renderer = GetOrCreateLineRenderer(_trajectoryRenderers, id, $"Traj_Human{id}", humanColor, 0.04f);
                    UpdateTrajectoryRenderer(id, renderer);
                }
                else HideRenderer(_trajectoryRenderers, id);
                
                // Goal (wander/point/follow – for visualization we show the current goal)
                Vector3 goal = GetHumanGoal(human);
                if (showHumanGoals && goal != Vector3.zero)
                {
                    var marker = GetOrCreateGoalMarker(id, humanColor);
                    marker.transform.position = goal + Vector3.up * 0.4f;
                    marker.SetActive(true);
                }
                else if (_goalMarkers.ContainsKey(id)) _goalMarkers[id].SetActive(false);
                
                // Velocity vector
                if (showHumanVelocityVectors)
                {
                    Vector3 velocity = GetHumanVelocity(human);
                    if (velocity.magnitude > 0.1f)
                    {
                        var renderer = GetOrCreateLineRenderer(_velocityVectors, id, $"Vel_Human{id}", humanColor, 0.04f);
                        renderer.SetPosition(0, pos + Vector3.up);
                        renderer.SetPosition(1, pos + Vector3.up + velocity);
                        renderer.enabled = true;
                    }
                    else HideRenderer(_velocityVectors, id);
                }
                else HideRenderer(_velocityVectors, id);
                
                // Interaction radius
                if (showHumanInteractionRadius)
                {
                    float radius = GetHumanInteractionRadius(human);
                    if (radius > 0)
                    {
                        var radiusObj = GetOrCreateInteractionRadius(id);
                        radiusObj.transform.position = pos;
                        radiusObj.transform.localScale = Vector3.one * radius;
                        radiusObj.SetActive(true);
                    }
                    else HideInteractionRadius(id);
                }
                else HideInteractionRadius(id);
                
                // Opacity
                if (Mathf.Abs(humanOpacity - 1f) > 0.01f)
                    SetHumanOpacity(human, humanOpacity);
                
                // Agent ID label
                if (showAgentIDs)
                {
                    var label = GetOrCreateAgentIdLabel(id, $"H{id}", humanColor);
                    label.transform.position = pos + Vector3.up * 1.5f;
                    label.text = $"H{id}";
                    label.color = humanColor;
                }
            }
        }
        
        private void UpdateDebugVisualizations()
        {
            if (showColliders) { /* optional: implement helper toggles */ }
            if (showBoundingBoxes) DrawBoundingBoxes();
            if (showDistanceLabels) UpdateDistanceLabels();
        }
        
        private void UpdateWireframeMode()
        {
            if (wireframeMode)
            {
                foreach (var renderer in FindObjectsOfType<Renderer>())
                {
                    if (!_originalMaterials.ContainsKey(renderer))
                    {
                        _originalMaterials[renderer] = renderer.materials;
                        Material[] wireMats = new Material[renderer.materials.Length];
                        for (int i = 0; i < wireMats.Length; i++)
                            wireMats[i] = _wireframeMaterial;
                        renderer.materials = wireMats;
                    }
                }
            }
            else
            {
                foreach (var kvp in _originalMaterials)
                    if (kvp.Key != null) kvp.Key.materials = kvp.Value;
                _originalMaterials.Clear();
            }
        }
        
        #endregion
        
        #region Helper Methods for Renderers
        
        private LineRenderer GetOrCreateLineRenderer(Dictionary<int, LineRenderer> dict, int id, string name, Color color, float width)
        {
            if (!dict.ContainsKey(id))
            {
                GameObject go = new GameObject(name);
                go.transform.SetParent(transform);
                LineRenderer lr = go.AddComponent<LineRenderer>();
                lr.startWidth = width;
                lr.endWidth = width;
                lr.material = new Material(Shader.Find("Sprites/Default"));
                lr.startColor = color;
                lr.endColor = color;
                dict[id] = lr;
            }
            dict[id].enabled = true;
            return dict[id];
        }
        
        private void HideRenderer(Dictionary<int, LineRenderer> dict, int id)
        {
            if (dict.ContainsKey(id)) dict[id].enabled = false;
        }
        
        private void UpdateTrajectoryRenderer(int id, LineRenderer renderer)
        {
            var history = GetPositionHistory(id);
            if (history.Count < 2)
            {
                renderer.positionCount = 0;
                return;
            }
            renderer.positionCount = history.Count;
            Vector3[] points = new Vector3[history.Count];
            for (int i = 0; i < history.Count; i++)
                points[i] = history[i] + Vector3.up * 0.1f;
            renderer.SetPositions(points);
        }
        
        private void UpdatePlannedPathRenderer(Robot robot, LineRenderer renderer)
        {
            // For now, just show a straight line to goal if exists
            if (robot.HasGoal)
            {
                renderer.positionCount = 2;
                renderer.SetPosition(0, robot.Position + Vector3.up * 0.2f);
                renderer.SetPosition(1, robot.Goal + Vector3.up * 0.2f);
                renderer.enabled = true;
            }
            else
            {
                renderer.enabled = false;
            }
        }
        
        private GameObject GetOrCreateGoalMarker(int id, Color color)
        {
            if (!_goalMarkers.ContainsKey(id))
            {
                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = $"Goal_{id}";
                marker.transform.SetParent(transform);
                marker.transform.localScale = Vector3.one * 0.3f;
                var rend = marker.GetComponent<Renderer>();
                // rend.material = new Material(Shader.Find("Standard"));
                rend.material = new Material(Shader.Find("HDRP/Lit"));
                rend.material.color = color;
                Destroy(marker.GetComponent<Collider>());
                _goalMarkers[id] = marker;
            }
            else
            {
                _goalMarkers[id].GetComponent<Renderer>().material.color = color;
            }
            return _goalMarkers[id];
        }
        
        private GameObject GetOrCreateInteractionRadius(int id)
        {
            if (!_interactionRadiusVisuals.ContainsKey(id))
            {
                GameObject go = new GameObject($"Radius_{id}");
                go.transform.SetParent(transform);
                LineRenderer lr = go.AddComponent<LineRenderer>();
                lr.startWidth = 0.03f;
                lr.endWidth = 0.03f;
                lr.loop = true;
                lr.useWorldSpace = false;
                lr.material = new Material(Shader.Find("Sprites/Default"));
                lr.startColor = new Color(1, 1, 0, 0.5f);
                lr.endColor = new Color(1, 1, 0, 0.5f);
                int segments = 32;
                lr.positionCount = segments + 1;
                Vector3[] points = new Vector3[segments + 1];
                for (int i = 0; i <= segments; i++)
                {
                    float ang = i * 2 * Mathf.PI / segments;
                    points[i] = new Vector3(Mathf.Cos(ang), 0.05f, Mathf.Sin(ang));
                }
                lr.SetPositions(points);
                _interactionRadiusVisuals[id] = go;
            }
            return _interactionRadiusVisuals[id];
        }
        
        private void HideInteractionRadius(int id)
        {
            if (_interactionRadiusVisuals.ContainsKey(id))
                _interactionRadiusVisuals[id].SetActive(false);
        }
        
        private TextMesh GetOrCreateAgentIdLabel(int id, string defaultText, Color color)
        {
            if (!_agentIdLabels.ContainsKey(id))
            {
                GameObject go = new GameObject($"Label_{id}");
                go.transform.SetParent(transform);
                TextMesh tm = go.AddComponent<TextMesh>();
                tm.text = defaultText;
                tm.fontSize = 24;
                tm.characterSize = 0.2f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.fontStyle = FontStyle.Bold;
                tm.color = color;
                
                // Ajouter le script Billboard pour que le texte regarde toujours la caméra
                Billboard billboard = go.AddComponent<Billboard>();
                
                _agentIdLabels[id] = tm;
            }
            return _agentIdLabels[id];
        }
        
        #endregion
        
        #region Data Extraction from Agents
        
        private Vector3 GetHumanGoal(HumanAgent human)
        {
            var movement = human.GetComponent<HumanMovement>();
            if (movement != null && movement.HasGoal)
            {
                Vector2 goal2D = movement.GoalPosition2D;
                return new Vector3(goal2D.x, 0, goal2D.y);
            }
            return Vector3.zero;
        }
        
        private Vector3 GetHumanVelocity(HumanAgent human)
        {
            var rb = human.GetComponent<Rigidbody>();
            if (rb != null) return rb.linearVelocity;
            return Vector3.zero;
        }
        
        private float GetHumanInteractionRadius(HumanAgent human)
        {
            return human.InteractionRadius;
        }
        
        private Color GetHumanColor(HumanAgent human)
        {
            if (!colorHumansByState) return humanDefaultColor;
            // simple distance to robot
            Robot robot = FindObjectOfType<Robot>();
            if (robot != null)
            {
                float dist = Vector3.Distance(human.transform.position, robot.Position);
                if (dist < 1.5f) return humanDangerColor;
                if (dist < 3f) return humanAlertColor;
            }
            return humanDefaultColor;
        }
        
        private void SetHumanOpacity(HumanAgent human, float opacity)
        {
            var renderers = human.GetComponentsInChildren<Renderer>();
            foreach (var rend in renderers)
                foreach (var mat in rend.materials)
                {
                    Color c = mat.color;
                    c.a = opacity;
                    mat.color = c;
                }
        }
        
        private void UpdateLaserScanVisualization(Robot robot)
        {
            if (_robotLaserScanner == null)
                _robotLaserScanner = robot.GetComponentInChildren<RaycastLaserScanner>();
            if (_robotLaserScanner == null || !_robotLaserScanner.isActiveAndEnabled)
            {
                _laserScanRenderer.enabled = false;
                return;
            }

            if (Time.time - _lastLaserUpdateTime > 0.05f)
            {
                _lastLaserUpdateTime = Time.time;
                _laserScanPoints.Clear();
                Vector3 origin = robot.Position + Vector3.up * 0.2f;
                _laserScanPoints.Add(origin);

                int count = _robotLaserScanner.samples;
                float angleStep = 360f / count;
                for (int i = 0; i < count; i++)
                {
                    float angle = i * angleStep * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    if (Physics.Raycast(origin, dir, out RaycastHit hit, _robotLaserScanner.range_max))
                        _laserScanPoints.Add(hit.point);
                    else
                        _laserScanPoints.Add(origin + dir * _robotLaserScanner.range_max);
                }
                _laserScanRenderer.positionCount = _laserScanPoints.Count;
                _laserScanRenderer.SetPositions(_laserScanPoints.ToArray());
            }
            _laserScanRenderer.enabled = true;
        }
        
        private void DrawBoundingBoxes()
        {
            var agents = FindObjectsOfType<HumanAgent>();
            foreach (var a in agents)
            {
                Vector3 center = a.transform.position;
                Vector3 size = new Vector3(0.5f, 1.5f, 0.5f);
                DrawWireCube(center, size, Color.white);
            }
        }

        private void DrawWireCube(Vector3 center, Vector3 size, Color color)
        {
            Vector3 half = size / 2;
            Vector3 p1 = center + new Vector3(-half.x, -half.y, -half.z);
            Vector3 p2 = center + new Vector3( half.x, -half.y, -half.z);
            Vector3 p3 = center + new Vector3( half.x, -half.y,  half.z);
            Vector3 p4 = center + new Vector3(-half.x, -half.y,  half.z);
            Vector3 p5 = center + new Vector3(-half.x,  half.y, -half.z);
            Vector3 p6 = center + new Vector3( half.x,  half.y, -half.z);
            Vector3 p7 = center + new Vector3( half.x,  half.y,  half.z);
            Vector3 p8 = center + new Vector3(-half.x,  half.y,  half.z);

            Debug.DrawLine(p1, p2, color);
            Debug.DrawLine(p2, p3, color);
            Debug.DrawLine(p3, p4, color);
            Debug.DrawLine(p4, p1, color);
            Debug.DrawLine(p5, p6, color);
            Debug.DrawLine(p6, p7, color);
            Debug.DrawLine(p7, p8, color);
            Debug.DrawLine(p8, p5, color);
            Debug.DrawLine(p1, p5, color);
            Debug.DrawLine(p2, p6, color);
            Debug.DrawLine(p3, p7, color);
            Debug.DrawLine(p4, p8, color);
        }
        
        private void UpdateDistanceLabels()
        {
            // optional: distance between robot and each human
            Robot robot = FindObjectOfType<Robot>();
            if (robot == null) return;
            var humans = FindObjectsOfType<HumanAgent>();
            foreach (var h in humans)
            {
                float dist = Vector3.Distance(robot.Position, h.transform.position);
                var label = GetOrCreateAgentIdLabel(h.GetInstanceID(), "dist", Color.white);
                label.transform.position = (robot.Position + h.transform.position) / 2f + Vector3.up * 1.2f;
                label.text = $"{dist:F1}m";
                label.color = Color.white;
                label.gameObject.SetActive(showDistanceLabels);
            }
        }
        
        #endregion
        
        #region Cleanup
        
        private void CleanupVisualizations()
        {
            foreach (var lr in _trajectoryRenderers.Values) if (lr) Destroy(lr.gameObject);
            foreach (var lr in _pathRenderers.Values) if (lr) Destroy(lr.gameObject);
            foreach (var go in _goalMarkers.Values) if (go) Destroy(go);
            foreach (var lr in _velocityVectors.Values) if (lr) Destroy(lr.gameObject);
            foreach (var go in _interactionRadiusVisuals.Values) if (go) Destroy(go);
            foreach (var tm in _agentIdLabels.Values) if (tm) Destroy(tm.gameObject);
            if (_laserScanRenderer) Destroy(_laserScanRenderer.gameObject);
        }
        
        #endregion
    }

    public class Billboard : MonoBehaviour
    {
        void LateUpdate()
        {
            if (Camera.main != null)
            {
                // Faire regarder le texte vers la caméra (le plan du texte sera face à la caméra)
                transform.LookAt(transform.position + Camera.main.transform.rotation * Vector3.forward,
                                Camera.main.transform.rotation * Vector3.up);
            }
        }
    }
}