using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RobotSNAP.Core
{
    /// <summary>
    /// Coordinateur des spawns - Décide où et quoi spawner
    /// </summary>
    public class SpawnCoordinator : MonoBehaviour, IResettable
    {
        [Header("Spawn Constraints")]
        [SerializeField] private float minPathLength = 3f;
        [SerializeField] private float maxPathLength = 0f;
        [SerializeField] private float minAgentDistance = 2f;
        [SerializeField] private int minHumans = 0;
        [SerializeField] private int maxHumans = 5;

        // Propriétés publiques
        public float MinPathLength { get => minPathLength; set => minPathLength = value; }
        public float MaxPathLength { get => maxPathLength; set => maxPathLength = value; }
        public float MinAgentDistance { get => minAgentDistance; set => minAgentDistance = value; }
        public int MinHumans { get => minHumans; set => minHumans = value; }
        public int MaxHumans { get => maxHumans; set => maxHumans = value; }
        
        [Header("Prefabs")]
        [SerializeField] private GameObject robotPrefab;
        [SerializeField] private GameObject goalPrefab;

        [Header("Debug")]
        [SerializeField] private bool logSpawnEvents = true;
        
        private INavMeshManager _navMesh;
        private IHumanPool _humanPool;
        private GameObject _currentRobot;
        private GameObject _currentGoal;
        private List<SpawnData> _pendingSpawns = new List<SpawnData>();
        private bool _isSpawning;
        
        private void Awake()
        {
            _navMesh = FindObjectOfType<NavMeshManager>();
            _humanPool = FindObjectOfType<HumanPoolManager>();
            
            EventBus.Instance.Subscribe<NavMeshBuiltEvent>(OnNavMeshBuilt);
            EventBus.Instance.Subscribe<ResetRequestEvent>(OnResetRequest);
        }
        
        private void OnDestroy()
        {
            EventBus.Instance.Unsubscribe<NavMeshBuiltEvent>(OnNavMeshBuilt);
            EventBus.Instance.Unsubscribe<ResetRequestEvent>(OnResetRequest);
        }
        
        private void OnNavMeshBuilt(NavMeshBuiltEvent evt)
        {
            if (evt.success)
            {
                StartCoroutine(GenerateAllSpawns());
            }
        }
        
        private void OnResetRequest(ResetRequestEvent evt)
        {
            StartCoroutine(Reset());
        }
        
        /// <summary>
        /// Public method to trigger spawn of all agents
        /// </summary>
        public IEnumerator SpawnAll()
        {
            yield return GenerateAllSpawns();
        }

        private IEnumerator GenerateAllSpawns()
        {
            if (_isSpawning) yield break;
            _isSpawning = true;
            
            // Disable existing
            if (_currentRobot != null) _currentRobot.SetActive(false);
            if (_currentGoal != null) _currentGoal.SetActive(false);

            if (_humanPool != null)
            {
                _humanPool.DeactivateAllHumans();
            }
            
            List<SpawnData> allSpawns = new List<SpawnData>();
            
            // Generate robot
            SpawnData robotSpawn = GenerateRobotSpawn();
            allSpawns.Add(robotSpawn);
            
            // Generate humans
            int humanCount = Random.Range(minHumans, maxHumans + 1);
            for (int i = 0; i < humanCount; i++)
            {
                SpawnData humanSpawn = GenerateHumanSpawn(i, allSpawns);
                allSpawns.Add(humanSpawn);
            }
            
            // Spawn robot
            yield return SpawnRobot(robotSpawn);
            
            // Spawn humans via pool
            for (int i = 1; i < allSpawns.Count; i++)
            {
                GameObject human = _humanPool.GetHuman();
                if (human != null)
                {
                    human.transform.SetPositionAndRotation(allSpawns[i].position, allSpawns[i].rotation);
                    IHumanController controller = human.GetComponent<IHumanController>();
                    Debug.Log(allSpawns[i].goalPosition);
                    controller?.SetGoal(allSpawns[i].goalPosition);
                    human.SetActive(true);
                }
                yield return null;
            }
            
            EventBus.Instance.Publish(new SpawnCompletedEvent
            {
                spawnedObjects = new[] { _currentRobot, _currentGoal },
                type = SpawnType.All
            });
            
            ActivateRobotAndGoal();

            _isSpawning = false;
        }
        
        private SpawnData GenerateRobotSpawn()
        {
            float radius = _navMesh != null ? 10f : 10f;
            Vector3 startPos = _navMesh?.GetRandomPoint(radius) ?? Random.insideUnitSphere * radius;
            startPos.y = 0;
            
            Vector3 goalPos = GetValidGoal(startPos, radius);
            
            return new SpawnData
            {
                position = startPos,
                rotation = Quaternion.identity,
                goalPosition = goalPos,
                goalRotation = Quaternion.identity,
                id = "robot",
                type = SpawnType.Robot
            };
        }
        
        private SpawnData GenerateHumanSpawn(int index, List<SpawnData> existing)
        {
            float radius = _navMesh != null ? 10f : 10f;
            Vector3 startPos;
            int attempts = 0;
            
            do
            {
                startPos = _navMesh?.GetRandomPoint(radius) ?? Random.insideUnitSphere * radius;
                startPos.y = 0;
                attempts++;
            } while (IsTooClose(startPos, existing, minAgentDistance) && attempts < 100);
            
            Vector3 goalPos = GetValidGoal(startPos, radius, existing);
            
            return new SpawnData
            {
                position = startPos,
                rotation = Quaternion.identity,
                goalPosition = goalPos,
                goalRotation = Quaternion.identity,
                id = $"human_{index}",
                type = SpawnType.Human
            };
        }
        
        private Vector3 GetValidGoal(Vector3 start, float radius, List<SpawnData> existing = null)
        {
            for (int i = 0; i < 100; i++)
            {
                Vector3 goal = _navMesh?.GetRandomPoint(radius) ?? Random.insideUnitSphere * radius;
                goal.y = 0;
                
                float straightDist = Vector3.Distance(start, goal);
                float pathLen = _navMesh?.GetPathLength(start, goal) ?? straightDist;
                
                bool validPath = straightDist >= minPathLength &&
                                 (maxPathLength <= 0 || straightDist <= maxPathLength) &&
                                 pathLen >= minPathLength;
                
                bool validDistance = existing == null || !IsTooClose(goal, existing, minAgentDistance);
                
                if (validPath && validDistance)
                {
                    return goal;
                }
            }
            
            return start + Vector3.right * 5f;
        }
        
        private bool IsTooClose(Vector3 pos, List<SpawnData> existing, float minDistance)
        {
            foreach (SpawnData data in existing)
            {
                if (Vector3.Distance(pos, data.position) < minDistance)
                    return true;
                if (Vector3.Distance(pos, data.goalPosition) < minDistance)
                    return true;
            }
            return false;
        }
        
        private IEnumerator SpawnRobot(SpawnData data)
        {
            if (robotPrefab != null)
            {
                if (_currentRobot != null)
                {
                    Destroy(_currentRobot);
                }
                
                _currentRobot = Instantiate(robotPrefab, data.position, data.rotation);
                _currentRobot.transform.parent = transform;
                _currentRobot.SetActive(false);
            }
            
            if (goalPrefab != null)
            {
                if (_currentGoal != null)
                {
                    Destroy(_currentGoal);
                }
                
                _currentGoal = Instantiate(goalPrefab, data.goalPosition, data.goalRotation);
                _currentGoal.transform.parent = transform;
                _currentGoal.SetActive(false);
            }
            
            yield return null;
        }

        /// <summary>
        /// Activate robot and goal
        /// </summary>
        public void ActivateRobotAndGoal()
        {
            if (_currentRobot != null)
            {
                _currentRobot.SetActive(true);
                var robot = _currentRobot.GetComponent<Robot>();
                robot?.Reset();
                
                if (logSpawnEvents)
                {
                    Debug.Log($"[SpawnCoordinator] Robot activated at {_currentRobot.transform.position}");
                }
            }
            
            if (_currentGoal != null)
            {
                _currentGoal.SetActive(true);
                
                if (logSpawnEvents)
                {
                    Debug.Log($"[SpawnCoordinator] Goal activated at {_currentGoal.transform.position}");
                }
            }
        }
        


        public void SetHumanCountRange(int min, int max)
        {
            minHumans = min;
            maxHumans = max;
        }

        public void SetPathConstraints(float minLength, float maxLength)
        {
            minPathLength = minLength;
            maxPathLength = maxLength;
        }

        public void SetMinAgentDistance(float distance)
        {
            minAgentDistance = distance;
        }
        
        public IEnumerator Reset()
        {
            yield return GenerateAllSpawns();
        }
        
        public GameObject GetRobot() => _currentRobot;
        public GameObject GetGoal() => _currentGoal;
        
        public void SetActive(bool active)
        {
            if (_currentRobot != null) _currentRobot.SetActive(active);
            if (_currentGoal != null) _currentGoal.SetActive(active);
        }
    }
}