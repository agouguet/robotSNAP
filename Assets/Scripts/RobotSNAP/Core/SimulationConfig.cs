using UnityEngine;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RobotSNAP.Core
{
    [CreateAssetMenu(fileName = "SimulationConfig", menuName = "RobotSNAP/Simulation Config")]
    public class SimulationConfig : ScriptableObject
    {
        [Header("Environment")]
        public int environmentCount = 1;
        public float environmentSpacing = 100f;
        public string datasetPath = "";
        
        [Header("Spawn")]
        public int minHumans = 0;
        public int maxHumans = 5;
        public float minAgentDistance = 2f;
        public float minPathLength = 3f;
        public float maxPathLength = 0f;
        
        [Header("Robot")]
        public float robotMaxLinearSpeed = 2f;
        public float robotMaxAngularSpeed = 2f;
        public int robotLaserSamples = 180;
        public int robotControlMode = 0;
        
        [Header("Human")]
        public float humanDefaultSpeed = 0.8f;
        public float humanMaxSpeed = 1f;
        public int humanControllerType = 0;
        public float humanInteractionRadius = 5f;
        
        [Header("Simulation")]
        public float timeScale = 2f;
        public bool inferenceMode = false;
        
        [Header("ROS")]
        public string rosPrefix = "";
        public float rosPublishFrequency = 10f;
        public bool rosEnabled = true;
        
        /// <summary>
        /// Crée une copie de la configuration
        /// </summary>
        public SimulationConfig Clone()
        {
            SimulationConfig copy = CreateInstance<SimulationConfig>();
            CopyTo(copy);
            return copy;
        }
        
        /// <summary>
        /// Copie les valeurs vers une autre config
        /// </summary>
        public void CopyTo(SimulationConfig target)
        {
            if (target == null) return;
            
            target.environmentCount = environmentCount;
            target.environmentSpacing = environmentSpacing;
            target.datasetPath = datasetPath;
            target.minHumans = minHumans;
            target.maxHumans = maxHumans;
            target.minAgentDistance = minAgentDistance;
            target.minPathLength = minPathLength;
            target.maxPathLength = maxPathLength;
            target.robotMaxLinearSpeed = robotMaxLinearSpeed;
            target.robotMaxAngularSpeed = robotMaxAngularSpeed;
            target.robotLaserSamples = robotLaserSamples;
            target.robotControlMode = robotControlMode;
            target.humanDefaultSpeed = humanDefaultSpeed;
            target.humanMaxSpeed = humanMaxSpeed;
            target.humanControllerType = humanControllerType;
            target.humanInteractionRadius = humanInteractionRadius;
            target.timeScale = timeScale;
            target.inferenceMode = inferenceMode;
            target.rosPrefix = rosPrefix;
            target.rosPublishFrequency = rosPublishFrequency;
            target.rosEnabled = rosEnabled;
        }
        
        /// <summary>
        /// Applique les paramètres de temps
        /// </summary>
        public void ApplyTimeSettings()
        {
            Time.timeScale = timeScale;
        }
        
        /// <summary>
        /// Sauvegarde la configuration dans un fichier JSON
        /// </summary>
        public void SaveToJson(string filePath)
        {
            string json = JsonUtility.ToJson(this, true);
            File.WriteAllText(filePath, json);
            Debug.Log($"[SimulationConfig] Saved to JSON: {filePath}");
        }
        
        /// <summary>
        /// Charge la configuration depuis un fichier JSON
        /// </summary>
        public static SimulationConfig LoadFromJson(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"[SimulationConfig] File not found: {filePath}");
                return null;
            }
            
            string json = File.ReadAllText(filePath);
            SimulationConfig config = CreateInstance<SimulationConfig>();
            JsonUtility.FromJsonOverwrite(json, config);
            
            Debug.Log($"[SimulationConfig] Loaded from JSON: {filePath}");
            return config;
        }
        
        /// <summary>
        /// Sauvegarde la configuration dans un fichier .asset (Editor only)
        /// </summary>
        public void SaveToAsset(string assetPath)
        {
#if UNITY_EDITOR
            // Créer une copie pour l'asset
            SimulationConfig asset = CreateInstance<SimulationConfig>();
            this.CopyTo(asset);
            
            // Créer le dossier si nécessaire
            string folder = Path.GetDirectoryName(assetPath);
            if (!AssetDatabase.IsValidFolder(folder))
            {
                string[] folders = folder.Split('/');
                string currentPath = "";
                foreach (string f in folders)
                {
                    string newPath = string.IsNullOrEmpty(currentPath) ? f : $"{currentPath}/{f}";
                    if (!AssetDatabase.IsValidFolder(newPath))
                    {
                        AssetDatabase.CreateFolder(string.IsNullOrEmpty(currentPath) ? "Assets" : currentPath, f);
                    }
                    currentPath = newPath;
                }
            }
            
            // Sauvegarder l'asset
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            Debug.Log($"[SimulationConfig] Saved to asset: {assetPath}");
#else
            Debug.LogWarning("[SimulationConfig] SaveToAsset only works in Editor");
#endif
        }
    }
}