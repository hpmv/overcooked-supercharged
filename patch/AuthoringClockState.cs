using System;

namespace SuperchargedPatch
{
    // Pure state machine, not yet a Unity/plugin patch. All frame arguments are
    // monotonic native render-frame identities, never controller/warp frame tags.
    // A caller must ObserveFrame once per native frame, and before native clock
    // updates. Calls from physics must use this same frame identity and step.
    public sealed class AuthoringClockState
    {
        public sealed class Checkpoint
        {
            internal readonly AuthoringClockState Owner;
            public readonly double Anchor;
            public readonly long EligibleTicks;
            public readonly float Step;
            public readonly float Value;

            internal Checkpoint(AuthoringClockState owner, double anchor, long ticks, float step, float value)
            {
                Owner = owner;
                Anchor = anchor;
                EligibleTicks = ticks;
                Step = step;
                Value = value;
            }
        }

        private double anchor;
        private long eligibleTicks;
        private readonly float step;
        private long frame;
        private bool nextFrameAuthoringPaused;
        private bool currentFrameAuthoringPaused;
        private float value;
        private long restores;

        // Seed with the source value already observed in this frame. Construction
        // does not invent a tick. Use a fresh instance for a fresh clock/session.
        public AuthoringClockState(long observedFrame, float observedValue, float captureStep, bool authoringPaused)
        {
            if (observedFrame < 0) throw new ArgumentOutOfRangeException("observedFrame");
            if (!Finite(observedValue)) throw new ArgumentException("Finite source required.", "observedValue");
            if (!Finite(captureStep) || captureStep <= 0) throw new ArgumentException("Positive finite capture step required.", "captureStep");
            frame = observedFrame;
            anchor = observedValue;
            step = captureStep;
            value = observedValue;
            currentFrameAuthoringPaused = authoringPaused;
            nextFrameAuthoringPaused = authoringPaused;
        }

        public long Frame { get { return frame; } }
        public long EligibleTicks { get { return eligibleTicks; } }
        public long RestoreCount { get { return restores; } }
        public bool AuthoringPausedThisFrame { get { return currentFrameAuthoringPaused; } }
        public bool AuthoringPauseRequested { get { return nextFrameAuthoringPaused; } }
        public bool ShouldRunNativeClockUpdates { get { return !currentFrameAuthoringPaused; } }
        public float Value { get { return value; } }
        public float CaptureStep { get { return step; } }

        public float ObserveFrame(long observedFrame)
        {
            if (observedFrame == frame) return value;
            // Missing observations are unknown pause history, not permission to
            // synthesize elapsed time. Integration must drive this every frame.
            if (frame == long.MaxValue || observedFrame != frame + 1)
                throw new InvalidOperationException("Clock frames must be contiguous and monotonic.");
            bool paused = nextFrameAuthoringPaused;
            long nextTicks = paused ? eligibleTicks : checked(eligibleTicks + 1);
            float nextValue = Calculate(anchor, nextTicks, step);
            frame = observedFrame;
            eligibleTicks = nextTicks;
            currentFrameAuthoringPaused = paused;
            value = nextValue;
            return value;
        }

        // Latch using the previous ownership before changing it. Therefore a
        // late pause/resume before the first getter still cannot relabel the
        // elapsed frame. All getters in that frame observe the same decision.
        public void SetAuthoringPaused(long observedFrame, bool paused)
        {
            ObserveFrame(observedFrame);
            nextFrameAuthoringPaused = paused;
        }

        public Checkpoint CaptureCheckpoint()
        {
            return new Checkpoint(this, anchor, eligibleTicks, step, value);
        }

        // Authoring restore changes the current cached source exactly once, then
        // holds it for the remainder of this already-paused frame. Unity time,
        // native clock fields, callbacks and physics are outside this helper.
        public void RestoreCheckpoint(Checkpoint checkpoint, long observedFrame)
        {
            if (checkpoint == null) throw new ArgumentNullException("checkpoint");
            if (!ReferenceEquals(checkpoint.Owner, this))
                throw new InvalidOperationException("Clock checkpoint belongs to another session.");
            if (observedFrame != frame || !currentFrameAuthoringPaused)
                throw new InvalidOperationException("Restore requires the current observed authoring-paused frame.");
            // Check everything before mutation. This also guards future changes
            // to a checkpoint deserializer; today's checkpoint is immutable.
            if (!Finite(checkpoint.Anchor) || checkpoint.EligibleTicks < 0 || checkpoint.Step != step ||
                !SameBits(Calculate(checkpoint.Anchor, checkpoint.EligibleTicks, checkpoint.Step), checkpoint.Value))
                throw new InvalidOperationException("Clock checkpoint has inconsistent exact state.");
            long nextRestores = checked(restores + 1);
            anchor = checkpoint.Anchor;
            eligibleTicks = checkpoint.EligibleTicks;
            value = checkpoint.Value;
            nextFrameAuthoringPaused = true;
            restores = nextRestores;
        }

        private static float Calculate(double baseValue, long ticks, float captureStep)
        {
            if (ticks == 0) return (float)baseValue;
            // Multiply in double and round once at the public native float.
            // Never re-anchor at a saved float or current Unity Time.time.
            double elapsed = ticks * (double)captureStep;
            double exact = baseValue + elapsed;
            float result = (float)exact;
            if (!Finite(exact) || !Finite(result)) throw new InvalidOperationException("Clock exceeded its finite range.");
            return result;
        }

        private static bool Finite(float x) { return !float.IsNaN(x) && !float.IsInfinity(x); }
        private static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
        private static bool SameBits(float x, float y)
        {
            return BitConverter.ToInt32(BitConverter.GetBytes(x), 0) == BitConverter.ToInt32(BitConverter.GetBytes(y), 0);
        }
    }
}
