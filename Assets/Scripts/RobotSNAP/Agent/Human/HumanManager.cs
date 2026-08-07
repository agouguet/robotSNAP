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

            if (_logEvents) Debug.Log($"[HumanManager] Agent {id} unregistered. Active count: {_activeCount}");
        }

        /// <summary>
        /// Remplit les listes avec les positions et vitesses des voisins dans le rayon donné.
        /// Les listes sont vidées avant d'être remplies.
        /// </summary>
        public void GetNeighbors(int selfId, Vector2 center, float radius,
                                 List<Vector2> outPositions, List<Vector2> outVelocities)
        {
            outPositions.Clear();
            outVelocities.Clear();

            float radiusSqr = radius * radius;

            for (int i = 0; i < _activeCount; i++)
            {
                AgentData data = _agents[i];
                if (data.Id == selfId || !data.Active)
                    continue;

                float distSqr = (data.Position - center).sqrMagnitude;
                if (distSqr < radiusSqr && distSqr > 0.0001f) // ignorer soi-même et positions trop proches
                {
                    outPositions.Add(data.Position);
                    outVelocities.Add(data.Velocity);
                }
            }
        }

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