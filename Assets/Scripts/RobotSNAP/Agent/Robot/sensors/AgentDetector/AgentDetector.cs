using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace RobotSNAP
{
    /// <summary>
    /// Pure agent detector - No ROS dependencies.
    /// Handles field of view detection, raycasting, and agent tracking.
    /// </summary>
    public class AgentDetector : MonoBehaviour
    {
        [Header("Detection Settings")]
        [SerializeField] private float radius = 5f;
        [SerializeField] [Range(0, 360)] private float angle = 90f;
        
        [Header("Filtering")]
        [SerializeField] private string targetTag = "";
        [SerializeField] private string[] targetTags = new string[] { };
        
        [Header("Layer Masks")]
        [SerializeField] private LayerMask targetMask = ~0;
        [SerializeField] private LayerMask obstructionMask = ~0;
        
        [Header("Performance")]
        [SerializeField] private float updateInterval = 0.2f;
        [SerializeField] private bool autoStart = true;
        
        [Header("Debug")]
        [SerializeField] private bool showGizmos = true;
        [SerializeField] private bool logDetections = false;
        
        // Events for external systems (ROS, UI, etc.)
        public event Action<GameObject, bool> OnAgentVisibilityChanged;
        public event Action<List<GameObject>> OnVisibleAgentsUpdated;
        public event Action<GameObject, float, Vector3> OnAgentDetected;
        
        // Public properties
        public float Radius => radius;
        public float Angle => angle;
        public IReadOnlyDictionary<GameObject, bool> AgentsView => _agentsView;
        public List<GameObject> VisibleAgents => GetVisibleAgents();
        public int VisibleCount => _agentsView.Count(kv => kv.Value);
        
        // Private state
        private Dictionary<GameObject, bool> _agentsView = new Dictionary<GameObject, bool>();
        private GameObject _agentPool;
        private bool _isRunning;
        private WaitForSeconds _waitInterval;
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            _waitInterval = new WaitForSeconds(updateInterval);
        }
        
        private void Start()
        {
            if (autoStart)
            {
                StartDetection();
            }
        }
        
        private void OnDestroy()
        {
            StopDetection();
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Start the detection routine
        /// </summary>
        public void StartDetection()
        {
            if (_isRunning) return;
            _isRunning = true;
            StartCoroutine(DetectionRoutine());
        }
        
        /// <summary>
        /// Stop the detection routine
        /// </summary>
        public void StopDetection()
        {
            _isRunning = false;
            StopCoroutine(DetectionRoutine());
        }
        
        /// <summary>
        /// Reset the detector state
        /// </summary>
        public void Reset()
        {
            ClearAllAgents();
            if (autoStart)
            {
                StartDetection();
            }
        }
        
        /// <summary>
        /// Set the agent pool (where to find agents to detect)
        /// </summary>
        public void SetAgentPool(GameObject agentPool)
        {
            _agentPool = agentPool;
            RefreshAgentList();
            Debug.Log($"[{name}] Agent pool set to: {_agentPool?.name ?? "null"}");
        }
        
        /// <summary>
        /// Refresh the list of agents to detect
        /// </summary>
        public void RefreshAgentList()
        {
            ClearAllAgents();
            Debug.Log($"[{name}] Refreshing agent list from pool: {_agentPool?.name ?? "null"}");
            if (_agentPool == null) return;
            
            // Find all children with matching tags
            foreach (Transform child in _agentPool.transform)
            {
                if (IsTargetTag(child.gameObject.tag))
                {
                    _agentsView[child.gameObject] = false;
                }
            }
            
            if (logDetections)
            {
                Debug.Log($"[{name}] Refreshed agent list: {_agentsView.Count} agents found");
            }
        }
        
        /// <summary>
        /// Force an immediate detection check
        /// </summary>
        public void ForceDetectionCheck()
        {
            PerformDetectionCheck();
        }
        
        /// <summary>
        /// Get the closest visible agents
        /// </summary>
        public List<GameObject> GetClosestAgents(int count)
        {
            var visible = GetVisibleAgents();
            if (visible.Count == 0) return new List<GameObject>();
            
            var distances = new Dictionary<GameObject, float>();
            foreach (var agent in visible)
            {
                float dist = Vector3.Distance(transform.position, agent.transform.position);
                distances[agent] = dist;
            }
            
            return distances.OrderBy(kv => kv.Value)
                           .Take(Mathf.Min(count, visible.Count))
                           .Select(kv => kv.Key)
                           .ToList();
        }
        
        /// <summary>
        /// Get the distance to an agent
        /// </summary>
        public float GetDistanceToAgent(GameObject agent)
        {
            if (agent == null) return float.MaxValue;
            return Vector3.Distance(transform.position, agent.transform.position);
        }
        
        /// <summary>
        /// Get the direction to an agent
        /// </summary>
        public Vector3 GetDirectionToAgent(GameObject agent)
        {
            if (agent == null) return Vector3.zero;
            return (agent.transform.position - transform.position).normalized;
        }
        
        /// <summary>
        /// Check if an agent is currently visible
        /// </summary>
        public bool IsAgentVisible(GameObject agent)
        {
            return _agentsView.TryGetValue(agent, out bool visible) && visible;
        }
        
        #endregion
        
        #region Private Methods
        
        private IEnumerator DetectionRoutine()
        {
            while (_isRunning)
            {
                PerformDetectionCheck();
                yield return _waitInterval;
            }
        }
        
        private void PerformDetectionCheck()
        {
            // Early exit if no agents
            if (_agentsView.Count == 0)
            {
                if (logDetections) Debug.Log($"[{name}] No agents to detect");
                return;
            }
            
            // Reset all to false first
            ResetVisibility();
            
            // Find potential targets within radius
            Collider[] hits = Physics.OverlapSphere(transform.position, radius, targetMask);
            var detectedThisFrame = new HashSet<GameObject>();
            
            foreach (Collider hit in hits)
            {
                GameObject target = hit.gameObject;
                
                // Skip self and non-target tags
                if (target == gameObject || !IsTargetTag(target.tag))
                    continue;
                
                // Check if this agent is in our view dictionary
                if (!_agentsView.ContainsKey(target))
                    continue;
                
                // Check field of view
                Vector3 targetPosition = target.transform.position + Vector3.up * 0.5f; // Eye level
                Vector3 directionToTarget = (targetPosition - transform.position).normalized;
                
                if (Vector3.Angle(transform.forward, directionToTarget) > angle / 2)
                    continue;
                
                // Check line of sight
                float distanceToTarget = Vector3.Distance(transform.position, targetPosition);
                
                bool hasLineOfSight = !Physics.Raycast(
                    transform.position, 
                    directionToTarget, 
                    distanceToTarget, 
                    obstructionMask
                );
                
                Debug.Log($"[{name}] Checking {target.name}: Distance={distanceToTarget:F2}, LOS={hasLineOfSight}");

                if (hasLineOfSight && distanceToTarget <= radius)
                {
                    _agentsView[target] = true;
                    detectedThisFrame.Add(target);
                    
                    OnAgentDetected?.Invoke(target, distanceToTarget, directionToTarget);
                    
                    if (logDetections)
                    {
                        Debug.Log($"[{name}] Detected: {target.name} at {distanceToTarget:F2}m");
                    }
                }
            }
            
            // Notify about visibility changes
            NotifyVisibilityChanges(detectedThisFrame);
        }
        
        private void ResetVisibility()
        {
            foreach (var key in _agentsView.Keys.ToList())
            {
                _agentsView[key] = false;
            }
        }
        
        private void NotifyVisibilityChanges(HashSet<GameObject> newlyVisible)
        {
            var changedAgents = new List<GameObject>();
            
            foreach (var agent in _agentsView.Keys)
            {
                bool isVisible = _agentsView[agent];
                
                // Notify individual changes
                OnAgentVisibilityChanged?.Invoke(agent, isVisible);
                
                if (isVisible)
                {
                    changedAgents.Add(agent);
                }
            }
            
            // Notify list of all visible agents
            OnVisibleAgentsUpdated?.Invoke(changedAgents);
        }
        
        private void ClearAllAgents()
        {
            var keys = _agentsView.Keys.ToList();
            foreach (var agent in keys)
            {
                OnAgentVisibilityChanged?.Invoke(agent, false);
            }
            _agentsView.Clear();
        }
        
        private List<GameObject> GetVisibleAgents()
        {
            return _agentsView.Where(kv => kv.Value)
                             .Select(kv => kv.Key)
                             .ToList();
        }
        
        private bool IsTargetTag(string tag)
        {
            // Check single tag
            if (!string.IsNullOrEmpty(targetTag) && tag == targetTag)
                return true;
            
            // Check tag array
            if (targetTags != null && targetTags.Length > 0)
            {
                foreach (string t in targetTags)
                {
                    if (tag == t) return true;
                }
            }
            
            return false;
        }
        
        #endregion
        
        #region Editor & Debug
        
        private void OnDrawGizmosSelected()
        {
            if (!showGizmos) return;
            
            // Draw field of view
            Gizmos.color = Color.yellow;
            DrawFOV();
            
            // Draw detection radius
            Gizmos.color = new Color(0, 1, 0, 0.3f);
            Gizmos.DrawWireSphere(transform.position, radius);
            
            // Draw lines to visible agents
            if (Application.isPlaying && _agentsView != null)
            {
                Gizmos.color = Color.green;
                foreach (var agent in _agentsView)
                {
                    if (agent.Value && agent.Key != null)
                    {
                        Gizmos.DrawLine(transform.position, agent.Key.transform.position);
                    }
                }
            }
        }
        
        private void DrawFOV()
        {
            float halfAngle = angle / 2;
            float startAngle = -halfAngle;
            float endAngle = halfAngle;
            
            Vector3 forward = transform.forward;
            
            for (float a = startAngle; a <= endAngle; a += 10f)
            {
                Quaternion rotation = Quaternion.Euler(0, a, 0);
                Vector3 direction = rotation * forward;
                Vector3 endPoint = transform.position + direction * radius;
                Gizmos.DrawLine(transform.position, endPoint);
            }
        }
        
        [ContextMenu("Refresh Agent List")]
        private void EditorRefreshAgentList()
        {
            RefreshAgentList();
        }
        
        [ContextMenu("Force Detection Check")]
        private void EditorForceDetection()
        {
            ForceDetectionCheck();
        }
        
        [ContextMenu("Log Visible Agents")]
        private void EditorLogVisibleAgents()
        {
            var visible = GetVisibleAgents();
            Debug.Log($"[{name}] Visible agents: {visible.Count}");
            foreach (var agent in visible)
            {
                Debug.Log($"  - {agent.name} at {GetDistanceToAgent(agent):F2}m");
            }
        }
        
        #endregion
    }
}