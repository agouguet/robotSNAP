using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace RobotSNAP.Core
{
    /// <summary>
    /// Manages simulation time, either in real-time (UTC) or simulated (with a start hour).
    /// Supports named time zones for display purposes.
    ///
    /// The elapsed time this clock reports is the simulation's, not the wall's: it advances by one physics
    /// step at a time, so it says how far the world has moved. It is the value <c>/clock</c>, the header of
    /// every stamped message and the <c>sim_time_seconds</c> of the state snapshot carry, which is what lets
    /// a client run the simulation faster without any of its streams disagreeing about when they happened.
    /// The two modes differ only in the hour the reported time is expressed from - the real UTC hour, or the
    /// configured start hour - never in how fast it advances.
    ///
    /// Time is counted on the physics step rather than once per rendered frame because the world moves on
    /// that step. A frame carries as many physics steps as the session's speed and the machine's frame rate
    /// disagree by - ten of them at ten times speed on a six hertz frame - and a stamp that only moves once
    /// per frame then names ten different poses at once. Every stamped stream, the lidar and the odometry
    /// alike, does not agree with itself any more, and a client that places a scan at the pose of its stamp
    /// turns the world by however far the robot moved inside that one stamp.
    ///
    /// The scale is owned by <see cref="RobotSNAP.Core.SimulationConfig"/>: the command router writes the
    /// same value to <c>Time.timeScale</c> (which scales the physics and every <c>Time.deltaTime</c>) and to
    /// this clock, so the world and the clock it is stamped with stay one thing.
    /// </summary>
    [ExecuteAlways]
    public class Clock : MonoBehaviour
    {
        private static Clock _instance;

        // When the property last looked for a clock and found none. The search below is a scene query, and
        // the callers of this property are per-frame ones, so a session that genuinely carries no clock must
        // not pay for a query on every frame it asks the question.
        private static float _lastEmptySearch;
        private const float SearchInterval = 0.5f;

        /// <summary>
        /// The session's clock: the one the loaded scenes carry, or null in a session that carries none.
        ///
        /// Registration is done by <see cref="Awake"/>, and a static that only a lifecycle callback ever
        /// writes is a static that can be lost while its object lives on. A script reload during a play
        /// session is exactly that: the managed side is rebuilt, Awake does not run again on the objects that
        /// are already there, and the scene's clock is orphaned. Everything that asks this property then
        /// reads null - the snapshots stop carrying <c>sim_time_seconds</c>, the status bar falls back to the
        /// wall clock, the pacing gate has no clock to stop against - while the clock itself sits in the
        /// scene, enabled, ticking. So the property looks for it as well, at most twice a second while it
        /// finds nothing, and remembers what it finds.
        ///
        /// It never creates a clock and never wakes a disabled one: creating one is what
        /// <see cref="EnsureExists"/> is for, and it is a decision a caller makes on purpose.
        /// </summary>
        public static Clock Instance
        {
            get
            {
                if (_instance != null)
                    return _instance;

                float now = Time.realtimeSinceStartup;
                if (now - _lastEmptySearch < SearchInterval)
                    return null;
                _lastEmptySearch = now;

                Clock found = FindAnyObjectByType<Clock>(FindObjectsInactive.Include);
                if (found != null)
                    _instance = found;
                return _instance;
            }
        }

        [Header("Settings")]
        [SerializeField] private bool useRealTime = true;
        [Tooltip("Simulation speed: the world, and this clock with it, advances this many seconds per second of wall time. Time.timeScale is set from the same value, see SimulationConfig.ApplyTimeSettings.")]
        [SerializeField] private float timeScale = 1f;
        [SerializeField] private bool autoStart = true;

        [Header("Simulation Start (ignored if useRealTime)")]
        [SerializeField] [Range(0, 24)] private float simulationStartHour = 8f;

        [Header("Time Zone (Advanced)")]
        [Tooltip("The IANA time zone ID (e.g., 'Europe/Paris', 'America/New_York')")]
        [SerializeField] private string _timeZoneId = "UTC";

        [Header("Debug")]
        [SerializeField] private bool logTimeChanges = false;

        [Header("Editor Debug (Read-Only)")]
        [SerializeField] private string editorTimeDisplay = "00:00:00";

        // Internal state
        // Simulated seconds elapsed since Initialize, advanced by the scaled frame time. This is the one
        // clock /clock, every stamped message and the state snapshot read, so it follows the time scale
        // rather than the wall: at a scale of five, the world moves five seconds in one second of wall time
        // and this clock says five seconds too.
        private double _elapsedSeconds = 0.0;
        // The reported clock, frozen while paused. It carries the simulation start hour in simulated mode,
        // which is why a pause stores the value that was reported rather than the raw elapsed one.
        private double _pauseTimeSeconds = 0f;
        private bool _isPaused = false;
        private double _lastTimeMillis;
        private float _editorDisplayUpdateTimer = 0f;
        private bool _isInitialized = false;
        private bool _previousUseRealTime;

        // Time zone
        private TimeZoneInfo _timeZone;

        // Events
        public event Action<double> OnTimeUpdated;
        public event Action<bool> OnPauseStateChanged;

        // Public properties
        public double CurrentTimeMillis => GetCurrentTimeMillis();
        public double CurrentTimeSeconds => CurrentTimeMillis / 1000.0;
        public bool IsPaused => _isPaused;
        public float TimeScale => timeScale;
        public bool UseRealTime => useRealTime;
        public DateTime LocalNow => GetLocalTime();

        /// <summary>
        /// Simulated seconds elapsed since the last <see cref="Initialize"/>: how far the world has moved
        /// since the mission was restarted. It is the counter the <c>sim_time_seconds</c> key of the state
        /// snapshot is read from, so a display that reads it here cannot drift from what a client is told.
        /// Unlike <see cref="CurrentTimeSeconds"/> it carries neither the simulated start hour nor the pause
        /// freeze - it is only the elapsed counter, and <see cref="Initialize"/> puts it back to zero.
        /// </summary>
        public double ElapsedSeconds => _elapsedSeconds;

        // Unix epoch
        public static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        #region Unity Lifecycle

        private void Awake()
        {
            // Unity's null is folded in on purpose: the static survives a scene reload and may be holding the
            // destroyed object a previous session left, which must not be mistaken for a clock that is holding
            // the time. A clock that is switched off does not hold it either - Awake still runs on a disabled
            // component - so the awake one takes over from it.
            if (_instance != null && _instance != this && _instance)
            {
                if (_instance.isActiveAndEnabled)
                {
                    // A second clock is not a second opinion: every stream is stamped from the one the static
                    // holds while a publisher that woke up holding the other one would stamp the same instant
                    // differently, and a client would see two clocks that disagree about how far the world has
                    // moved - which is exactly what happened while two of them ran sixteen seconds apart. The
                    // *component* goes and it is loud about it, but not the object it sits on: a clock added by
                    // mistake to an authored object must not take that object with it.
                    Debug.LogWarning(
                        $"[Clock] a second clock was added on '{name}' while '{_instance.name}' already holds " +
                        "the time: the new component is being removed", this);
                    Remove(this);
                    return;
                }
            }
            _instance = this;

            if (Application.isPlaying)
            {
                DontDestroyOnLoad(gameObject);
            }

            LoadTimeZone();
        }

        /// <summary>
        /// The session's clock, created only if the loaded scenes carry none.
        ///
        /// Every caller that needs a clock - the supervisor, the ROS bridge - used to build one itself when
        /// <see cref="Instance"/> was still null, which is the state it is in before the scene's own clock has
        /// had its Awake. Two of those callers waking in the same frame therefore built two clocks, and a
        /// client watched a session whose time was kept twice, sixteen seconds apart, with the gate stopping
        /// the world against one of them. Finding the component first - inactive objects included, because a
        /// clock that is merely switched off is still the clock - is what makes "there is one clock" true
        /// rather than hopeful.
        /// </summary>
        public static Clock EnsureExists()
        {
            if (_instance != null)
                return _instance;

            Clock found = FindAnyObjectByType<Clock>(FindObjectsInactive.Include);
            if (found != null)
                return found;

            var clockGO = new GameObject("Clock");
            found = clockGO.AddComponent<Clock>();
            if (Application.isPlaying)
                DontDestroyOnLoad(clockGO);
            Debug.Log("[Clock] no clock in the loaded scenes: created one");
            return found;
        }

        /// <summary>
        /// Silences a redundant clock component, in the way the current mode allows: play mode takes it off
        /// at the end of the frame, and the editor only switches it off, because destroying it there would
        /// edit a scene the user authored.
        /// </summary>
        private static void Remove(Clock duplicate)
        {
            if (duplicate == null)
                return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                duplicate.enabled = false;
                return;
            }
#endif
            if (Application.isPlaying)
                Destroy(duplicate);
            else
                duplicate.enabled = false;
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
            // Reload time zone if the ID changed
            LoadTimeZone();

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

            // The frame does not move the clock - FixedUpdate does, see AdvanceSimulated - so this only
            // announces the time the physics loop has reached and refreshes the readouts that follow it.
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

            // Update editor display every 0.5s
            _editorDisplayUpdateTimer += Time.deltaTime;
            if (_editorDisplayUpdateTimer >= 0.5f)
            {
                _editorDisplayUpdateTimer = 0f;
                UpdateEditorDisplay();
            }
        }

        /// <summary>
        /// Advances the clock by one physics step. The world moves on that step - the agents, the lidar and
        /// the pose every publisher reads - so this is the instant a stamped message names.
        /// <c>Time.fixedDeltaTime</c> is already a length of simulated time and the physics loop already runs
        /// it <c>timeScale</c> times more often per second of wall time, so the scale is not applied again
        /// here: at a scale of five a second of wall time carries five seconds of simulation, as it should.
        /// </summary>
        private void FixedUpdate()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return;
#endif

            if (!autoStart || !_isInitialized) return;

            AdvanceSimulated(Time.fixedDeltaTime);
        }

        #endregion

        #region Time Zone Management

        /// <summary>
        /// Loads the time zone from the configured ID.
        /// Falls back to UTC if the ID is not valid.
        /// </summary>
        private void LoadTimeZone()
        {
            try
            {
                _timeZone = TimeZoneInfo.FindSystemTimeZoneById(_timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                Debug.LogWarning($"[Clock] TimeZone '{_timeZoneId}' not found. Using UTC.");
                _timeZone = TimeZoneInfo.Utc;
                _timeZoneId = "UTC";
            }
            catch (InvalidTimeZoneException)
            {
                Debug.LogWarning($"[Clock] Invalid TimeZone '{_timeZoneId}'. Using UTC.");
                _timeZone = TimeZoneInfo.Utc;
                _timeZoneId = "UTC";
            }
        }

        /// <summary>
        /// Gets the current local time according to the selected time zone.
        /// </summary>
        public DateTime GetLocalTime()
        {
            DateTime utc = DateTime.UtcNow;
            return TimeZoneInfo.ConvertTimeFromUtc(utc, _timeZone);
        }

        /// <summary>
        /// Returns the current time zone ID.
        /// </summary>
        public string GetTimeZoneId() => _timeZoneId;

        /// <summary>
        /// Sets the time zone by ID and reloads it.
        /// </summary>
        public void SetTimeZone(string timeZoneId)
        {
            if (_timeZoneId == timeZoneId) return;
            _timeZoneId = timeZoneId;
            LoadTimeZone();
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UpdateEditorDisplay();
#endif
        }

        #endregion

        #region Editor Display

        /// <summary>
        /// Updates the editor display string with the current time (respecting time zone).
        /// </summary>
        private void UpdateEditorDisplay()
        {
            if (useRealTime)
            {
                DateTime local = GetLocalTime();
                editorTimeDisplay = local.ToString("HH:mm:ss");
            }
            else
            {
                // For simulated time, apply the time zone offset to the display
                TimeSpan offset = _timeZone.GetUtcOffset(DateTime.UtcNow);
                double secondsInDay = (CurrentTimeSeconds % 86400.0) + offset.TotalSeconds;
                if (secondsInDay < 0) secondsInDay += 86400.0;
                if (secondsInDay >= 86400.0) secondsInDay -= 86400.0;
                int hours = Mathf.FloorToInt((float)(secondsInDay / 3600.0));
                int minutes = Mathf.FloorToInt((float)((secondsInDay % 3600.0) / 60.0));
                int seconds = Mathf.FloorToInt((float)(secondsInDay % 60.0));
                editorTimeDisplay = $"{hours:D2}:{minutes:D2}:{seconds:D2}";
            }
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes the clock. Resets time to start of simulation or real time.
        /// </summary>
        public void Initialize()
        {
            // Both modes restart the same elapsed counter; what differs is only the hour the reported time
            // is expressed from, which GetCurrentTimeMillis adds back in simulated mode.
            _elapsedSeconds = 0.0;
            _pauseTimeSeconds = 0.0;
            _isPaused = false;
            _lastTimeMillis = GetCurrentTimeMillis();

            _isInitialized = true;
            _previousUseRealTime = useRealTime;

            UpdateEditorDisplay();

            if (logTimeChanges)
            {
                Debug.Log($"[Clock] Initialized. Mode: {(useRealTime ? "RealTime" : "Simulation")}, StartHour: {simulationStartHour}h, Simulated seconds: {_elapsedSeconds}");
            }
        }

        /// <summary>
        /// Resets the time to the initial state.
        /// </summary>
        public void ResetTime()
        {
            Initialize();
        }

        #endregion

        #region Time Management

        /// <summary>
        /// Gets the current time in milliseconds.
        /// </summary>
        private double GetCurrentTimeMillis()
        {
            if (_isPaused)
                return _pauseTimeSeconds * 1000;

            double seconds = _elapsedSeconds + (useRealTime ? 0.0 : simulationStartHour * 3600.0);
            return seconds * 1000.0;
        }

        /// <summary>
        /// Pauses the clock.
        /// </summary>
        public void Pause()
        {
            if (_isPaused) return;
            _pauseTimeSeconds = GetCurrentTimeMillis() / 1000.0;
            _isPaused = true;
            OnPauseStateChanged?.Invoke(true);
            if (logTimeChanges)
                Debug.Log($"[Clock] Paused at {_pauseTimeSeconds:F3}s");
        }

        /// <summary>
        /// Resumes the clock.
        /// </summary>
        public void Resume()
        {
            if (!_isPaused) return;
            // The elapsed counter did not move while paused, so the value the pause froze is the one the
            // clock resumes on. The simulated mode expresses its reported time from the start hour, so that
            // offset has to come back out of the frozen value first.
            if (!useRealTime)
                _elapsedSeconds = Math.Max(0.0, _pauseTimeSeconds - simulationStartHour * 3600.0);
            _isPaused = false;
            _lastTimeMillis = GetCurrentTimeMillis();
            OnPauseStateChanged?.Invoke(false);
            if (logTimeChanges)
                Debug.Log($"[Clock] Resumed at {CurrentTimeSeconds:F3}s");
        }

        /// <summary>
        /// Toggles pause state.
        /// </summary>
        public void TogglePause()
        {
            if (_isPaused) Resume(); else Pause();
        }

        /// <summary>
        /// Sets the time scale.
        /// </summary>
        public void SetTimeScale(float scale)
        {
            timeScale = Mathf.Max(0f, scale);
            if (logTimeChanges) Debug.Log($"[Clock] Time scale set to {timeScale}");
        }

        /// <summary>
        /// Advances the clock by one step of <paramref name="simulatedSeconds"/> seconds of simulation time,
        /// the length of a physics step. This is what the running clock does, once per FixedUpdate, and what
        /// a test can call without waiting on real steps. Does nothing while paused.
        /// </summary>
        public void AdvanceSimulated(double simulatedSeconds)
        {
            if (_isPaused) return;
            _elapsedSeconds += Math.Max(0.0, simulatedSeconds);
        }

        /// <summary>
        /// Advances the clock by one frame of <paramref name="unscaledDeltaSeconds"/> seconds of wall time,
        /// scaled by <see cref="timeScale"/>. This is the wall rule, kept for a caller that measures frames
        /// of wall time and for the edit-mode test that pins it; the running clock counts physics steps
        /// through <see cref="AdvanceSimulated"/> instead, because that is what the world moves on. Does
        /// nothing while paused.
        /// </summary>
        public void Advance(double unscaledDeltaSeconds)
        {
            if (_isPaused) return;
            _elapsedSeconds += Math.Max(0.0, unscaledDeltaSeconds) * timeScale;
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
