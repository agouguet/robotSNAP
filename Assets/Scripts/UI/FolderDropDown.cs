using UnityEngine;
using UnityEngine.UI;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using TMPro;

namespace ROS_DRL
{
    public class FolderDropdown : MonoBehaviour
    {
        public TMP_Dropdown dropdown;
        public string folderPathRelative = "Dataset"; // Ex: "dataset" ou "scenario"

        private List<string> matchingFolders = new List<string>();

        void Start()
        {
            dropdown.ClearOptions();

            string rootPath = Path.Combine(Application.streamingAssetsPath, folderPathRelative.Replace("Assets/", ""));

            if (Directory.Exists(rootPath))
            {
                matchingFolders = FindMatchingFolders(rootPath);

                // Récupère le chemin relatif depuis folderPathRelative
                List<string> displayNames = matchingFolders
                    .Select(fullPath => Utils.GetRelativePath(fullPath, rootPath))
                    .Select(path => path.Replace("\\", "/")) // Pour uniformiser
                    .ToList();

                dropdown.AddOptions(displayNames);
                dropdown.onValueChanged.AddListener(OnFolderSelected);
            }
            else
            {
                Debug.LogWarning("Dossier non trouvé : " + rootPath);
            }
        }

        private void OnFolderSelected(int index)
        {
            if (index >= 0 && index < matchingFolders.Count)
            {
                string selectedFullPath = matchingFolders[index];
                string relativePath = Utils.GetRelativePath(selectedFullPath, Application.streamingAssetsPath);

                if (Supervisor.instance != null)
                {
                    Supervisor.instance.SetFolderPathForAll(relativePath);
                    Debug.Log($"📂 Folder selected: {relativePath}");
                }
                else
                {
                    Debug.LogWarning("Supervisor.instance is null");
                }
            }
        }

        // 🔍 Recherche récursive de tous les dossiers contenant /png et /json
        private List<string> FindMatchingFolders(string root)
        {
            List<string> results = new List<string>();

            foreach (string dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
            {
                string pngPath = Path.Combine(dir, "png");
                string jsonPath = Path.Combine(dir, "json");

                if (Directory.Exists(pngPath) && Directory.Exists(jsonPath))
                {
                    results.Add(dir);
                }
            }

            return results;
        }
    }
}
