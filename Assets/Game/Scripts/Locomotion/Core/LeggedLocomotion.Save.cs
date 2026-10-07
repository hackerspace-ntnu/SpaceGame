// Putting a walking machine back exactly as it was standing.
//
// This is the smallest of the five files and the only one that is not part of a frame. Nothing here
// is called by the locomotion itself: a saver in Assembly-CSharp reads the snapshot out and hands it
// back on the next load, and between those two moments this file does nothing at all.
//
// ─────────── what is actually being fixed ───────────
//
// The machine's pose is already restored — the body's transform is somebody else's job and is
// handled. What is NOT restored is everything the legs know, and a legged machine is mostly what its
// legs know. Reload one and every foot snaps to the default stance under its hip, the gait clock
// restarts at zero, and `heightPrimed` comes back false so the body's first frame settles onto the
// ground from wherever the smoothing happens to start. Six legs' worth of that at once is the pop
// every legged creature in the world does on load.
//
// It is COSMETIC, and it is worth being honest that it is: nothing here changes where the machine
// is, what it can reach, or whether it can walk. It is one visible stumble per creature per load.
//
// ─────────── the rule this file must not break ───────────
//
// Nothing at stride frequency may be filtered, and nothing here filters anything: this is assignment
// only, and only ever at a moment the machine is not stepping. In particular the gait phase is
// restored verbatim rather than being re-derived or blended toward — the clock is advanced by
// distance travelled, and a phase that is anything other than exactly what it was is a foot in the
// wrong slice.
//
// Strictly additive: no behaviour above this line changed, and a machine whose record is absent
// takes precisely the path it took before this file existed.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    public abstract partial class LeggedLocomotion
    {
        /// <summary>
        /// One leg's contribution to the snapshot. A plain value type in this assembly, so the saver
        /// can hold an array of them without this assembly knowing anything about serialization.
        /// </summary>
        public struct LegSnapshot
        {
            public Vector3 Foot;
            public Vector3 GroundNormal;
            public Vector3 SwingFrom;
            public Vector3 SwingTo;
            public float PhaseOffset;
            public bool Swinging;
            public float SwingT;
            public float SwingLift;
            public float SwingSpan;
            public bool WasInSlice;
            public bool Grounded;
            public float StanceTime;
            public float Load;
        }

        /// <summary>Everything about how this machine was standing that a frame does not re-derive.</summary>
        public struct Snapshot
        {
            public float Phase;
            public Vector3 PathPos;
            public float Yaw;
            public float SmoothedHeight;
            public bool HeightPrimed;
            public float FallVelocity;
            public bool Falling;
            public LegSnapshot[] Legs;
        }

        /// <summary>
        /// True once a restore has spoken for this machine's stance. Read by <c>Start</c>, which
        /// otherwise drops the machine onto the ground and grounds every foot underneath it —
        /// undoing the restore for the one case where the two run in that order.
        /// </summary>
        private bool locomotionRestored;

        /// <summary>
        /// The machine's stance, or null if the rig never came up. Null means "nothing worth
        /// storing", which is the correct record for a machine that found no legs.
        /// </summary>
        public Snapshot? CaptureLocomotion()
        {
            if (!ready) return null;

            var snapshot = new Snapshot
            {
                Phase = gait.Phase,
                PathPos = pathPos,
                Yaw = currentYaw,
                SmoothedHeight = smoothedHeight,
                HeightPrimed = heightPrimed,
                FallVelocity = fallVelocity,
                Falling = IsFalling,
                Legs = new LegSnapshot[legs.Count],
            };

            for (int i = 0; i < legs.Count; i++)
            {
                LegState leg = legs[i];
                snapshot.Legs[i] = new LegSnapshot
                {
                    Foot = leg.Foot,
                    GroundNormal = leg.GroundNormal,
                    SwingFrom = leg.SwingFrom,
                    SwingTo = leg.SwingTo,
                    PhaseOffset = leg.PhaseOffset,
                    Swinging = leg.Swinging,
                    SwingT = leg.SwingT,
                    SwingLift = leg.SwingLift,
                    SwingSpan = leg.SwingSpan,
                    WasInSlice = leg.WasInSlice,
                    Grounded = leg.Grounded,
                    StanceTime = leg.StanceTime,
                    Load = leg.Load,
                };
            }

            return snapshot;
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// <para>
        /// Positional over the legs, and a mismatched count is tolerated rather than refused: a rig
        /// re-exported with a different number of limbs since the save is a real thing that happens
        /// here, and the legs that still line up are worth putting back even when the last one has
        /// nothing to be put back from.
        /// </para>
        /// <para>
        /// The footholds are WORLD positions, restored alongside a body pose that was captured in
        /// the same frame, so the two agree. That is also why this must not run before the pose is
        /// in place — it does not, because the store places an object before asking any of its
        /// components anything.
        /// </para>
        /// </summary>
        public void RestoreLocomotion(in Snapshot snapshot)
        {
            locomotionRestored = true;
            if (!ready) return;

            gait.Phase = Mathf.Repeat(snapshot.Phase, 1f);

            // The path is the machine's position with no bob or lean folded into it, which is
            // exactly what was captured — so this is a straight assignment and not a second, quieter
            // owner of the transform. `lastBodyPos` comes with it so the velocity tracker's first
            // frame does not read the whole load as one frame of travel.
            pathPos = snapshot.PathPos;
            currentYaw = snapshot.Yaw;
            smoothedHeight = snapshot.SmoothedHeight;
            heightPrimed = snapshot.HeightPrimed;
            fallVelocity = snapshot.FallVelocity;
            IsFalling = snapshot.Falling;
            lastBodyPos = body != null ? body.position : snapshot.PathPos;

            LegSnapshot[] restored = snapshot.Legs;
            if (restored == null) return;

            int count = Mathf.Min(restored.Length, legs.Count);
            for (int i = 0; i < count; i++)
            {
                LegState leg = legs[i];
                LegSnapshot s = restored[i];

                leg.Foot = s.Foot;
                leg.GroundNormal = s.GroundNormal.sqrMagnitude > 1e-6f ? s.GroundNormal : Vector3.up;
                leg.SwingFrom = s.SwingFrom;
                leg.SwingTo = s.SwingTo;
                leg.PhaseOffset = s.PhaseOffset;
                leg.Swinging = s.Swinging;
                leg.SwingT = Mathf.Clamp01(s.SwingT);
                leg.SwingLift = s.SwingLift;
                // Floored, never zero: the swing timer divides by this, and a record from before it
                // was written — or one truncated to zero — would advance a swing by infinity and
                // teleport the foot to its touchdown on the first frame.
                leg.SwingSpan = Mathf.Max(s.SwingSpan, 1e-3f);
                leg.WasInSlice = s.WasInSlice;
                leg.Grounded = s.Grounded;
                leg.StanceTime = s.StanceTime;
                leg.Load = Mathf.Clamp01(s.Load);

                // Not restored, deliberately: `Unreachable` is a verdict the solver reaches about
                // THIS frame's geometry, and it is re-answered by the first SolveLegs. Carrying a
                // stale one in would let the step-early rule fire against ground the machine is no
                // longer standing on.
                leg.Unreachable = false;
            }
        }
    }
}
