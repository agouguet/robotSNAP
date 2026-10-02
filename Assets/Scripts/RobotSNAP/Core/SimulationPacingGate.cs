using UnityEngine;

namespace RobotSNAP.Core
{
    /// <summary>
    /// Lets a client take the session one control period at a time instead of letting it free-run.
    ///
    /// A free-running session is paced by the client's clock, and that clock is the client's own: a policy that
    /// takes twenty milliseconds to answer while the world runs at a hundred times speed has let two seconds of
    /// the world go by on the previous command - twenty times the control period the training was written
    /// around - and a slower policy silently changes the task. It also makes the two ends disagree about time:
    /// the episode limit is counted in the world's seconds, but the world advances while nobody is deciding.
    ///
    /// In lockstep mode the world stops instead. A client releases one period, the world spends exactly that
    /// much of its own time - rounded up to the physics step, the smallest quantum it has - and stops again.
    /// The control period is then a period of the simulation at any speed, a heavy policy costs wall time and
    /// not fidelity, and an episode cannot run past the world time it was given. The speed a session reaches is
    /// the machine's, so the time scale becomes a request rather than a promise - which is what it always was.
    /// </summary>
    public class SimulationPacingGate : MonoBehaviour
    {
        [Tooltip("Log every release and every stop.")]
        [SerializeField] private bool logChanges = false;

        [Tooltip(
            "A client that stops releasing for this many seconds of wall time has gone away, and the gate " +
            "hands the clock back rather than leaving the scene stopped for whoever comes next. Zero " +
            "disables the guard.")]
        [SerializeField] private float releaseLeaseSeconds = 30f;

        // The scale the world runs at while a period is released. Read from the session's configuration at
        // every release, so a client that changes the scale between two steps is obeyed on the next one.
        private float _scale = 1f;
        // What the client asked for, kept beside the scale that was actually used so a message can say the
        // difference instead of hiding it.
        private float _requested = 1f;
        private Clock _clock;
        private double _releasedAt;
        private bool _released;
        // The period counted in physics steps instead of seconds. See StepsForPeriod.
        private int _stepsToSpend = 1;
        private int _stepsSpent;
        // When the last release arrived, on the wall clock - which keeps running at a zero time scale, and is
        // therefore the only clock that can say a client has stopped talking. See Update.
        private float _lastReleaseWall;
        // The catch-up ceiling the gate found when it took the clock, so the lease can put back what it
        // replaced instead of leaving its own value behind on a session it no longer paces.
        private float _previousMaximumDeltaTime;
        private bool _hasPreviousMaximumDeltaTime;

        /// <summary>True while a client is taking the session one period at a time.</summary>
        public bool IsLockstep { get; private set; }

        /// <summary>The simulated seconds one release grants, rounded up to a physics step in practice.</summary>
        public float StepSeconds { get; private set; }

        /// <summary>The scale a release actually runs at, which is the requested one or less.</summary>
        public float EffectiveScale => _scale;

        /// <summary>The scale the client asked for, whether or not it was usable at this period.</summary>
        public float RequestedScale => _requested;

        /// <summary>Whether the requested scale was reduced to one an engine frame can resolve.</summary>
        public bool ScaleClamped => _requested > 0f && _scale < _requested;

        /// <summary>
        /// The most wall time one frame may catch up on, in seconds of wall time, for the budget the period has
        /// <em>left</em>: the steps still to spend, times a physics step, over the scale they are spent at.
        ///
        /// It has to be enough for the work that remains, or the world cannot spend it at all: a session at a
        /// single times speed on an editor that draws at forty frames a second advances twenty milliseconds of
        /// simulation per thirty-three millisecond frame, so a ceiling under that leaves the world behind its own
        /// clock. It also has to keep a frame from carrying more than what is left, because the stop is written
        /// from a physics step and the steps a frame has already queued still run, so bounding the frame by the
        /// budget left - and not by the whole period - is what keeps a released period one period. Measured live
        /// at ten times speed, a ten-step period was being spent in about fifteen steps, because the ceiling
        /// still named the whole period on the frame that had only two steps left to run.
        ///
        /// The engine refuses a value below one physics step - written live, 0.004 on a 0.02 step reads back
        /// 0.02 - so the floor here is the engine's own and not a choice. At a scale where a single frame wants
        /// several steps, the tail of a period is landed by the scale instead: see
        /// <see cref="BindTheRemainingBudget"/>.
        /// </summary>
        public float CatchUpCeiling
        {
            get
            {
                int remaining = Mathf.Max(0, _stepsToSpend - _stepsSpent);
                float perFrame = remaining * Time.fixedDeltaTime;
                if (_scale > 0f)
                    perFrame /= _scale;
                return Mathf.Max(Time.fixedDeltaTime, perFrame);
            }
        }

        /// <summary>Whether a granted period is being spent right now.</summary>
        public bool IsReleased => _released;

        /// <summary>Physics steps one release still has to spend, and how many of them it has spent.</summary>
        public int StepsToSpend => _stepsToSpend;
        public int StepsSpent => _stepsSpent;

        /// <summary>
        /// The wall instant of the last release, and the seconds of silence after which the gate gives the
        /// clock back. Exposed so an edit-mode test can reason about the lease without waiting thirty seconds
        /// of real time, and so a caller can see how long a client has been quiet.
        /// </summary>
        public float LastReleaseWall => _lastReleaseWall;

        /// <summary>The guard's budget in seconds of wall time; zero means it never fires.</summary>
        public float ReleaseLeaseSeconds => releaseLeaseSeconds;

        /// <summary>
        /// Take the session one period at a time. It stops at once and waits for <see cref="Release"/>; the
        /// period is <paramref name="seconds"/> of simulated time and the world runs at <paramref name="scale"/>
        /// while it is being spent.
        /// </summary>
        public void SetLockstep(float seconds, float scale)
        {
            _clock ??= Clock.Instance;
            StepSeconds = Mathf.Max(0f, seconds);
            _requested = Mathf.Max(0f, scale);
            _scale = ClampScale(_requested, StepSeconds);
            // The budget the next release will grant, so the ceiling below is the period's until a release has
            // spent from it. The stop is counted in steps and the ceiling is sized to the steps that are left.
            _stepsToSpend = StepsForPeriod(StepSeconds, Time.fixedDeltaTime);
            _stepsSpent = 0;

            // The finest granularity the engine has is the drawn frame: it spends at least one physics step
            // of simulation per frame, so at scale S a frame can carry up to fixedDeltaTime * S of it however
            // this gate writes in between - a scale of a hundred on a fifty millisecond step spends five
            // seconds of simulation in the frame that follows a release, and the period the client asked for
            // is over twenty-five times before anything here sees it again. Bounding the catch-up to the budget
            // that is left, and holding the scale inside the period in ClampScale, is what keeps the promise
            // without throttling the session. See CatchUpCeiling.
            // Captured once: a second lockstep that follows the first without a SetFree in between would
            // otherwise record the gate's own ceiling as the one to put back, and restore a value it wrote.
            if (!_hasPreviousMaximumDeltaTime)
            {
                _previousMaximumDeltaTime = Time.maximumDeltaTime;
                _hasPreviousMaximumDeltaTime = true;
            }
            Time.maximumDeltaTime = CatchUpCeiling;
            IsLockstep = true;
            _released = false;
            Time.timeScale = 0f;
            _lastReleaseWall = Time.realtimeSinceStartup;
            if (logChanges)
                Debug.Log(
                    $"[PacingGate] lockstep, {StepSeconds} s of simulation per release at scale {_scale}" +
                    (ScaleClamped ? $" (asked {_requested})" : ""));
        }

        /// <summary>Give the session back to the scale it was configured with.</summary>
        public void SetFree(float scale)
        {
            IsLockstep = false;
            _released = false;
            _scale = Mathf.Max(0f, scale);
            Time.timeScale = _scale;
            RestoreCatchUp();
            if (logChanges)
                Debug.Log($"[PacingGate] free running at scale {_scale}");
        }

        /// <summary>
        /// Gives the clock back to the session, for a client that stopped releasing.
        ///
        /// Lockstep is a loan of the clock, and it was written as one: the client that took it returns it in
        /// <see cref="SetFree"/> or in the environment's own teardown. What that missed is the client that
        /// never gets to run its teardown - a training run interrupted with Ctrl-C, a killed process, a
        /// script that threw - and the loan then has no way back. The world stays at a zero time scale, the
        /// physics loop stops, and because the state stream is published from that loop the session goes
        /// silent as well: the scene reads as lagging or frozen to whoever is watching, and the next client
        /// is refused for as long as it waits for a snapshot. This is that loan's expiry.
        /// </summary>
        public void GiveBackTheClock()
        {
            if (!IsLockstep)
                return;

            IsLockstep = false;
            _released = false;
            Time.timeScale = Mathf.Max(0f, _scale);
            RestoreCatchUp();
            Debug.LogWarning(
                $"[PacingGate] no release for {releaseLeaseSeconds:g} s: the client has gone, the session " +
                $"runs free at scale {_scale} again");
        }

        /// <summary>
        /// Whether <paramref name="wallNow"/> is past the lease the client was given. A lease of zero never
        /// expires, and a session the user paused on purpose is not a client that has gone: the pause is the
        /// clock being held deliberately, and taking the clock back would undo it.
        /// </summary>
        public bool ShouldGiveBackTheClock(float wallNow)
        {
            if (!IsLockstep) return false;
            if (releaseLeaseSeconds <= 0f) return false;
            if (_clock != null && _clock.IsPaused) return false;
            return wallNow - _lastReleaseWall >= releaseLeaseSeconds;
        }

        /// <summary>Puts back the catch-up ceiling the gate replaced when it took the clock.</summary>
        private void RestoreCatchUp()
        {
            if (!_hasPreviousMaximumDeltaTime) return;
            _hasPreviousMaximumDeltaTime = false;
            Time.maximumDeltaTime = _previousMaximumDeltaTime;
        }

        /// <summary>
        /// Spend one period. Called once per control step by the client, after the command whose effect the
        /// period is meant to show: the release is what lets the world move, so a command that arrives with it
        /// or before it is the one the whole period is driven by.
        ///
        /// A release that arrives while a period is already being spent is the client re-arming one that is in
        /// flight, and it is ignored: resetting the step count here started the period over, so the world ran one
        /// more whole period than it was asked for and the gate never reached the stop. The period in flight
        /// wins, and the next release is honoured once the world has stopped again.
        /// </summary>
        public void Release(float scale)
        {
            if (!IsLockstep)
                return;

            if (_released)
                return;

            _clock ??= Clock.Instance;
            _requested = Mathf.Max(0f, scale);
            _scale = ClampScale(_requested, StepSeconds);
            _stepsToSpend = StepsForPeriod(StepSeconds, Time.fixedDeltaTime);
            _stepsSpent = 0;
            Time.maximumDeltaTime = CatchUpCeiling;
            _releasedAt = _clock != null ? _clock.CurrentTimeSeconds : 0.0;
            _lastReleaseWall = Time.realtimeSinceStartup;
            _released = true;
            Time.timeScale = _scale;
            if (logChanges)
                Debug.Log(
                    $"[PacingGate] released {StepSeconds} s from {_releasedAt:F3} at scale {_scale}" +
                    (ScaleClamped ? $" (asked {_requested})" : ""));
        }

        /// <summary>
        /// How many physics steps of simulation one release grants.
        ///
        /// The period is a length of simulated time, and the physics step is the smallest slice of it the
        /// engine can spend, so the number of steps is the period divided by the step - rounded to the nearest
        /// one the engine can actually take, and never below one.
        ///
        /// Counting the steps is what makes the promise hold in a session that carries no clock at all, which
        /// is the case this used to get wrong: the stop was written as a comparison against the simulation
        /// clock, so a scene whose clock was missing - no Clock component, or one whose registration was lost
        /// - never reached the comparison, and the gate quietly stopped nothing. Lockstep then behaved exactly
        /// like free running, with no message anywhere to say the mode had been dropped. A physics step is the
        /// engine's own quantum and needs no clock to be counted, so the period is now a count. It also lands
        /// the stop on the period itself rather than a step past it, since the comparison against a clock read
        /// at the release instant used to round the period up by one step.
        /// </summary>
        public static int StepsForPeriod(float stepSeconds, float fixedTimestep)
        {
            if (fixedTimestep <= 0f || stepSeconds <= 0f)
                return 1;
            return Mathf.Max(1, Mathf.RoundToInt(stepSeconds / fixedTimestep));
        }

        /// <summary>
        /// The largest scale at which one frame's physics still fits inside <paramref name="stepSeconds"/>.
        ///
        /// A frame carries at least one physics step, so the largest scale whose frame can be contained in
        /// the period is the period divided by the step. Past it the engine, not this gate, decides how far
        /// the world moves per frame, and a client that asked for a period of 0.2 s at a scale of 100 on a
        /// 20 ms step would watch it spend two seconds per frame.
        /// </summary>
        public static float MaxScaleForStep(float stepSeconds, float fixedTimestep)
        {
            if (stepSeconds <= 0f)
                return 0f;
            if (fixedTimestep <= 0f)
                return float.PositiveInfinity;
            return stepSeconds / fixedTimestep;
        }

        /// <summary>
        /// The largest scale at which a frame of <paramref name="frameWallSeconds"/> seconds of wall time still
        /// queues no more than <paramref name="stepsLeft"/> physics steps.
        ///
        /// A frame runs one physics step per <c>fixedTimestep</c> of scaled simulation, so the steps it queues
        /// are the frame's wall time times the scale, over the step. Holding the scale to this value is what
        /// lands the stop on the budget that is left once the engine's own floor on the catch-up makes
        /// <see cref="CatchUpCeiling"/> too coarse to do it - see <see cref="BindTheRemainingBudget"/>.
        /// </summary>
        public static float ScaleForStepsLeft(int stepsLeft, float frameWallSeconds, float fixedTimestep)
        {
            if (frameWallSeconds <= 0f || fixedTimestep <= 0f || stepsLeft <= 0)
                return 0f;
            return stepsLeft * fixedTimestep / frameWallSeconds;
        }

        private static float ClampScale(float requested, float stepSeconds)
        {
            return Mathf.Min(requested, MaxScaleForStep(stepSeconds, Time.fixedDeltaTime));
        }

        /// <summary>
        /// Stops the world the moment the period is spent. Checked on the physics step and not on the frame,
        /// because the frame is the wrong quantum for this: a session drawing at twenty hertz while the world
        /// runs ten times faster would notice a spent period two hundred milliseconds of simulation late, and
        /// a frame that draws at one hertz would let a whole second of it through. The period is spent in
        /// steps and not in clock seconds - see <see cref="StepsForPeriod"/> - so a scene that carries no clock
        /// is paced exactly like one that does.
        /// </summary>
        private void FixedUpdate()
        {
            if (!IsLockstep)
                return;

            if (!_released)
                return;

            _stepsSpent++;
            if (_stepsSpent < _stepsToSpend)
                return;

            _released = false;
            Time.timeScale = 0f;
        }

        /// <summary>
        /// Keeps a waiting world stopped, and sizes the frame that is spending a release. The world is only ever
        /// moved by a release, so nothing else that writes <c>Time.timeScale</c> - a scale applied from the
        /// configuration, say - can start it behind the client's back. A paused session stays paused: the gate
        /// lends the clock a budget, it does not take the pause away.
        /// </summary>
        private void Update()
        {
            if (!IsLockstep)
                return;

            _clock ??= Clock.Instance;

            if (ShouldGiveBackTheClock(Time.realtimeSinceStartup))
            {
                GiveBackTheClock();
                return;
            }

            if (!_released)
            {
                if (!(_clock != null && _clock.IsPaused))
                    Time.timeScale = 0f;
                return;
            }

            // Re-bound every frame and not only at the release: a frame queues its physics steps before this
            // gate is looked at again, so what the frame may carry has to follow the budget down as the steps
            // are spent, or the last frame spends the tail of the period plus a whole frame's worth of steps.
            BindTheRemainingBudget();
        }

        /// <summary>
        /// Sizes the next frame to the budget the period has left, so a frame can never queue more physics
        /// steps than remain.
        ///
        /// <c>Time.maximumDeltaTime</c> is the bound a hitch is caught by - it stops one long frame from
        /// spending more than the period - but the engine refuses it a value below one physics step (written
        /// live, 0.004 on a 0.02 step reads back 0.02), so at a scale where a frame wants several steps it
        /// cannot by itself shrink to the two or three that are left. The scale is the second handle and the one
        /// that lands the stop there: a frame is sized for the wall time of the frame just measured, and never
        /// for less than one physics step, so it carries at most the steps that remain instead of the half
        /// period a frame at the requested scale would. The scale goes back to the requested one on the next
        /// release, and the whole period still runs at it - only its tail is slowed.
        /// </summary>
        private void BindTheRemainingBudget()
        {
            Time.maximumDeltaTime = CatchUpCeiling;

            int remaining = _stepsToSpend - _stepsSpent;
            if (remaining <= 0)
                return;

            float frameWall = Mathf.Max(Time.unscaledDeltaTime, Time.fixedDeltaTime);
            Time.timeScale = Mathf.Min(_scale, ScaleForStepsLeft(remaining, frameWall, Time.fixedDeltaTime));
        }
    }
}
