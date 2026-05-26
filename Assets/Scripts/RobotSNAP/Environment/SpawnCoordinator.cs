using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RobotSNAP.Core.Scenario;

namespace RobotSNAP.Core
{
    /// <summary>
    /// Coordinateur des spawns - Décide où et quoi spawner
    /// </summary>
    public class SpawnCoordinator : MonoBehaviour, IResettable
    {
        [Header("Spawn Constraints")]
        [SerializeField] private float _minPathLength = 3f;
        [SerializeField] private float _maxPathLength = 0f;
        [SerializeField] private float _minAgentDistance = 2f;
        [SerializeField] private int _minHumans = 0;
        [SerializeField] private int _maxHumans = 5;

        // Propriétés publiques
        public float MinPathLength { get => _minPathLength; set => _minPathLength = value; }
        public float MaxPathLength { get => _maxPathLength; set => _maxPathLength = value; }
        public float MinAgentDistance { get => _minAgentDistance; set => _minAgentDistance = value; }
        public int MinHumans { get => _minHumans; set => _minHumans = value; }
        public int MaxHumans { get => _maxHumans; set => _maxHumans = value; }
        
        [Header("Prefabs")]
        [SerializeField] private GameObject _robotPrefab;
        [SerializeField] private GameObject _goalPrefab;

        [Header("Debug")]
        [SerializeField] private bool _logEvents = true;
        
        private INavMeshManager _navMesh;
        private HumanPoolManager _humanPool;
        private GameObject _currentRobot;
        private GameObject _currentGoal;
        private List<SpawnData> _pendingSpawns = new List<SpawnData>();
        private bool _isSpawning;
        private ScenarioData _currentScenario;
        
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
        /// Définit le scénario courant pour le spawn
        /// </summary>
        public void SetScenario(ScenarioData scenario)
        {
            _currentScenario = scenario;
            
            if (_logEvents)
                Debug.Log($"[SpawnCoordinator] Scenario set: {scenario?.Name ?? "none"}");
        }
        
        /// <summary>
        /// Public method to trigger spawn of all agents
        /// </summary>
        public IEnumerator SpawnAll()
        {
            yield return GenerateAllSpawns();
        }
        
        /// <summary>
        /// Spawn uniquement les humains (pour le scenario applier)
        /// </summary>
        public IEnumerator SpawnHumansOnly()
        {
            if (_isSpawning) yield break;
            _isSpawning = true;
            
            // Retourner les humains existants au pool
            if (_humanPool != null)
            {
                _humanPool.ReturnAllHumans();
            }
            
            // Générer les humains selon le scénario ou les paramètres par défaut
            if (_currentScenario != null && _currentScenario.Humans != null && _currentScenario.Humans.Count > 0)
            {
                yield return StartCoroutine(SpawnHumansFromScenario());
            }
            else
            {
                yield return StartCoroutine(SpawnDefaultHumans());
            }
            
            _isSpawning = false;
        }
        
        private IEnumerator SpawnHumansFromScenario()
        {
            int totalHumans = 0;
            foreach (var config in _currentScenario.Humans)
            {
                totalHumans += config.Count;
            }
            
            if (_logEvents)
                Debug.Log($"[SpawnCoordinator] Spawning {totalHumans} humans from scenario");
            
            int humanIndex = 0;
            foreach (var config in _currentScenario.Humans)
            {
                for (int i = 0; i < config.Count; i++)
                {
                    SpawnData humanSpawn = GenerateHumanSpawnFromConfig(config, i, config.Count, humanIndex);
                    
                    GameObject human = _humanPool.GetHuman(config);
                    if (human != null)
                    {
                        human.transform.SetPositionAndRotation(humanSpawn.position, humanSpawn.rotation);
                        var controller = human.GetComponent<IHumanController>();
                        controller?.SetGoal(humanSpawn.goalPosition);
                        human.SetActive(true);
                    }
                    
                    humanIndex++;
                    
                    if (humanIndex % 5 == 0)
                        yield return null;
                }
            }
            
            if (_logEvents)
                Debug.Log($"[SpawnCoordinator] Spawned {humanIndex} humans from scenario");
        }
        
        private SpawnData GenerateHumanSpawnFromConfig(HumanScenarioConfig config, int index, int total, int globalIndex)
        {
            float radius = _navMesh != null ? 10f : 10f;
            Vector3 startPos = GetPositionFromConfig(config.Spawn, index, total, globalIndex);
            
            // Si la position n'a pas été résolue, utiliser un point aléatoire
            if (startPos == Vector3.zero)
            {
                startPos = _navMesh?.GetRandomSpawnPoint(radius) ?? Random.insideUnitSphere * radius;
                startPos.y = 0;
            }
            
            Vector3 goalPos = GetGoalFromConfig(config.Goal, startPos);
            
            return new SpawnData
            {
                position = startPos,
                rotation = Quaternion.identity,
                goalPosition = goalPos,
                goalRotation = Quaternion.identity,
                id = config.Id,
                type = SpawnType.Human
            };
        }
        
        private Vector3 GetPositionFromConfig(SpawnConfig spawn, int index, int total, int globalIndex)
        {
            if (spawn == null) return Vector3.zero;
            
            switch (spawn.Type?.ToLower())
            {
                case "point":
                    return ResolvePointReference(spawn.Reference);
                    
                case "random":
                    Bounds bounds = ResolveBoundsReference(spawn.Reference);
                    if (bounds.size != Vector3.zero)
                    {
                        return new Vector3(
                            Random.Range(bounds.min.x, bounds.max.x),
                            bounds.center.y,
                            Random.Range(bounds.min.z, bounds.max.z)
                        );
                    }
                    break;
                    
                case "formation":
                    if (spawn.Formation == "line")
                    {
                        float offset = (index - (total - 1) / 2f) * spawn.Spacing;
                        Vector3 center = GetFormationCenter(spawn);
                        return center + new Vector3(offset, 0, 0);
                    }
                    else if (spawn.Formation == "circle")
                    {
                        float angle = (index / (float)total) * Mathf.PI * 2;
                        Vector3 center = GetFormationCenter(spawn);
                        return center + new Vector3(Mathf.Cos(angle) * spawn.Spacing, 0, Mathf.Sin(angle) * spawn.Spacing);
                    }
                    break;
            }
            
            return Vector3.zero;
        }
        
        private Vector3 ResolvePointReference(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return Vector3.zero;
            
            // Chercher dans les points du scénario
            if (_currentScenario != null && _currentScenario.Points != null)
            {
                if (_currentScenario.Points.TryGetValue(reference, out var point))
                {
                    return point.ToVector3();
                }
            }
            
            // Chercher un GameObject avec ce tag
            GameObject go = GameObject.FindWithTag(reference);
            if (go != null) return go.transform.position;
            
            return Vector3.zero;
        }
        
        private Bounds ResolveBoundsReference(string reference)
        {
            if (string.IsNullOrEmpty(reference)) return new Bounds();
            
            if (_currentScenario != null && _currentScenario.Points != null)
            {
                if (_currentScenario.Points.TryGetValue(reference, out var point) && point.IsBounds)
                {
                    return point.ToBounds();
                }
            }
            
            return new Bounds();
        }
        
        private Vector3 GetFormationCenter(SpawnConfig spawn)
        {
            if (spawn.RelativeTo == "robot" && _currentRobot != null)
                return _currentRobot.transform.position;
            else if (!string.IsNullOrEmpty(spawn.RelativeTo))
                return ResolvePointReference(spawn.RelativeTo);
            
            return Vector3.zero;
        }
        
        private Vector3 GetGoalFromConfig(GoalConfig goal, Vector3 startPos)
        {
            if (goal == null) return Vector3.zero;
            
            switch (goal.Type?.ToLower())
            {
                case "point":
                    return ResolvePointReference(goal.Reference);
                    
                case "random":
                    Bounds bounds = ResolveBoundsReference(goal.Reference);
                    if (bounds.size != Vector3.zero)
                    {
                        return new Vector3(
                            Random.Range(bounds.min.x, bounds.max.x),
                            bounds.center.y,
                            Random.Range(bounds.min.z, bounds.max.z)
                        );
                    }
                    break;
                    
                case "wander":
                case "follow":
                case "stay":
                    // Ces types seront gérés par le controller
                    return startPos;
            }
            
            return Vector3.zero;
        }
        
        private IEnumerator SpawnDefaultHumans()
        {
            int humanCount = Random.Range(_minHumans, _maxHumans + 1);
            List<SpawnData> existing = new List<SpawnData>();
            
            // Ajouter le robot aux existing pour éviter les collisions
            if (_currentRobot != null)
            {
                existing.Add(new SpawnData { position = _currentRobot.transform.position });
            }
            
            for (int i = 0; i < humanCount; i++)
            {
                SpawnData humanSpawn = GenerateDefaultHumanSpawn(i, existing);
                existing.Add(humanSpawn);
                
                GameObject human = _humanPool.GetHuman();
                if (human != null)
                {
                    human.transform.SetPositionAndRotation(humanSpawn.position, humanSpawn.rotation);
                    var controller = human.GetComponent<IHumanController>();
                    controller?.SetGoal(humanSpawn.goalPosition);
                    human.SetActive(true);
                }
                
                if (i % 5 == 0)
                    yield return null;
            }
            
            if (_logEvents)
                Debug.Log($"[SpawnCoordinator] Spawned {humanCount} default humans");
        }
        
        private SpawnData GenerateDefaultHumanSpawn(int index, List<SpawnData> existing)
        {
            float radius = _navMesh != null ? 10f : 10f;
            Vector3 startPos;
            int attempts = 0;
            
            do
            {
                startPos = _navMesh?.GetRandomSpawnPoint(radius) ?? Random.insideUnitSphere * radius;
                startPos.y = 0;
                attempts++;
            } while (IsTooClose(startPos, existing, _minAgentDistance) && attempts < 100);
            
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
        
        private IEnumerator GenerateAllSpawns()
        {
            Debug.Log("[SpawnCoordinator] Generating all spawns...");
            if (_isSpawning) yield break;
            _isSpawning = true;
            
            // Disable existing
            if (_currentRobot != null) _currentRobot.SetActive(false);
            if (_currentGoal != null) _currentGoal.SetActive(false);
            
            // Generate robot spawn
            SpawnData robotSpawn = GenerateRobotSpawn();
            Debug.Log($"[SpawnCoordinator] Generated robot spawn at {robotSpawn.position} with goal at {robotSpawn.goalPosition}");
            
            // Spawn robot
            yield return SpawnRobot(robotSpawn);
            
            // Spawn humans
            yield return StartCoroutine(SpawnHumansOnly());
            
            EventBus.Instance.Publish(new SpawnCompletedEvent
            {
                spawnedObjects = new[] { _currentRobot, _currentGoal },
                type = SpawnType.All
            });
            
            ActivateRobotAndGoal();

            Debug.Log($"[SpawnCoordinator] All spawns generated. Robot at {robotSpawn.position}, Goal at {robotSpawn.goalPosition}");
            
            _isSpawning = false;
        }
        
        private SpawnData GenerateRobotSpawn()
        {
            float radius = _navMesh != null ? 10f : 10f;
            
            // Vérifier si le scénario donne une position pour le robot
            Vector3 startPos = Vector3.zero;
            if (_currentScenario?.Robot != null && !string.IsNullOrEmpty(_currentScenario.Robot.StartRef))
            {
                startPos = ResolvePointReference(_currentScenario.Robot.StartRef);
            }
            
            if (startPos == Vector3.zero)
            {
                startPos = _navMesh?.GetRandomSpawnPoint(radius) ?? Random.insideUnitSphere * radius;
                startPos.y = 0;
            }
            
            float randomY = Random.Range(0f, 360f);
            Quaternion startRotation = Quaternion.Euler(0f, randomY, 0f);
            
            Vector3 goalPos = Vector3.zero;
            if (_currentScenario?.Robot != null && !string.IsNullOrEmpty(_currentScenario.Robot.GoalRef))
            {
                goalPos = ResolvePointReference(_currentScenario.Robot.GoalRef);
            }
            
            if (goalPos == Vector3.zero)
            {
                goalPos = GetValidGoal(startPos, radius);
            }
            
            return new SpawnData
            {
                position = startPos,
                rotation = startRotation,
                goalPosition = goalPos,
                goalRotation = Quaternion.identity,
                id = "robot",
                type = SpawnType.Robot
            };
        }
        
        private Vector3 GetValidGoal(Vector3 start, float radius, List<SpawnData> existing = null)
        {
            for (int i = 0; i < 100; i++)
            {
                Vector3 goal = _navMesh?.GetRandomNavigationPoint(radius) ?? Random.insideUnitSphere * radius;
                goal.y = 0;
                
                float straightDist = Vector3.Distance(start, goal);
                float pathLen = _navMesh?.GetPathLength(start, goal) ?? straightDist;
                
                bool validPath = straightDist >= _minPathLength &&
                                 (_maxPathLength <= 0 || straightDist <= _maxPathLength) &&
                                 pathLen >= _minPathLength;
                
                bool validDistance = existing == null || !IsTooClose(goal, existing, _minAgentDistance);
                
                if (validPath && validDistance)
                {
                    return goal;
                }
            }
            
            return start + Vector3.right * _minPathLength;
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
            if (_robotPrefab != null)
            {
                if (_currentRobot != null)
                {
                    Destroy(_currentRobot);
                }
                
                _currentRobot = Instantiate(_robotPrefab, data.position, data.rotation);
                _currentRobot.transform.parent = transform;
                _currentRobot.SetActive(false);
                
                // Appliquer la vitesse et le comportement du scénario
                if (_currentScenario?.Robot != null)
                {
                    var robot = _currentRobot.GetComponent<Robot>();
                    if (robot != null)
                    {
                        robot.SetSpeed(_currentScenario.Robot.Speed);
                        robot.SetBehavior(_currentScenario.Robot.Behavior);
                    }
                }
            }
            
            if (_goalPrefab != null)
            {
                if (_currentGoal != null)
                {
                    Destroy(_currentGoal);
                }
                
                _currentGoal = Instantiate(_goalPrefab, data.goalPosition, data.goalRotation);
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
                
                if (_logEvents)
                {
                    Debug.Log($"[SpawnCoordinator] Robot activated at {_currentRobot.transform.position}");
                }
            }
            
            if (_currentGoal != null)
            {
                _currentGoal.SetActive(true);
                
                if (_logEvents)
                {
                    Debug.Log($"[SpawnCoordinator] Goal activated at {_currentGoal.transform.position}");
                }
            }
        }
        
        public void SetHumanCountRange(int min, int max)
        {
            _minHumans = min;
            _maxHumans = max;
        }
        
        public void SetPathConstraints(float minLength, float maxLength)
        {
            _minPathLength = minLength;
            _maxPathLength = maxLength;
        }
        
        public void SetMinAgentDistance(float distance)
        {
            _minAgentDistance = distance;
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
        
        public void ClearAll()
        {
            if (_humanPool != null)
            {
                _humanPool.ReturnAllHumans();
            }
            
            if (_currentRobot != null)
            {
                Destroy(_currentRobot);
                _currentRobot = null;
            }
            
            if (_currentGoal != null)
            {
                Destroy(_currentGoal);
                _currentGoal = null;
            }
        }
    }
}