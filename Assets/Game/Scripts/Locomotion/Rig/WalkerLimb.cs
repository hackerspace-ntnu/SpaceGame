// One limb's bones, as found on the rig.
//
// A limb is deliberately NOT a leg. It is a chain plus a measurement, with no gait state, no
// foothold and no load share -- those live in LegState, which owns a limb. That seam is the whole
// cost of being able to drive an arm off the same IK later: an arm is a limb whose target comes
// from something other than a gait.
using UnityEngine;

namespace SpaceGame.Locomotion
{
    public static partial class WalkerRig
    {
        /// What a limb is FOR, taken from the prefix its root bone carries. Discovery and
        /// measurement are identical either way -- a limb is a limb -- but the locomotion walks on
        /// one and not the other, so the rig has to say which is which. Before this existed an arm
        /// named `Arm_L` was not found at all and an arm named `Limb_L` was found and then walked on.
        public enum LimbRole
        {
            /// Carries the machine. Gets a LegState, a gait slot and a foothold.
            Leg,
            /// Everything else that hangs off the body and is posed by IK. Gets a WalkerArm: a
            /// world target and a tip direction, and nothing to do with the gait.
            Arm,
        }

        public class Limb
        {
            public string Id;

            /// Leg unless the root bone said otherwise. Defaulting to Leg is deliberate: every rig
            /// that existed before roles did carries `Limb_`/`Coxa_`/`Hip_` roots and must keep
            /// meaning exactly what it meant.
            public LimbRole Role;

            /// The yaw joint at the base of the chain. Null on a planar limb, which has none.
            public Transform Root;

            /// The pitch chain, root-most first. Always at least one joint.
            public Transform[] Pitch;

            /// The tip's roll joint -- the sole. Optional: a rig without one still walks, with a
            /// sole that can only pitch, so this degrades rather than fails.
            public Transform Tip;

            /// Base DOFs found between the root and the pitch chain -- an insect's roll trochanter,
            /// say. Measured so the classification is right, then held at their rest pose: the
            /// solver drives one yaw joint and one roll joint and nothing else.
            public Transform[] HeldBase;

            public WalkerLimbGeometry Geometry;

            /// First pitch joint. The limb's plane hangs off this, and it sits ON the yaw axis,
            /// which is what lets the two solve stages be independent.
            public Transform Anchor => Pitch[0];

            /// Last pitch joint. Carries the contact point.
            public Transform TipJoint => Pitch[Pitch.Length - 1];

            /// Where the limb currently touches the world.
            public Vector3 ContactPoint => TipJoint.TransformPoint(Geometry.ContactLocalTip);

            // ─────────── the classic four-joint leg's names ───────────
            public Transform Coxa => Root;
            public Transform Hip => Pitch[0];
            public Transform Knee => Pitch.Length > 1 ? Pitch[1] : null;
            public Transform Ankle => Pitch[Pitch.Length - 1];
            public Transform Foot => Tip;
        }
    }
}
