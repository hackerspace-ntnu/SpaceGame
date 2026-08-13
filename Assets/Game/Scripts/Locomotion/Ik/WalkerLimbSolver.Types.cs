// What the limb solver is handed, and what it hands back.
//
// Split out from the solve itself so that file stays about the arithmetic. The types are still
// nested in WalkerLimbSolver, so every call site reads exactly as it did before.
//
// Limits and Result both carry a per-joint array now that a limb is N joints rather than three.
// The classic leg's names -- Hip, Knee, Ankle -- are kept as accessors over the array: they are how
// every existing call site and test is written, and a four-joint leg is still the common case.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    public static partial class WalkerLimbSolver
    {
        /// Where the limb is attached this frame, in world space. Rebuilt each frame from the
        /// body's transform; the attachment is rigid relative to the body, so this is a basis
        /// change and nothing more.
        public struct Frame
        {
            /// Hinge centre of the first pitch joint. Lies on the yaw axis, so it is invariant
            /// under yaw -- which is the property that lets the two solve stages be independent.
            public Vector3 Hip;
            /// Yaw hinge, pointing up.
            public Vector3 YawAxis;
            /// In-plane horizontal axis at zero yaw, pointing outboard.
            public Vector3 RestFwd;
        }

        /// Symmetric travel about the rest pose, in degrees. This is the whole of what stops the
        /// machine folding itself into poses no linkage could hold.
        public struct Limits
        {
            public float Yaw;
            public float Roll;

            /// One entry per pitch joint. A limb with more joints than entries reuses the last,
            /// so a uniform limit needs one number rather than N.
            public float[] Pitch;

            /// The classic leg's three pitch joints, over the array.
            public float Hip { get => PitchAt(0); set => SetPitch(0, value); }
            public float Knee { get => PitchAt(1); set => SetPitch(1, value); }
            public float Ankle { get => PitchAt(2); set => SetPitch(2, value); }

            /// Travel allowed at pitch joint `i`. Out-of-range indices reuse the last entry, which
            /// is what makes a uniform limb need one number.
            public float PitchAt(int i)
            {
                if (Pitch == null || Pitch.Length == 0) return 0f;
                return Pitch[Mathf.Clamp(i, 0, Pitch.Length - 1)];
            }

            private void SetPitch(int i, float value)
            {
                if (Pitch == null || Pitch.Length <= i)
                {
                    var grown = new float[i + 1];
                    if (Pitch != null) Pitch.CopyTo(grown, 0);
                    Pitch = grown;
                }
                Pitch[i] = value;
            }

            /// The same travel at every pitch joint. For limbs that have no reason to differ --
            /// which is most of them; the ostrich's hip 55 / knee 90 / ankle 60 is the exception.
            public static Limits Uniform(float yaw, float pitch, float roll, int count)
            {
                var limits = new Limits { Yaw = yaw, Roll = roll, Pitch = new float[Mathf.Max(1, count)] };
                for (int i = 0; i < limits.Pitch.Length; i++) limits.Pitch[i] = pitch;
                return limits;
            }

            public static Limits Default =>
                new Limits { Yaw = 40f, Hip = 45f, Knee = 60f, Ankle = 45f, Roll = 30f };
        }

        public struct Result
        {
            /// Degrees about the yaw axis, measured from the rest plane.
            public float Yaw;

            /// Absolute plane angles, radians, measured as atan2(up, fwd) of each segment. One per
            /// pitch joint, root-most first.
            public float[] Pitch;

            /// Degrees about the sole's fore-aft hinge. Answers the part of the ground normal
            /// that lies across the limb's plane, which no pitch joint can reach.
            public float Roll;

            /// The linkage could not honour the target: out of reach, or a joint limit bound.
            ///
            /// NOT the same question as "is the foot unreachable". On a bird some joint is on a
            /// limit most frames; a gait that reads this as unreachable steps early on two thirds
            /// of them and syncs its legs into a hop. Use ReachFraction > 1 for that.
            public bool Clamped;

            /// Distance from the first pitch joint to the target, as a fraction of the linkage's
            /// maximum.
            public float ReachFraction;

            public int PitchCount => Pitch != null ? Pitch.Length : 0;

            // ─────────── the classic four-joint leg's names ───────────
            public float HipAngle => AngleAt(0);
            public float KneeAngle => AngleAt(1);
            public float AnkleAngle => AngleAt(PitchCount - 1);

            private float AngleAt(int i)
                => Pitch != null && i >= 0 && i < Pitch.Length ? Pitch[i] : 0f;
        }
    }
}
