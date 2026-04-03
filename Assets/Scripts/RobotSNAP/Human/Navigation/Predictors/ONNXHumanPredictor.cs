using UnityEngine;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;

namespace RobotSNAP.Movement.Predictors
{
    /// <summary>
    /// Prédicteur utilisant le modèle ONNX avec Sentis (Unity Inference Engine)
    /// </summary>
    public class ONNXHumanPredictor : IDisposable
    {
        // Paramètres du modèle (doivent correspondre à l'entraînement)
        private const int OBS_LEN = 8;
        private const int PRED_LEN = 4;
        private const int K_NEIGHBORS = 5;
        private const int LATENT_DIM = 16;
        
        // Dimensions des features
        private const int TRAJ_FEATURES = (OBS_LEN - 1) * 4;  // 28
        private const int NEIGH_FEATURES = K_NEIGHBORS * 3;    // 15
        private const int GOAL_FEATURES = 6;                    // 6
        public const int INPUT_SIZE = TRAJ_FEATURES + NEIGH_FEATURES + GOAL_FEATURES; // 49
        
        // Modèle Sentis
        private Unity.InferenceEngine.Model runtimeModel;
        private Unity.InferenceEngine.Worker worker;
        private bool isInitialized = false;
        
        // Configuration
        private Unity.InferenceEngine.BackendType backendType = Unity.InferenceEngine.BackendType.GPUCompute;
        private bool useFallbackOnError = true;
        
        // Callbacks
        public event Action<PredictionResult> OnPredictionComplete;
        
        /// <summary>
        /// Constructeur avec chemin du modèle (fichier .onnx)
        /// </summary>
        public ONNXHumanPredictor(string modelPath, bool useGPU = true)
        {
            backendType = useGPU && SystemInfo.supportsComputeShaders 
                ? Unity.InferenceEngine.BackendType.GPUCompute 
                : Unity.InferenceEngine.BackendType.CPU;
            
            LoadModelFromPath(modelPath);
        }
        
        /// <summary>
        /// Charge le modèle directement depuis le chemin du fichier
        /// </summary>
        private void LoadModelFromPath(string modelPath)
        {
            try
            {
                // Méthode 1: Essayer de charger depuis Resources d'abord
                Unity.InferenceEngine.ModelAsset modelAsset = Resources.Load<Unity.InferenceEngine.ModelAsset>(modelPath);
                
                if (modelAsset != null)
                {
                    Debug.Log("Model loaded from Resources");
                    runtimeModel = Unity.InferenceEngine.ModelLoader.Load(modelAsset);
                }
                else
                {
                    // Méthode 2: Chercher le fichier sur le disque
                    string fullPath = GetFullPath(modelPath);
                    
                    if (File.Exists(fullPath))
                    {
                        Debug.Log($"Loading model from file: {fullPath}");
                        
                        // ✅ CORRECTION: Lire le fichier et utiliser ModelLoader.Load avec ModelAsset
                        // Il faut d'abord importer le fichier comme ModelAsset via Unity
                        
                        // Option A: Utiliser AssetDatabase dans l'éditeur
                        #if UNITY_EDITOR
                        string assetPath = GetAssetPath(fullPath);
                        if (!string.IsNullOrEmpty(assetPath))
                        {
                            modelAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>(assetPath);
                            if (modelAsset != null)
                            {
                                runtimeModel = Unity.InferenceEngine.ModelLoader.Load(modelAsset);
                            }
                        }
                        #endif
                        
                        // Option B: Si le modèle n'est pas importé, on ne peut pas le charger directement
                        // La solution est de déplacer le fichier dans Resources
                        if (runtimeModel == null)
                        {
                            Debug.LogError($"Model file exists but cannot be loaded directly. Please place the .onnx file in a 'Resources' folder and ensure it's properly imported in Unity.\nFile: {fullPath}");
                            isInitialized = false;
                            return;
                        }
                    }
                    else
                    {
                        // Méthode 3: Chercher dans tous les Resources
                        var allModels = Resources.LoadAll<Unity.InferenceEngine.ModelAsset>("");
                        Debug.Log($"Found {allModels.Length} models in Resources");
                        
                        foreach (var model in allModels)
                        {
                            Debug.Log($"  Model: {model.name}");
                            if (model.name.Contains("policy") || model.name.Contains("lstm"))
                            {
                                runtimeModel = Unity.InferenceEngine.ModelLoader.Load(model);
                                break;
                            }
                        }
                    }
                }
                
                if (runtimeModel == null)
                {
                    Debug.LogError($"Failed to load model from any source: {modelPath}");
                    isInitialized = false;
                    return;
                }
                
                // Créer le worker
                worker = new Unity.InferenceEngine.Worker(runtimeModel, backendType);
                isInitialized = true;
                
                Debug.Log($"ONNX model loaded successfully with Sentis");
                Debug.Log($"  Backend: {(backendType == Unity.InferenceEngine.BackendType.GPUCompute ? "GPU Compute" : "CPU")}");
                Debug.Log($"  Model inputs: {string.Join(", ", runtimeModel.inputs)}");
                Debug.Log($"  Model outputs: {string.Join(", ", runtimeModel.outputs)}");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load ONNX model: {e.Message}\n{e.StackTrace}");
                isInitialized = false;
            }
        }

        #if UNITY_EDITOR
        /// <summary>
        /// Obtient le chemin asset à partir du chemin système
        /// </summary>
        private string GetAssetPath(string fullPath)
        {
            string dataPath = Application.dataPath;
            if (fullPath.StartsWith(dataPath))
            {
                return "Assets" + fullPath.Substring(dataPath.Length);
            }
            return null;
        }
        #endif

        /// <summary>
        /// Obtient le chemin complet du modèle
        /// </summary>
        private string GetFullPath(string modelPath)
        {
            // Essayer différents chemins possibles
            string[] possiblePaths = {
                modelPath,
                Path.Combine(Application.dataPath, modelPath),
                Path.Combine(Application.dataPath, "Resources", modelPath),
                Path.Combine(Application.dataPath, "Resources/Models", modelPath),
                Path.Combine(Application.dataPath, "Resources", modelPath + ".onnx"),
                Path.Combine(Application.dataPath, "Resources/Models", modelPath + ".onnx")
            };
            
            foreach (string path in possiblePaths)
            {
                if (File.Exists(path))
                    return path;
                    
                // Essayer avec extension .onnx
                string withExt = path.EndsWith(".onnx") ? path : path + ".onnx";
                if (File.Exists(withExt))
                    return withExt;
            }
            
            return modelPath;
        }
        
        /// <summary>
        /// Constructeur avec ModelAsset direct
        /// </summary>
        public ONNXHumanPredictor(Unity.InferenceEngine.ModelAsset asset, bool useGPU = true)
        {
            backendType = useGPU && SystemInfo.supportsComputeShaders 
                ? Unity.InferenceEngine.BackendType.GPUCompute 
                : Unity.InferenceEngine.BackendType.CPU;
            
            try
            {
                runtimeModel = Unity.InferenceEngine.ModelLoader.Load(asset);
                worker = new Unity.InferenceEngine.Worker(runtimeModel, backendType);
                isInitialized = true;
                
                Debug.Log($"ONNX model loaded successfully from ModelAsset");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load model from ModelAsset: {e.Message}");
                isInitialized = false;
            }
        }
        
        /// <summary>
        /// Prédit une trajectoire (synchrone)
        /// </summary>
        public PredictionResult PredictTrajectory(
            Vector2[] trajectory,
            Vector2[] neighbors,
            Vector2 goal,
            int numSamples = 5)
        {
            if (!isInitialized)
            {
                Debug.LogWarning("ONNX predictor not initialized, using fallback");
                return GetFallbackPrediction(trajectory, goal, numSamples);
            }
            
            try
            {
                // Construire le tensor d'entrée
                float[] inputData = BuildInputFeatures(trajectory, neighbors, goal);
                Unity.InferenceEngine.TensorShape inputShape = new Unity.InferenceEngine.TensorShape(1, INPUT_SIZE);
                Unity.InferenceEngine.Tensor<float> inputTensor = new Unity.InferenceEngine.Tensor<float>(inputShape, inputData);
                
                List<Vector2[]> allPredictions = new List<Vector2[]>();
                List<float> confidences = new List<float>();
                
                // Générer plusieurs échantillons avec différentes variables latentes
                for (int i = 0; i < numSamples; i++)
                {
                    // Variable latente aléatoire
                    float[] latentData = new float[LATENT_DIM];
                    for (int j = 0; j < LATENT_DIM; j++)
                    {
                        latentData[j] = UnityEngine.Random.Range(-1f, 1f);
                    }
                    Unity.InferenceEngine.TensorShape latentShape = new Unity.InferenceEngine.TensorShape(1, LATENT_DIM);
                    Unity.InferenceEngine.Tensor<float> latentTensor = new Unity.InferenceEngine.Tensor<float>(latentShape, latentData);
                    
                    // ✅ CORRECTION: Utiliser Schedule au lieu de Execute
                    worker.Schedule(inputTensor, latentTensor);
                    
                    // Récupérer le tensor de sortie
                    Unity.InferenceEngine.Tensor<float> outputTensor = worker.PeekOutput() as Unity.InferenceEngine.Tensor<float>;
                    
                    if (outputTensor != null)
                    {
                        // Copier les données dans un array
                        float[] outputArray = outputTensor.DownloadToArray();
                        
                        // Convertir en Vector2[]
                        Vector2[] prediction = ConvertToVector2Array(outputArray);
                        allPredictions.Add(prediction);
                        
                        // Calculer la confiance
                        float confidence = CalculateConfidence(prediction, allPredictions);
                        confidences.Add(confidence);
                    }
                    
                    latentTensor.Dispose();
                }
                
                inputTensor.Dispose();
                
                var result = new PredictionResult
                {
                    predictions = allPredictions.ToArray(),
                    confidenceScores = confidences.ToArray(),
                    timestamp = DateTime.Now
                };
                
                OnPredictionComplete?.Invoke(result);
                return result;
            }
            catch (Exception e)
            {
                Debug.LogError($"ONNX prediction failed: {e.Message}\n{e.StackTrace}");
                if (useFallbackOnError)
                {
                    return GetFallbackPrediction(trajectory, goal, numSamples);
                }
                return null;
            }
        }
        
        /// <summary>
        /// Retourne une prédiction vide
        /// </summary>
        private Vector2[] GetEmptyPrediction()
        {
            Vector2[] empty = new Vector2[PRED_LEN];
            for (int i = 0; i < PRED_LEN; i++)
                empty[i] = Vector2.zero;
            return empty;
        }
        
        /// <summary>
        /// Prédiction asynchrone
        /// </summary>
        public async Task<PredictionResult> PredictTrajectoryAsync(
            Vector2[] trajectory,
            Vector2[] neighbors,
            Vector2 goal,
            int numSamples = 5)
        {
            return await Task.Run(() => PredictTrajectory(trajectory, neighbors, goal, numSamples));
        }
        
        /// <summary>
        /// Construit les features d'entrée pour le modèle
        /// </summary>
        private float[] BuildInputFeatures(Vector2[] trajectory, Vector2[] neighbors, Vector2 goal)
        {
            float[] features = new float[INPUT_SIZE];
            int idx = 0;
            
            // 1. Features de trajectoire (positions relatives + vitesses)
            if (trajectory != null && trajectory.Length >= 2)
            {
                int startIdx = Mathf.Max(0, trajectory.Length - OBS_LEN);
                
                for (int i = startIdx; i < trajectory.Length - 1; i++)
                {
                    if (idx + 4 <= TRAJ_FEATURES)
                    {
                        Vector2 current = trajectory[i];
                        Vector2 next = trajectory[i + 1];
                        
                        features[idx++] = current.x;
                        features[idx++] = current.y;
                        features[idx++] = (next.x - current.x);
                        features[idx++] = (next.y - current.y);
                    }
                }
            }
            
            // Compléter avec des zéros si nécessaire
            while (idx < TRAJ_FEATURES)
            {
                features[idx++] = 0f;
            }
            
            // 2. Features des voisins (positions relatives)
            if (neighbors != null && trajectory != null && trajectory.Length > 0)
            {
                Vector2 currentPos = trajectory[trajectory.Length - 1];
                
                for (int i = 0; i < K_NEIGHBORS; i++)
                {
                    if (i < neighbors.Length)
                    {
                        Vector2 relPos = neighbors[i] - currentPos;
                        features[idx++] = relPos.x;
                        features[idx++] = relPos.y;
                        features[idx++] = Vector2.Distance(currentPos, neighbors[i]);
                    }
                    else
                    {
                        features[idx++] = 0f;
                        features[idx++] = 0f;
                        features[idx++] = 0f;
                    }
                }
            }
            else
            {
                idx += NEIGH_FEATURES;
            }
            
            // 3. Features du goal
            if (trajectory != null && trajectory.Length > 0)
            {
                Vector2 lastPos = trajectory[trajectory.Length - 1];
                Vector2 goalRel = goal - lastPos;
                features[idx++] = goalRel.x;
                features[idx++] = goalRel.y;
            }
            else
            {
                idx += 2;
            }
            
            features[idx++] = 0f; // vx goal
            features[idx++] = 0f; // vy goal
            features[idx++] = 0f; // scene encoding 1
            features[idx++] = 0f; // scene encoding 2
            
            return features;
        }
        
        /// <summary>
        /// Convertit un tableau de float en tableau de Vector2
        /// </summary>
        private Vector2[] ConvertToVector2Array(float[] data)
        {
            Vector2[] result = new Vector2[PRED_LEN];
            
            // Le tensor est de shape [batch, PRED_LEN, 2]
            for (int i = 0; i < PRED_LEN && i * 2 + 1 < data.Length; i++)
            {
                float x = data[i * 2];
                float y = data[i * 2 + 1];
                result[i] = new Vector2(x, y);
            }
            
            return result;
        }
        
        /// <summary>
        /// Calcule un score de confiance pour la prédiction
        /// </summary>
        private float CalculateConfidence(Vector2[] prediction, List<Vector2[]> allPredictions)
        {
            if (allPredictions.Count < 2)
                return 0.5f;
            
            Vector2[] lastPrediction = allPredictions[allPredictions.Count - 2];
            float maxDistance = 0f;
            
            for (int i = 0; i < PRED_LEN && i < prediction.Length && i < lastPrediction.Length; i++)
            {
                float dist = Vector2.Distance(prediction[i], lastPrediction[i]);
                if (dist > maxDistance) maxDistance = dist;
            }
            
            float confidence = Mathf.Clamp01(1f - (maxDistance / 5f));
            return confidence;
        }
        
        /// <summary>
        /// Prédiction de fallback
        /// </summary>
        private PredictionResult GetFallbackPrediction(Vector2[] trajectory, Vector2 goal, int numSamples)
        {
            Vector2[][] predictions = new Vector2[numSamples][];
            float[] confidences = new float[numSamples];
            
            Vector2 lastPos = trajectory != null && trajectory.Length > 0 
                ? trajectory[trajectory.Length - 1] 
                : Vector2.zero;
            Vector2 direction = (goal - lastPos).normalized;
            
            for (int i = 0; i < numSamples; i++)
            {
                predictions[i] = new Vector2[PRED_LEN];
                
                for (int j = 0; j < PRED_LEN; j++)
                {
                    float noise = i * 0.2f * (UnityEngine.Random.value - 0.5f);
                    Vector2 noisyDir = (direction + new Vector2(noise, noise)).normalized;
                    predictions[i][j] = lastPos + noisyDir * (j + 1) * 0.5f;
                }
                
                confidences[i] = 0.3f + (i * 0.1f);
            }
            
            return new PredictionResult
            {
                predictions = predictions,
                confidenceScores = confidences,
                timestamp = DateTime.Now,
                isFallback = true
            };
        }
        
        /// <summary>
        /// Warm-up du modèle pour éviter les stutters
        /// </summary>
        public void Warmup()
        {
            if (!isInitialized) return;
            
            Debug.Log("Warming up ONNX model...");
            
            try
            {
                // Créer des entrées factices
                float[] dummyInputData = new float[INPUT_SIZE];
                Unity.InferenceEngine.Tensor<float> dummyInput = new Unity.InferenceEngine.Tensor<float>(new Unity.InferenceEngine.TensorShape(1, INPUT_SIZE), dummyInputData);
                
                float[] dummyLatentData = new float[LATENT_DIM];
                Unity.InferenceEngine.Tensor<float> dummyLatent = new Unity.InferenceEngine.Tensor<float>(new Unity.InferenceEngine.TensorShape(1, LATENT_DIM), dummyLatentData);
                
                // ✅ CORRECTION: Utiliser Schedule au lieu de Execute
                worker.Schedule(dummyInput, dummyLatent);
                
                dummyInput.Dispose();
                dummyLatent.Dispose();
                
                Debug.Log("Model warmed up and ready");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Warmup failed: {e.Message}");
            }
        }
        
        /// <summary>
        /// Libère les ressources
        /// </summary>
        public void Dispose()
        {
            worker?.Dispose();
        }
        
        /// <summary>
        /// Vérifie si le modèle est chargé et prêt
        /// </summary>
        public bool IsReady => isInitialized && worker != null;
        
        /// <summary>
        /// Résultat de prédiction
        /// </summary>
        public class PredictionResult
        {
            public Vector2[][] predictions;
            public float[] confidenceScores;
            public DateTime timestamp;
            public bool isFallback = false;
            
            public Vector2 GetMeanPrediction()
            {
                if (predictions == null || predictions.Length == 0 || predictions[0].Length == 0)
                    return Vector2.zero;
                
                Vector2 sum = Vector2.zero;
                int count = 0;
                
                foreach (var pred in predictions)
                {
                    if (pred != null && pred.Length > 0)
                    {
                        sum += pred[0];
                        count++;
                    }
                }
                
                return count > 0 ? sum / count : Vector2.zero;
            }
            
            public float GetMeanConfidence()
            {
                if (confidenceScores == null || confidenceScores.Length == 0)
                    return 0.5f;
                
                float sum = 0f;
                foreach (float conf in confidenceScores)
                {
                    sum += conf;
                }
                return sum / confidenceScores.Length;
            }
        }
    }
}