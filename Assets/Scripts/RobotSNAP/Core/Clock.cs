using System;
using UnityEngine;

namespace RobotSNAP.Core
{
    /// <summary>
    /// Pure time manager - No ROS dependencies.
    /// Provides simulation time and timestamp generation.
    /// Singleton accessible from anywhere.
    /// </summary>
    public class Clock : MonoBehaviour
    {
        private static Clock _instance;
        public static Clock Instance => _instance;
        
        [Header("Settings")]
        [SerializeField] private bool useRealTime = true;
        [SerializeField] private float timeScale = 1f;
        [SerializeField] private bool autoStart = true;
        
        [Header("Debug")]
        [SerializeField] private bool logTimeChanges = false;
        
        // Time state
        private double _startTimeMillis;
        private double _lastTimeMillis;
        private double _pauseTimeOffset;
        private bool _isPaused;
        
        // Events
        public event Action<double> OnTimeUpdated;
        public event Action<bool> OnPauseStateChanged;
        
        // Properties
        public double CurrentTimeMillis => GetCurrentTimeMillis();
        public double CurrentTimeSeconds => CurrentTimeMillis / 1000.0;
        public bool IsPaused => _isPaused;
        public float TimeScale => timeScale;
        
        // Unix epoch reference
        public static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            // if (_instance != null && _instance != this)
            // {
            //     Destroy(gameObject);
            //     return;
            // }
            
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            Initialize();
        }
        
        private void Update()
        {
            if (!autoStart || _isPaused) return;
            
            double currentTime = GetCurrentTimeMillis();
            if (Math.Abs(currentTime - _lastTimeMillis) > 0.001)
            {
                _lastTimeMillis = currentTime;
                OnTimeUpdated?.Invoke(currentTime);
                
                if (logTimeChanges && Time.frameCount % 60 == 0)
                {
                    Debug.Log($"[Clock] Time: {CurrentTimeSeconds:F3}s");
                }
            }
        }
        
        #endregion
        
        #region Initialization
        
        public void Initialize()
        {
            ResetTime();
        }
        
        public void ResetTime()
        {
            _startTimeMillis = (DateTime.UtcNow - UnixEpoch).TotalMilliseconds;
            _lastTimeMillis = _startTimeMillis;
            _pauseTimeOffset = 0;
            
            if (logTimeChanges)
            {
                Debug.Log($"[Clock] Initialized at {CurrentTimeSeconds:F3}s since epoch");
            }
        }
        
        #endregion
        
        #region Time Management
        
        private double GetCurrentTimeMillis()
        {
            if (useRealTime)
            {
                double realTime = (DateTime.UtcNow - UnixEpoch).TotalMilliseconds;
                return _isPaused ? _pauseTimeOffset : realTime - _startTimeMillis;
            }
            else
            {
                return Time.time * 1000 * timeScale;
            }
        }
        
        /// <summary>
        /// Get the current time as raw milliseconds
        /// </summary>
        public double GetCurrentTimeRaw()
        {
            return GetCurrentTimeMillis();
        }
        
        /// <summary>
        /// Pause the simulation time
        /// </summary>
        public void Pause()
        {
            if (_isPaused) return;
            
            _pauseTimeOffset = GetCurrentTimeMillis();
            _isPaused = true;
            
            OnPauseStateChanged?.Invoke(true);
            
            if (logTimeChanges)
            {
                Debug.Log($"[Clock] Paused at {CurrentTimeSeconds:F3}s");
            }
        }
        
        /// <summary>
        /// Resume the simulation time
        /// </summary>
        public void Resume()
        {
            if (!_isPaused) return;
            
            _startTimeMillis += GetCurrentTimeMillis() - _pauseTimeOffset;
            _isPaused = false;
            
            OnPauseStateChanged?.Invoke(false);
            
            if (logTimeChanges)
            {
                Debug.Log($"[Clock] Resumed at {CurrentTimeSeconds:F3}s");
            }
        }
        
        /// <summary>
        /// Toggle pause state
        /// </summary>
        public void TogglePause()
        {
            if (_isPaused)
                Resume();
            else
                Pause();
        }
        
        /// <summary>
        /// Set time scale
        /// </summary>
        public void SetTimeScale(float scale)
        {
            timeScale = Mathf.Max(0f, scale);
            Time.timeScale = timeScale;
            
            if (logTimeChanges)
            {
                Debug.Log($"[Clock] Time scale set to {timeScale}");
            }
        }
        
        #endregion
        
        #region Editor Utilities
        
        [ContextMenu("Log Current Time")]
        private void EditorLogTime()
        {
            Debug.Log($"[Clock] Current time: {CurrentTimeSeconds:F6}s ({CurrentTimeMillis:F0}ms)");
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