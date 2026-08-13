// Measured, immutable geometry of one limb. Captured once from the rest pose; angles are radians,
// lengths are world units, and each axle is stored in the space its own joint's
// `localRotation = rest * AngleAxis(theta, axle)` needs.
//
// A limb is a yaw joint carrying a chain of N parallel PITCH joints, optionally finished by a roll
// hinge at the tip. It was three pitch joints named Hip/Knee/Ankle until this became an array: a
// stubby two-joint leg, a five-joint insect leg and an arm are all the same shape with a different
// N, and hard-coding three was the only thing stopping them sharing this code.
//
// The named accessors at the bottom -- UpperLength, RestKneeAngle and friends -- are the classic
// four-joint leg's vocabulary kept alive over the array. They cost nothing and they are why the
// solver's tests read exactly as they did before the generalisation.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    /// One pitch joint and the segment hanging off it.
    public struct WalkerLimbSegment
    {
        /// Hinge axis in the joint's own space.
        public Vector3 AxleLocal;
        /// Rest localRotation, which every solved angle is expressed as a delta from.
        public Quaternion RestLocal;
        /// Absolute plane angle at rest, measured as atan2(up, fwd).
        public float RestAngle;
        /// To the next joint down the chain. The last segment measures to the CONTACT POINT, which
        /// is what meets the ground rather than wherever the tip bone happens to sit.
        public float Length;
    }

    public struct WalkerLimbGeometry
    {
        // ─────────── the base yaw joint ───────────
        // Optional. Without it the limb is trapped in one vertical plane and a planted foot cannot
        // stay planted while the body travels across that plane -- see invariant I5. A rig may
        // legitimately be built that way, so this degrades rather than refuses.
        public bool HasYaw;
        /// Vertical hinge, in body space. Signed so it points up.
        public Vector3 YawAxisBody;
        public Vector3 YawAxisLocalRoot;
        public Quaternion RestRootLocal;

        /// In-plane horizontal axis at zero yaw, in body space. Signed to point outboard, toward
        /// the contact point, so a yaw angle measured against it reads the way you would expect.
        public Vector3 RestFwdBody;

        // ─────────── the pitch chain ───────────
        // All of them denote one world line at rest and stay parallel to each other at any yaw,
        // because yawing the root turns them together. Angles about parallel axles add, so a child
        // joint only supplies the remainder of its parent's rotation.
        public WalkerLimbSegment[] Pitch;

        // ─────────── the tip roll joint ───────────
        // The sole's own hinge, running fore-aft along the limb. Every other joint pitches about a
        // lateral axle, so without this the sole can meet a slope it is walking up but not one it
        // is walking across. Its pivot sits ON the contact point, so rolling tilts the sole without
        // dragging the point the IK just placed.
        public bool HasRoll;
        public Vector3 RollAxisLocalTip;
        public Quaternion RestTipLocal;

        /// Sign of the 2D cross product of the first two pitch segments at rest. Pins the elbow
        /// solution of the TWO-LINK analytic path so the knee can never pop through to the mirrored
        /// pose. The CCD path is seeded from the rest pose instead, so its bend directions are
        /// implicit in the seed and it does not read this.
        public float BendSign;

        /// Contact point in the last pitch joint's local space, at rest. This is what meets the
        /// ground.
        public Vector3 ContactLocalTip;

        /// Horizontal distance from the yaw axis to the rest contact point. The radius of the arc
        /// the tip sweeps when the root turns, so it is what sets a splayed-leg machine's stride.
        public float RestFootRadius;

        // ─────────── derived ───────────

        public int PitchCount => Pitch != null ? Pitch.Length : 0;

        /// Links the IK is free to choose. The last segment's DIRECTION is prescribed by the
        /// surface normal, so it is not one of them.
        public int FreeLinks => PitchCount > 0 ? PitchCount - 1 : 0;

        public float TotalLength
        {
            get
            {
                float sum = 0f;
                for (int i = 0; i < PitchCount; i++) sum += Pitch[i].Length;
                return sum;
            }
        }

        /// Furthest the contact point can get from the first pitch joint, with a margin so the
        /// solver is never handed a target exactly at the singularity.
        public float MaxReach => TotalLength * 0.97f;

        // ─────────── the classic four-joint leg's names ───────────
        // Hip is pitch 0, Knee pitch 1, Ankle the last one. Meaningless on a limb that is not that
        // shape, which is why the code that has to work for any N uses the array directly.

        public float UpperLength => LengthAt(0);
        public float LowerLength => LengthAt(1);
        public float FootLength => LengthAt(PitchCount - 1);

        public float RestHipAngle => AngleAt(0);
        public float RestKneeAngle => AngleAt(1);
        public float RestAnkleAngle => AngleAt(PitchCount - 1);

        private float LengthAt(int i)
            => Pitch != null && i >= 0 && i < Pitch.Length ? Pitch[i].Length : 0f;

        private float AngleAt(int i)
            => Pitch != null && i >= 0 && i < Pitch.Length ? Pitch[i].RestAngle : 0f;
    }
}
