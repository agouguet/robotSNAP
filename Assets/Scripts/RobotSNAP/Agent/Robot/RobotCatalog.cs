using System.Collections.Generic;
using UnityEngine;

namespace RobotSNAP.Agents
{
    /// <summary>
    /// La table « id de type de robot -> prefab du projet », écrite par l'outil d'éditeur
    /// <c>RobotSNAP/Robots/Rebuild the robot catalog</c> et lue à l'exécution par RobotRoster.
    ///
    /// Elle existe pour que le composant RobotRoster, qui est créé à l'exécution et n'a donc rien de câblé à la
    /// main dans une scène, puisse tout de même retrouver les vrais prefabs du projet : un scénario dit
    /// « jackal » et obtient le Jackal livré dans Assets/Prefabs/Robots, avec ses roues, ses capteurs et ses
    /// scripts. Les profils de RobotProfile décrivent ce qu'un type sait faire (rayon, vitesse, lidar) ; ce
    /// catalogue dit quel prefab incarne ce type.
    /// </summary>
    [CreateAssetMenu(fileName = "RobotCatalog", menuName = "RobotSNAP/Robot Catalog")]
    public sealed class RobotCatalog : ScriptableObject
    {
        /// <summary>Une ligne du catalogue : un id de type, et le prefab qui le représente.</summary>
        [System.Serializable]
        public sealed class Entry
        {
            public string TypeId;
            public GameObject Prefab;
        }

        /// <summary>
        /// Nom sous Resources/ : l'asset est donc chargé sans référence de scène, ce qui permet à un
        /// RobotRoster créé à l'exécution de le trouver sans qu'aucun objet ne le lui passe.
        /// </summary>
        public const string ResourcePath = "RobotCatalog";

        [SerializeField] private List<Entry> _entries = new List<Entry>();

        /// <summary>Lignes du catalogue, dans l'ordre écrit par l'outil d'éditeur.</summary>
        public IReadOnlyList<Entry> Entries => _entries;

        /// <summary>Charge le catalogue livré avec le projet. Peut retourner null si l'asset n'existe pas.</summary>
        public static RobotCatalog Load()
        {
            return Resources.Load<RobotCatalog>(ResourcePath);
        }

        /// <summary>
        /// Prefab du type demandé, ou null si le type est inconnu. La comparaison ignore la casse et les
        /// espaces, parce qu'un id vient d'un fichier YAML écrit à la main : « Jackal », « jackal » et
        /// « jackal » avec une espace en trop désignent le même robot.
        /// </summary>
        public GameObject PrefabFor(string typeId)
        {
            if (string.IsNullOrEmpty(typeId)) return null;

            string wanted = Normalize(typeId);
            if (wanted.Length == 0) return null;

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry entry = _entries[i];
                if (entry == null || entry.Prefab == null || string.IsNullOrEmpty(entry.TypeId)) continue;

                if (Normalize(entry.TypeId) == wanted) return entry.Prefab;
            }

            return null;
        }

        /// <summary>
        /// Remplace tout le contenu du catalogue. Utilisé par l'outil d'éditeur, qui le régénère à partir des
        /// prefabs présents dans le projet plutôt que de laisser quelqu'un tenir la liste à la main.
        /// </summary>
        public void SetEntries(IEnumerable<Entry> entries)
        {
            _entries.Clear();
            if (entries == null) return;

            foreach (Entry entry in entries)
            {
                if (entry != null) _entries.Add(entry);
            }
        }

        /// <summary>Retire les espaces et met en minuscules, pour comparer des ids écrits librement.</summary>
        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var builder = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsWhiteSpace(c)) continue;
                builder.Append(char.ToLowerInvariant(c));
            }

            return builder.ToString();
        }
    }
}
