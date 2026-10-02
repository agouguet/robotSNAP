using System.Reflection;
using NUnit.Framework;
using RobotSNAP.Core;
using UnityEngine;

namespace RobotSNAP.Tests.Editor
{
    /// <summary>
    /// What a control period can hold at a given speed.
    ///
    /// The engine moves the world on the drawn frame, and a frame carries at least one physics step, so the
    /// largest scale whose frame still fits inside a control period is the period divided by the step. Past
    /// it a single frame spends more simulation than the period the client asked for and whatever budget the
    /// episode was given is gone before the gate is looked at again - measured live, a period of a fifth of a
    /// second at a scale of a hundred spent five seconds of simulation in the one frame that followed a
    /// release, on a fifty millisecond step. These tests pin the arithmetic that keeps that from happening.
    /// </summary>
    public sealed class SimulationPacingTests
    {
        private GameObject _host;
        private SimulationPacingGate _gate;
        private float _originalTimeScale;
        private float _originalDeltaTime;
        private float _originalMaximumDeltaTime;

        [SetUp]
        public void SetUp()
        {
            _originalTimeScale = Time.timeScale;
            _originalDeltaTime = Time.fixedDeltaTime;
            _originalMaximumDeltaTime = Time.maximumDeltaTime;

            _host = new GameObject("pacing_gate_under_test");
            _gate = _host.AddComponent<SimulationPacingGate>();
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _originalTimeScale;
            Time.fixedDeltaTime = _originalDeltaTime;
            Time.maximumDeltaTime = _originalMaximumDeltaTime;
            if (_host != null)
                Object.DestroyImmediate(_host);
        }

        [Test]
        public void TheLargestUsableScaleIsThePeriodOverThePhysicsStep()
        {
            Assert.That(SimulationPacingGate.MaxScaleForStep(0.2f, 0.02f), Is.EqualTo(10f).Within(1e-6f));
            Assert.That(SimulationPacingGate.MaxScaleForStep(2f, 0.02f), Is.EqualTo(100f).Within(1e-6f));
            Assert.That(SimulationPacingGate.MaxScaleForStep(0.2f, 0.05f), Is.EqualTo(4f).Within(1e-6f));
        }

        [Test]
        public void OnePeriodIsAWholeNumberOfPhysicsSteps()
        {
            // The period is spent in steps rather than in clock seconds, so that a scene with no clock is
            // paced exactly like one with a clock - see StepsForPeriod. The count is what the gate stops on.
            Assert.That(SimulationPacingGate.StepsForPeriod(0.2f, 0.02f), Is.EqualTo(10));
            Assert.That(SimulationPacingGate.StepsForPeriod(0.2f, 0.05f), Is.EqualTo(4));
            Assert.That(SimulationPacingGate.StepsForPeriod(2f, 0.02f), Is.EqualTo(100));
            Assert.That(
                SimulationPacingGate.StepsForPeriod(0.01f, 0.02f),
                Is.EqualTo(1),
                "a period shorter than the engine's own step is still one step, never none");
            Assert.That(
                SimulationPacingGate.StepsForPeriod(0.2f, 0f),
                Is.EqualTo(1),
                "a scene with a zero physics step must not divide by it");
        }

        [Test]
        public void AReleaseHandsOutThePeriodAsAStepCountAndNoStepYet()
        {
            Time.fixedDeltaTime = 0.02f;
            _gate.SetLockstep(0.2f, 1f);

            _gate.Release(1f);

            Assert.That(_gate.StepsToSpend, Is.EqualTo(10), "0.2 s at a 0.02 s step");
            Assert.That(_gate.StepsSpent, Is.EqualTo(0));
        }

        [Test]
        public void AReleaseWhileAPeriodIsBeingSpentIsIgnored()
        {
            // The bug this pins: a release that arrived while a granted period was still being spent re-armed
            // the period - it reset the step count and raised the scale - so the world ran one more whole period
            // than the client asked for and the gate never reached its stop. The period in flight wins, and the
            // steps it has already spent are not taken back.
            Time.fixedDeltaTime = 0.02f;
            _gate.SetLockstep(0.2f, 1f);
            _gate.Release(1f);
            SpendSteps(4);

            _gate.Release(50f);

            Assert.That(_gate.IsReleased, Is.True, "the period already granted is still the one being spent");
            Assert.That(_gate.StepsSpent, Is.EqualTo(4), "the steps already spent are not reset by the re-arming release");
            Assert.That(_gate.EffectiveScale, Is.EqualTo(1f).Within(1e-6f), "the dropped release did not raise the scale");
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void AReleaseIsHonouredAgainOnceThePeriodIsSpent()
        {
            Time.fixedDeltaTime = 0.02f;
            _gate.SetLockstep(0.2f, 1f);
            _gate.Release(1f);
            SpendSteps(_gate.StepsToSpend);
            Assert.That(_gate.IsReleased, Is.False, "the world stopped once the period was spent");

            _gate.Release(1f);

            Assert.That(_gate.IsReleased, Is.True, "a release after the stop is honoured");
            Assert.That(_gate.StepsSpent, Is.EqualTo(0));
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(1e-6f));
        }

        /// <summary>Spends physics steps the way the gate's own FixedUpdate does, without waiting on frames.</summary>
        private void SpendSteps(int steps)
        {
            for (int i = 0; i < steps; i++)
                InvokeGate("FixedUpdate");
        }

        /// <summary>Runs a private method of the gate, which the editor runs no lifecycle for outside Play.</summary>
        private void InvokeGate(string method) =>
            typeof(SimulationPacingGate)
                .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_gate, null);

        /// <summary>Runs the gate's own frame, the one that sizes the frame a release is spending.</summary>
        private void RunTheFrame() => InvokeGate("Update");

        [Test]
        public void AUsableScaleIsLeftAlone()
        {
            Time.fixedDeltaTime = 0.02f;

            _gate.SetLockstep(0.2f, 5f);

            Assert.That(_gate.EffectiveScale, Is.EqualTo(5f).Within(1e-6f));
            Assert.That(_gate.ScaleClamped, Is.False);
            Assert.That(_gate.RequestedScale, Is.EqualTo(5f).Within(1e-6f));
        }

        [Test]
        public void AScaleOneFrameCannotResolveIsHeldBack()
        {
            Time.fixedDeltaTime = 0.05f;

            _gate.SetLockstep(0.2f, 100f);

            Assert.That(_gate.EffectiveScale, Is.EqualTo(4f).Within(1e-6f), "0.2 s / 0.05 s");
            Assert.That(_gate.ScaleClamped, Is.True);
            Assert.That(
                _gate.RequestedScale,
                Is.EqualTo(100f).Within(1e-6f),
                "the ask is kept so a message can say what was reduced and why");
        }

        [Test]
        public void ALongControlPeriodHoldsTheScaleAFrameWouldSwallow()
        {
            Time.fixedDeltaTime = 0.02f;

            _gate.SetLockstep(2f, 100f);

            Assert.That(_gate.EffectiveScale, Is.EqualTo(100f).Within(1e-6f));
            Assert.That(_gate.ScaleClamped, Is.False);
        }

        [Test]
        public void LockstepStopsTheWorldAndTightensTheCatchUp()
        {
            Time.fixedDeltaTime = 0.02f;
            Time.maximumDeltaTime = 10f;

            _gate.SetLockstep(0.2f, 10f);

            Assert.That(Time.timeScale, Is.EqualTo(0f), "a lockstep session waits for a release");
            Assert.That(
                Time.maximumDeltaTime,
                Is.EqualTo(Time.fixedDeltaTime).Within(1e-6f),
                "a fifth of a second at ten times speed is one physics step of a frame, so the period and " +
                "the tightest value the engine accepts are the same number here");
        }

        [Test]
        public void TheCatchUpStillLetsTheWorldKeepRealTimeAtScaleOne()
        {
            // The bug this pins: bounding the catch-up to one physics step made a session at a single times
            // speed advance twenty milliseconds of simulation per thirty-three millisecond frame on an editor
            // drawing at thirty frames a second. The world fell behind the wall clock and looked like it was
            // lagging, whatever the client was doing. The ceiling has to be the period, not the step.
            Time.fixedDeltaTime = 0.02f;
            Time.maximumDeltaTime = 10f;

            _gate.SetLockstep(0.2f, 1f);

            Assert.That(
                Time.maximumDeltaTime,
                Is.EqualTo(0.2f).Within(1e-6f),
                "one fifth of a second at one times speed is a fifth of a second of wall time");
            Assert.That(
                Time.maximumDeltaTime,
                Is.GreaterThan(Time.fixedDeltaTime),
                "a frame may carry the whole period it was released for, or the world cannot keep up");
        }

        [Test]
        public void TheCatchUpOfALongPeriodIsTheWholePeriodOverTheScale()
        {
            Time.fixedDeltaTime = 0.02f;

            _gate.SetLockstep(2f, 100f);
            Assert.That(Time.maximumDeltaTime, Is.EqualTo(0.02f).Within(1e-6f), "2 s / 100");

            _gate.SetLockstep(2f, 10f);
            Assert.That(Time.maximumDeltaTime, Is.EqualTo(0.2f).Within(1e-6f), "2 s / 10");
        }

        [Test]
        public void TheCatchUpFollowsTheBudgetThePeriodHasLeft()
        {
            // The bug this pins: the ceiling named the whole period for every frame of it, so the frame that
            // reached the last step had already queued a whole frame's worth of steps past the budget - measured
            // live at ten times speed, a ten-step period spent in about fifteen steps. Sized to what is left, a
            // frame can only ever queue the steps that remain.
            Time.fixedDeltaTime = 0.02f;
            _gate.SetLockstep(2f, 1f);
            Assert.That(
                _gate.CatchUpCeiling,
                Is.EqualTo(2f).Within(1e-6f),
                "a hundred steps of budget, at one times speed");

            _gate.Release(1f);
            SpendSteps(90);

            Assert.That(_gate.StepsToSpend - _gate.StepsSpent, Is.EqualTo(10), "ten steps of the hundred are left");
            Assert.That(
                Time.maximumDeltaTime,
                Is.EqualTo(2f).Within(1e-6f),
                "the release re-bounds the ceiling to the whole period it just granted");
            RunTheFrame();
            Assert.That(
                Time.maximumDeltaTime,
                Is.EqualTo(0.2f).Within(1e-6f),
                "and the frame that follows re-bounds it to the ten steps that are left");
        }

        [Test]
        public void TheCatchUpNeverDropsBelowTheEnginesOwnFloor()
        {
            // The engine refuses a maximumDeltaTime below one physics step - written live, 0.004 on a 0.02 step
            // reads back 0.02 - so a budget smaller than a step still names a step here. The tail is landed by
            // the scale instead, see TheTailFrameRunsBelowTheRequestedScale.
            Time.fixedDeltaTime = 0.02f;
            _gate.SetLockstep(0.2f, 10f);
            _gate.Release(10f);
            SpendSteps(8);

            Assert.That(_gate.StepsToSpend - _gate.StepsSpent, Is.EqualTo(2));
            Assert.That(
                _gate.CatchUpCeiling,
                Is.EqualTo(Time.fixedDeltaTime).Within(1e-6f),
                "two steps at ten times speed is under the engine's floor, so the floor is what is reported");
        }

        [Test]
        public void ATailFrameCannotQueueMoreStepsThanRemain()
        {
            // Sizing a frame by the steps that are left is only worth anything if the frame it sizes cannot
            // queue more of them than that: the scale it computes has to keep the frame's physics inside the
            // budget, whatever the frame's wall time turns out to be.
            foreach (int left in new[] { 1, 2, 5, 9, 50 })
            {
                foreach (float frame in new[] { 0.004f, 0.0167f, 0.033f, 0.05f, 0.2f })
                {
                    float scale = Mathf.Min(10f, SimulationPacingGate.ScaleForStepsLeft(left, frame, 0.02f));
                    float queued = Mathf.Floor(frame * scale / 0.02f);

                    Assert.That(
                        queued,
                        Is.LessThanOrEqualTo(left),
                        $"{left} steps left, a {frame} s frame at scale {scale}, queues {queued}");
                }
            }
        }

        [Test]
        public void TheTailFrameRunsBelowTheRequestedScale()
        {
            Time.fixedDeltaTime = 0.05f;
            _gate.SetLockstep(0.5f, 10f);
            _gate.Release(10f);
            Assert.That(
                Time.timeScale,
                Is.EqualTo(10f).Within(1e-6f),
                "the release runs the whole period at the scale the client asked for");

            SpendSteps(7);
            RunTheFrame();

            // Three steps of the ten are left, so the frame may carry at most three of them: the scale has to
            // be at or below that, whatever wall time the editor happens to have drawn the last frame in.
            Assert.That(Time.timeScale, Is.LessThanOrEqualTo(3f + 1e-4f), "the tail is sized to the budget, not the period");
            Assert.That(Time.timeScale, Is.GreaterThan(0f), "the tail still runs: it is slowed, not stopped");
        }

        [Test]
        public void AReleaseClampsTheScaleTheConfigurationHandsIt()
        {
            Time.fixedDeltaTime = 0.05f;
            _gate.SetLockstep(0.2f, 4f);

            // The configuration still carries the scale the client asked for: the release is where it is
            // reduced, so a caller that changes the scale mid-episode cannot raise it back past the engine.
            _gate.Release(100f);

            Assert.That(_gate.EffectiveScale, Is.EqualTo(4f).Within(1e-6f));
            Assert.That(Time.timeScale, Is.EqualTo(4f).Within(1e-6f));
            Assert.That(_gate.IsReleased, Is.True);
        }

        [Test]
        public void FreeRunningGivesTheScaleAndTheCatchUpBack()
        {
            Time.fixedDeltaTime = 0.02f;
            Time.maximumDeltaTime = 10f;
            _gate.SetLockstep(0.2f, 10f);

            _gate.SetFree(10f);

            Assert.That(_gate.IsLockstep, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(10f).Within(1e-6f));
            Assert.That(
                Time.maximumDeltaTime,
                Is.EqualTo(10f).Within(1e-6f),
                "the ceiling the gate replaced while it held the clock goes back with it");
        }

        [Test]
        public void AClientThatKeepsReleasingKeepsTheClock()
        {
            Time.fixedDeltaTime = 0.02f;
            Time.maximumDeltaTime = 10f;

            _gate.SetLockstep(0.2f, 1f);
            _gate.Release(1f);

            Assert.That(
                _gate.ShouldGiveBackTheClock(_gate.LastReleaseWall + _gate.ReleaseLeaseSeconds * 0.5f),
                Is.False,
                "a client that is still answering keeps the clock it was lent");
        }

        [Test]
        public void AClientThatStopsReleasingGivesTheClockBack()
        {
            // The bug this pins: a lockstep session whose client never got to run its teardown - an
            // interrupted training run, a killed process - left the world at a zero time scale, and nothing
            // on the other side could take it back. The world stopped, the state stream published from the
            // loop that stopped went silent with it, and the scene read as frozen ever after.
            Time.fixedDeltaTime = 0.02f;
            Time.maximumDeltaTime = 10f;
            _gate.SetLockstep(0.2f, 1f);

            Assert.That(
                _gate.ShouldGiveBackTheClock(_gate.LastReleaseWall + _gate.ReleaseLeaseSeconds + 1f),
                Is.True,
                "a client that has been quiet past its lease has gone");

            _gate.GiveBackTheClock();

            Assert.That(_gate.IsLockstep, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(1e-6f), "the world runs again");
            Assert.That(Time.maximumDeltaTime, Is.EqualTo(10f).Within(1e-6f));
        }

        [Test]
        public void TheLeaseDoesNotFireOnASessionThatWasNeverLent()
        {
            _gate.SetFree(1f);

            Assert.That(_gate.ShouldGiveBackTheClock(float.MaxValue), Is.False);
            _gate.GiveBackTheClock();
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(1e-6f));
        }
    }
}
