using UnityEngine;
using System.Collections.Generic;

namespace RobotSNAP.Agents
{
    /// <summary>
    /// Gestionnaire des agents humains pour un environnement donné.
    /// Stocke les positions/vitesses des agents actifs pour une détection rapide des voisins.
    /// Cet objet est détruit avec son environnement, il n'y a pas de persistance globale.
    /// </summary>
    public class HumanManager : MonoBehaviour
    {
        [Header("Performance")]
        [Tooltip("Capacité initiale de la liste (réallouée automatiquement si dépassée)")]
        [SerializeField] private int _initialCapacity = 50;

        // Structure de données d'un agent (pure data)
        private struct AgentData
        {
            public int Id;
            public Vector2 Position;
            public Vector2 Velocity;
            public bool Active;
        }

        // Données internes
        private List<AgentData> _agents = new List<AgentData>();
        private Dictionary<int, int> _idToIndex = new Dictionary<int, int>(); // Id -> index dans la liste
        private int _activeCount = 0;

        // Caches pour les requêtes (évite les allocations)
        private List<Vector2> _tempPositions = new List<Vector2>();
        private List<Vector2> _tempVelocities = new List<Vector2>();

        // Uniform spatial hash of the active agents: a neighbour query then visits a handful of buckets
        // instead of every agent of the crowd.
        private readonly Dictionary<long, List<int>> _buckets = new Dictionary<long, List<int>>();
        private int _bucketFrame = -1;

        [Header("Neighbour query")]
        [Tooltip("Side of a spatial-hash cell, in metres. Around the perception radius works best.")]
        [SerializeField] private float _bucketCellSize = 4f;

        // Pour le debug
        [Header("Debug")]
        [SerializeField] private bool _logEvents = false;

        private void Awake()
        {
            // Pré-allocation pour éviter les réallocations
            _agents.Capacity = _initialCapacity;
        }

        /// <summary>
        /// Enregistre un agent dans le manager (appelé quand l'agent est activé ou créé).
        /// Si l'ID existe déjà, met à jour la position.
        /// </summary>
        public void RegisterAgent(int id, Vector2 initialPosition)
        {
            if (_idToIndex.ContainsKey(id))
            {
                // L'agent est déjà enregistré, on met à jour sa position
                UpdateAgent(id, initialPosition, Vector2.zero);
                if (_logEvents) Debug.Log($"[HumanManager] Agent {id} already registered, updated position.");
                return;
            }

            // Ajouter un nouvel agent
            if (_activeCount >= _agents.Count)
            {
                _agents.Add(new AgentData());
            }

            _agents[_activeCount] = new AgentData
            {
                Id = id,
                Position = initialPosition,
                Velocity = Vector2.zero,
                Active = true
            };
            _idToIndex[id] = _activeCount;
            _activeCount++;
            InvalidateSpatialIndex();

            if (_logEvents) Debug.Log($"[HumanManager] Agent {id} registered at {initialPosition}. Active count: {_activeCount}");
        }

        /// <summary>
        /// Met à jour la position et la vitesse d'un agent enregistré.
        /// </summary>
        public void UpdateAgent(int id, Vector2 position, Vector2 velocity)
        {
            if (_idToIndex.TryGetValue(id, out int index))
            {
                AgentData data = _agents[index];
                data.Position = position;
                data.Velocity = velocity;
                _agents[index] = data;
            }
            else
            {
                // Si l'agent n'est pas enregistré (cas exceptionnel), on l'ajoute
                if (_logEvents) Debug.LogWarning($"[HumanManager] Agent {id} not found in dictionary, registering now.");
                RegisterAgent(id, position);
                UpdateAgent(id, position, velocity);
            }
        }

        /// <summary>
        /// Désenregistre un agent (appelé quand il est désactivé ou détruit).
        /// </summary>
        public void UnregisterAgent(int id)
        {
            if (!_idToIndex.TryGetValue(id, out int index))
            {
                if (_logEvents) Debug.LogWarning($"[HumanManager] Agent {id} not found, cannot unregister.");
                return;
            }

            // On remplace l'agent à supprimer par le dernier agent actif (swap with last)
            _activeCount--;
            AgentData lastData = _agents[_activeCount];
            if (index != _activeCount)
            {
                // On déplace le dernier agent à la place de celui qu'on supprime
                _agents[index] = lastData;
                _idToIndex[lastData.Id] = index;
            }

            _idToIndex.Remove(id);
            InvalidateSpatialIndex();

            if (_logEvents) Debug.Log($"[HumanManager] Agent {id} unregistered. Active count: {_activeCount}");
        }

        /// <summary>
        /// Remplit les listes avec les positions et vitesses des voisins dans le rayon donné.
        /// Les listes sont vidées avant d'être remplies.
        /// </summary>
        public void GetNeighbors(int selfId, Vector2 center, float radius,
                                 List<Vector2> outPositions, List<Vector2> outVelocities)
        {
            GetNeighbors(selfId, center, radius, outPositions, outVelocities, null);
        }

        /// <summary>
        /// Same query, but also returns the id of each neighbour so a caller can ignore the agents it walks
        /// with — a group member yields to strangers, not to its own companions.
        /// </summary>
        public void GetNeighbors(int selfId, Vector2 center, float radius,
                                 List<Vector2> outPositions, List<Vector2> outVelocities, List<int> outIds)
        {
            outPositions.Clear();
            outVelocities.Clear();
            outIds?.Clear();

            if (_activeCount == 0)
                return;

            // The index is rebuilt at most once per frame: agents refresh their position several times per
            // frame, and rebuilding it on each call would put the quadratic cost straight back.
            if (_bucketFrame != Time.frameCount)
                RebuildSpatialIndex();

            float cellSize = Mathf.Max(1f, _bucketCellSize);
            float radiusSqr = radius * radius;
            int minX = Mathf.FloorToInt((center.x - radius) / cellSize);
            int maxX = Mathf.FloorToInt((center.x + radius) / cellSize);
            int minY = Mathf.FloorToInt((center.y - radius) / cellSize);
            int maxY = Mathf.FloorToInt((center.y + radius) / cellSize);

            for (int cellX = minX; cellX <= maxX; cellX++)
            {
                for (int cellY = minY; cellY <= maxY; cellY++)
                {
                    if (!_buckets.TryGetValue(CellKey(cellX, cellY), out List<int> bucket))
                        continue;

                    for (int entry = 0; entry < bucket.Count; entry++)
                    {
                        AgentData data = _agents[bucket[entry]];
                        if (data.Id == selfId || !data.Active)
                            continue;

                        float distSqr = (data.Position - center).sqrMagnitude;
                        if (distSqr >= radiusSqr || distSqr <= 0.0001f)
                            continue;

                        outPositions.Add(data.Position);
                        outVelocities.Add(data.Velocity);
                        outIds?.Add(data.Id);
                    }
                }
            }
        }

        /// <summary>
        /// Rebuilds the buckets from the active agents. Cheap enough to run once per frame and far cheaper than
        /// the linear scan it replaces once a crowd grows past a few dozen agents.
        /// </summary>
        private void RebuildSpatialIndex()
        {
            foreach (List<int> bucket in _buckets.Values)
                bucket.Clear();

            float cellSize = Mathf.Max(1f, _bucketCellSize);
            for (int index = 0; index < _activeCount; index++)
            {
                AgentData data = _agents[index];
                if (!data.Active)
                    continue;

                int cellX = Mathf.FloorToInt(data.Position.x / cellSize);
                int cellY = Mathf.FloorToInt(data.Position.y / cellSize);
                long key = CellKey(cellX, cellY);
                if (!_buckets.TryGetValue(key, out List<int> bucket))
                {
                    bucket = new List<int>(8);
                    _buckets[key] = bucket;
                }

                bucket.Add(index);
            }

            _bucketFrame = Time.frameCount;
        }

        private void InvalidateSpatialIndex() => _bucketFrame = -1;

        /// <summary>Key of a cell in the uniform hash: two signed ints packed side by side.</summary>
        private static long CellKey(int cellX, int cellY) => ((long)cellX << 32) ^ (uint)cellY;

        /// <summary>
        /// Vide complètement le manager (tous les agents sont supprimés).
        /// </summary>
        public void ClearAll()
        {
            _agents.Clear();
            _idToIndex.Clear();
            _activeCount = 0;
            _tempPositions.Clear();
            _tempVelocities.Clear();
            if (_logEvents) Debug.Log("[HumanManager] Cleared all agent data.");
        }

        /// <summary>
        /// Retourne le nombre d'agents actifs actuellement enregistrés.
        /// </summary>
        public int ActiveAgentCount => _activeCount;

        // Optionnel : méthode pour obtenir un agent par son ID (pour debug)
        public bool TryGetAgentData(int id, out Vector2 position, out Vector2 velocity)
        {
            if (_idToIndex.TryGetValue(id, out int index))
            {
                AgentData data = _agents[index];
                position = data.Position;
                velocity = data.Velocity;
                return true;
            }
            position = Vector2.zero;
            velocity = Vector2.zero;
            return false;
        }

        #region Debug

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;
            // Optionnel : afficher les positions des agents enregistrés
            Gizmos.color = Color.green;
            for (int i = 0; i < _activeCount; i++)
            {
                AgentData data = _agents[i];
                Vector3 pos3D = new Vector3(data.Position.x, 0.1f, data.Position.y);
                Gizmos.DrawWireSphere(pos3D, 0.1f);
            }
        }

        #endregion
    }
}
