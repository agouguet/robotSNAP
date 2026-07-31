using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RobotSNAP.Core
{
    [ExecuteAlways]
    public class Clock : MonoBehaviour
    {
        private static Clock _instance;
        public static Clock Instance => _instance;

        [Header("Settings")]
        [SerializeField] private bool useRealTime = true;
        [SerializeField] private float timeScale = 1f;
        [SerializeField] private bool autoStart = true;

        [Header("Simulation Start (ignoré si useRealTime)")]
        [SerializeField] [Range(0, 24)] private float simulationStartHour = 8f;

        [Header("Debug")]
        [SerializeField] private bool logTimeChanges = false;

        [Header("Editor Debug (Read-Only)")]
        [SerializeField] private string editorTimeDisplay = "00:00:00";

        private double _simulatedTimeSeconds = 0f;
        private double _pauseTimeSeconds = 0f;
        private bool _isPaused = false;
        private double _startTimeMillis;
        private double _lastTimeMillis;
        private float _editorDisplayUpdateTimer = 0f;
        private bool _isInitialized = false;
        private bool _previousUseRealTime;

        public event Action<double> OnTimeUpdated;
        public event Action<bool> OnPauseStateChanged;

        public double CurrentTimeMillis => GetCurrentTimeMillis();
        public double CurrentTimeSeconds => CurrentTimeMillis / 1000.0;
        public bool IsPaused => _isPaused;
        public float TimeScale => timeScale;
        public bool UseRealTime => useRealTime;

        public static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        #region Unity Lifecycle

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        private void OnEnable()
        {
            if (!_isInitialized)
            {
                Initialize();
                _isInitialized = true;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                enabled = true;
                UpdateEditorDisplay();
            }
#endif
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (useRealTime != _previousUseRealTime)
            {
                _previousUseRealTime = useRealTime;
                if (Application.isPlaying)
                {
                    Initialize();
                }
                else
                {
                    UpdateEditorDisplay();
                }
                return;
            }

            if (!Application.isPlaying)
            {
                UpdateEditorDisplay();
            }
            else
            {
                if (!useRealTime && _isInitialized)
                {
                    Initialize();
                }
            }
        }
#endif

        private void Update()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UpdateEditorDisplay();
                return;
            }
#endif

            if (!autoStart || !_isInitialized) return;

            if (!useRealTime && !_isPaused)
            {
                _simulatedTimeSeconds += Time.deltaTime * timeScale;
            }

            double currentTime = GetCurrentTimeMillis();
            if (Math.Abs(currentTime - _lastTimeMillis) > 0.001)
            {
                _lastTimeMillis = currentTime;
                OnTimeUpdated?.Invoke(currentTime);

                if (logTimeChanges && Time.frameCount % 60 == 0)
                {
                    Debug.Log($"[Clock] Time: {CurrentTimeSeconds:F3}s ({(CurrentTimeSeconds % 86400) / 3600:F2}h)");
                }
            }

            // Mise à jour de l'affichage dans l'inspecteur (toutes les 0.5s)
            _editorDisplayUpdateTimer += Time.deltaTime;
            if (_editorDisplayUpdateTimer >= 0.5f)
            {
                _editorDisplayUpdateTimer = 0f;
                if (useRealTime)
                    UpdateEditorDisplay();       // affiche l'heure UTC
                else
                    UpdateEditorDisplayFromCurrentTime(); // affiche l'heure simulée
            }
        }

        #endregion

        #region Editor Debug

        /// <summary>
        /// Met à jour l'affichage pour le mode Édition ou pour le mode RealTime en jeu.
        /// Affiche l'heure UTC actuelle.
        /// </summary>
        private void UpdateEditorDisplay()
        {
            if (useRealTime)
            {
                DateTime now = DateTime.UtcNow;
                editorTimeDisplay = now.ToString("HH:mm:ss");
            }
            else
            {
                double totalSeconds = simulationStartHour * 3600.0;
                int hours = Mathf.FloorToInt((float)(totalSeconds / 3600.0));
                int minutes = Mathf.FloorToInt((float)((totalSeconds % 3600.0) / 60.0));
                int seconds = Mathf.FloorToInt((float)(totalSeconds % 60.0));
                editorTimeDisplay = $"{hours:D2}:{minutes:D2}:{seconds:D2}";
            }
        }

        /// <summary>
        /// Met à jour l'affichage à partir du temps simulé (pour mode simulation).
        /// </summary>
        private void UpdateEditorDisplayFromCurrentTime()
        {
            double secondsInDay = CurrentTimeSeconds % 86400.0;
            if (secondsInDay < 0) secondsInDay += 86400.0;
            int hours = Mathf.FloorToInt((float)(secondsInDay / 3600.0));
            int minutes = Mathf.FloorToInt((float)((secondsInDay % 3600.0) / 60.0));
            int seconds = Mathf.FloorToInt((float)(secondsInDay % 60.0));
            editorTimeDisplay = $"{hours:D2}:{minutes:D2}:{seconds:D2}";
        }

        #endregion

        #region Initialization

        public void Initialize()
        {
            if (useRealTime)
            {
                _startTimeMillis = (DateTime.UtcNow - UnixEpoch).TotalMilliseconds;
                _lastTimeMillis = _startTimeMillis;
                _simulatedTimeSeconds = 0;
                _pauseTimeSeconds = 0;
                _isPaused = false;
            }
            else
            {
                double startOffsetSeconds = simulationStartHour * 3600.0;
                _simulatedTimeSeconds = startOffsetSeconds;
                _pauseTimeSeconds = 0;
                _isPaused = false;
                _lastTimeMillis = _simulatedTimeSeconds * 1000;
            }

            _isInitialized = true;
            _previousUseRealTime = useRealTime;

            // Mise à jour initiale de l'affichage
            if (useRealTime)
                UpdateEditorDisplay();
            else
                UpdateEditorDisplayFromCurrentTime();

            if (logTimeChanges)
            {
                Debug.Log($"[Clock] Initialized. Mode: {(useRealTime ? "RealTime" : "Simulation")}, StartHour: {simulationStartHour}h, Simulated seconds: {_simulatedTimeSeconds}");
            }
        }

        public void ResetTime()
        {
            Initialize();
        }

        #endregion

        #region Time Management

        private double GetCurrentTimeMillis()
        {
            if (_isPaused)
                return _pauseTimeSeconds * 1000;

            if (useRealTime)
            {
                double realTime = (DateTime.UtcNow - UnixEpoch).TotalMilliseconds;
                return realTime - _startTimeMillis;
            }
            else
            {
                return _simulatedTimeSeconds * 1000;
            }
        }

        public void Pause()
        {
            if (_isPaused) return;
            _pauseTimeSeconds = GetCurrentTimeMillis() / 1000.0;
            _isPaused = true;
            OnPauseStateChanged?.Invoke(true);
            if (logTimeChanges)
                Debug.Log($"[Clock] Paused at {_pauseTimeSeconds:F3}s");
        }

        public void Resume()
        {
            if (!_isPaused) return;
            if (useRealTime)
            {
                double currentReal = (DateTime.UtcNow - UnixEpoch).TotalMilliseconds;
                double frozenValue = _pauseTimeSeconds * 1000;
                _startTimeMillis = currentReal - frozenValue;
            }
            else
            {
                _simulatedTimeSeconds = _pauseTimeSeconds;
            }
            _isPaused = false;
            _lastTimeMillis = GetCurrentTimeMillis();
            OnPauseStateChanged?.Invoke(false);
            if (logTimeChanges)
                Debug.Log($"[Clock] Resumed at {CurrentTimeSeconds:F3}s");
        }

        public void TogglePause()
        {
            if (_isPaused) Resume(); else Pause();
        }

        public void SetTimeScale(float scale)
        {
            timeScale = Mathf.Max(0f, scale);
            if (logTimeChanges) Debug.Log($"[Clock] Time scale set to {timeScale}");
        }

        #endregion

        #region Editor Utilities

        [ContextMenu("Log Current Time")]
        private void EditorLogTime()
        {
            Debug.Log($"[Clock] Current time: {CurrentTimeSeconds:F6}s ({(CurrentTimeSeconds % 86400) / 3600:F2}h)");
        }

        [ContextMenu("Toggle Pause")]
        private void EditorTogglePause()
        {
            TogglePause();
        }

        [ContextMenu("Reset Time")]
        private void EditorResetTime()
        {
            ResetTime();
        }

        #endregion
    }
}