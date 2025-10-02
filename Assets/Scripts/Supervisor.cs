using UnityEngine;
using System.Collections.Generic;
using TMPro;

namespace ROS_DRL
{
    [ExecuteAlways]
    public class Supervisor : MonoBehaviour
    {
        private static Supervisor _instance;
        public static Supervisor instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<Supervisor>();
                }
                return _instance;
            }
        }

        public ROSClockPublisher clock => ROSClockPublisher.instance;

        [Header("Learning Settings")]
        public bool inferenceMode = false;

        [Header("View Settings")]

        public GameObject SoloView;
        public GameObject MultipleView;

        [Header("Environment Settings")]
        public int numberOfEnv = 10;
        public GameObject envPrefab;
        private List<GameObject> envInstances = new List<GameObject>();

        private float fixedDeltaTime;
        public float targetTimeScale = 2.0f;
        public TMP_InputField inputEnvNumber;
        public TMP_Dropdown inputLaserSample;
        public TMP_InputField inputHumanNumberMin;
        public TMP_InputField inputHumanNumberMax;
        public int minValue = 10;
        public int maxValue = 300;
        public int selectedMin;
        public int selectedMax;
        public bool useTask = true;

        private bool needTotalReset = false;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this.gameObject);
                return;
            }
            _instance = this;
            fixedDeltaTime = 0.02f;
        }

        private void Start()
        {
            Time.timeScale = targetTimeScale;
            Time.fixedDeltaTime = fixedDeltaTime / Time.timeScale;
            SoloView.SetActive(inferenceMode);
            MultipleView.SetActive(!inferenceMode);


#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            CreateEnvironments();
        }

        private void Update()
        {

#if UNITY_EDITOR
            if (!Application.isPlaying)
                return;
#endif
            // Raccourcis clavier pour test rapide
            if (Input.GetKeyDown(KeyCode.R))
                ResetEnvironments();

            if (Input.GetKeyDown(KeyCode.C))
                CreateEnvironments();

            if (Input.GetKeyDown(KeyCode.D))
                DestroyEnvironments();
        }

        // 🔧 Crée les environnements
        public void CreateEnvironments()
        {
            if (envPrefab == null)
            {
                Debug.LogError("envPrefab is not assigned.");
                return;
            }

            DestroyEnvironments(); // Évite de dupliquer si on en a déjà

            for (int i = 0; i < numberOfEnv; i++)
            {
                GameObject instance = Instantiate(envPrefab, GetEnvPosition(i), Quaternion.identity, this.transform);
                instance.name = $"Env_{i}";
                instance.GetComponent<EnvController>().env_id = i;
                if (numberOfEnv > 1)
                    instance.GetComponent<EnvController>().use_prefix = true;
                envInstances.Add(instance);
            }

            Debug.Log($"✅ {numberOfEnv} environments created.");
        }

        // 🧹 Détruit tous les environnements
        public void DestroyEnvironments()
        {
#if UNITY_EDITOR
            foreach (var env in envInstances)
            {
                if (env != null)
                {
                    if (Application.isPlaying)
                        Destroy(env);
                    else
                        DestroyImmediate(env);
                }
            }
#else
            foreach (var env in envInstances)
            {
                if (env != null) Destroy(env);
            }
#endif
            envInstances.Clear();

            Debug.Log("🗑 All environments destroyed.");
        }

        // 🔄 Reset
        public void ResetEnvironments()
        {
            if (needTotalReset)
            {
                needTotalReset = false;
                DestroyEnvironments();
                CreateEnvironments();
            }
            else
            { 
                foreach (var env in envInstances)
                {
                    if (env == null) continue;
                    var datasetEnv = env.GetComponent<EnvController>();
                    if (datasetEnv != null)
                    {
                        datasetEnv.EditorResetScene();
                    }
                }
            }
        }

        // Positionne les environnements automatiquement
        private Vector3 GetEnvPosition(int index)
        {
            float spacing = 100f; // Distance entre chaque environnement
            return new Vector3(index * spacing, 0, 0);
        }


        public void SetEnvNumber()
        {
            bool hasValue = int.TryParse(inputEnvNumber.text, out int value);

            if (hasValue)
            {
                numberOfEnv = value;
                needTotalReset = true;
            }
            else
            {
                Debug.LogWarning("Valeurs invalides (pas des entiers)");
            }
        }

        public void SetHumanNumberForAll()
        {
            bool minParsed = int.TryParse(inputHumanNumberMin.text, out int parsedMin);
            bool maxParsed = int.TryParse(inputHumanNumberMax.text, out int parsedMax);

            if (minParsed && maxParsed)
            {
                if (parsedMin <= parsedMax)
                {
                    bool reset = false;
                    foreach (var env in envInstances)
                    {
                        if (env == null) continue;
                        var datasetEnv = env.GetComponent<EnvController>();
                        if (datasetEnv != null)
                        {
                            datasetEnv.minNumberOfHumans = parsedMin;
                            datasetEnv.maxNumberOfHumans = parsedMax;
                            reset = true;
                        }
                    }
                    // if(reset)
                    //     ResetEnvironments();
                }
                else
                {
                    Debug.LogWarning("Min doit être ≤ Max");
                }
            }
            else
            {
                Debug.LogWarning("Valeurs invalides (pas des entiers)");
            }
        }

        public void SetFolderPathForAll(string newFolderPath)
        {
            bool reset = false;
            foreach (var env in envInstances)
            {
                if (env == null) continue;

                // Cherche le composant dans l'env instancié
                var datasetEnv = env.GetComponent<EnvCreatorFromDataset>();
                if (datasetEnv != null)
                {
                    datasetEnv.folderPath = newFolderPath;
                    reset = true;
                    Debug.Log($"Set FolderPath for {env.name} to {newFolderPath}");
                }
                else
                {
                    Debug.LogWarning($"EnvCreatorFromDataset not found on {env.name}");
                }
            }
            // if(reset)
            //     ResetEnvironments();
        }

        public void SetLaserSampleForAll()
        {
            string selectedText = inputLaserSample.options[inputLaserSample.value].text;
            bool parsed = int.TryParse(selectedText, out int laserSample);
            bool reset = false;

            foreach (var env in envInstances)
            {
                if (env == null) continue;

                // Cherche le composant dans l'env instancié
                var datasetEnv = env.GetComponent<EnvController>();
                if (datasetEnv != null)
                {
                    datasetEnv.SetLaserSample(laserSample);
                    reset = true;
                }
            }
            // if(reset)
            //     ResetEnvironments();
        }

        public void SetUseTask(bool isOn)
        {
            useTask = !useTask;
            bool reset = false;
            foreach (var env in envInstances)
            {
                if (env == null) continue;
                var datasetEnv = env.GetComponent<EnvController>();
                if (datasetEnv != null)
                {
                    datasetEnv.useTask = useTask;
                    reset = true;
                }
            }
            // if(reset)
            //     ResetEnvironments();
        }

        public void SetInferenceMode(bool isOn)
        {
            inferenceMode = !inferenceMode;
            SoloView.SetActive(inferenceMode);
            MultipleView.SetActive(!inferenceMode);
            if (inferenceMode)
            {
                inputEnvNumber.text = 1.ToString();
                SetEnvNumber();
            }
        }
    }
}
