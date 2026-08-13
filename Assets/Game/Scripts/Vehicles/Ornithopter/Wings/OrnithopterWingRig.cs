using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Vehicles.Ornithopter
{
    /// <summary>
    /// Bone lookup for <c>Arm_DuneOrnithopter</c>, and nothing else. Mirrors what
    /// <c>WalkerRig</c> does for the legged machines: find the joints once, remember the pose the
    /// artist authored, and hand out transforms.
    ///
    /// Every rotation the animator applies is a DELTA on the cached rest pose, never an absolute.
    /// That is what lets the model be re-exported with a different rest attitude — a bit more
    /// dihedral, a digit re-aimed — without a single number in the animator changing.
    ///
    /// Resolution is by exact name and it throws naming the bone it could not find. A rig this
    /// size fails in a way that is almost impossible to diagnose from the symptom: half the wing
    /// simply does not move, and there is nothing in the console to say why.
    /// </summary>
    public class OrnithopterWingRig
    {
        public const int DigitCount = 5;

        /// <summary>One wing's chain: the flap joint, the drivetrain that visibly turns with it,
        /// the sweep joint, and the five spars the membrane is stretched between.</summary>
        public class Wing
        {
            public Transform Shoulder;                  // FLAP — local X, no per-side sign
            public Transform Gear;                      // spins with the beat — local Y
            public Transform Crank;                     // throw follows the gear
            public Transform Arm;                       // SWEEP — local Z, per-side sign
            public readonly Transform[] Digits = new Transform[DigitCount];  // SPLAY + TWIST

            public Quaternion ShoulderRest, GearRest, CrankRest, ArmRest;
            public readonly Quaternion[] DigitRest = new Quaternion[DigitCount];

            /// <summary>
            /// +1 for starboard, -1 for port. Applied to rotations about local Z, and to the roll
            /// differential — the two places a per-side sign is genuinely needed.
            ///
            /// The reason, from the model's build record: the two wing bones point outboard in
            /// opposite directions, so their local X and Y axes are already mirrored and the SAME
            /// angle on both sides produces a symmetric result. Local Z points up on both sides
            /// and is NOT mirrored. Sign a Z rotation wrong and one wing opens while the other
            /// closes — which is exactly the bug this field exists to prevent.
            /// </summary>
            public float Sign;
        }

        public Transform Root { get; private set; }
        public Transform Body { get; private set; }
        public Transform Cradle { get; private set; }
        public Transform Boom1 { get; private set; }
        public Transform Boom2 { get; private set; }
        public Transform TailHub { get; private set; }
        public Transform[] TailDigits { get; } = new Transform[DigitCount];

        public Quaternion Boom1Rest { get; private set; }
        public Quaternion Boom2Rest { get; private set; }
        public Quaternion[] TailDigitRest { get; } = new Quaternion[DigitCount];

        public Wing Left { get; private set; }
        public Wing Right { get; private set; }

        public bool IsBuilt { get; private set; }

        /// <summary>
        /// Walk the hierarchy under <paramref name="root"/> and bind every bone. Throws on the
        /// first one missing.
        /// </summary>
        public void Build(Transform root)
        {
            var index = new Dictionary<string, Transform>(64);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                index[t.name] = t;

            Transform Find(string name)
            {
                if (index.TryGetValue(name, out Transform t) && t != null)
                    return t;
                throw new MissingReferenceException(
                    $"OrnithopterWingRig: no bone named '{name}' under '{root.name}'. The FBX must " +
                    "be exported from dune_ornithopter.blend with its armature — see " +
                    "Assets/Art/Models/_Source~/models/vehicles/dune_ornithopter_export.py.");
            }

            Root = Find("Bone_Root");
            Body = Find("Bone_Body");
            Cradle = Find("Bone_Cradle");

            Boom1 = Find("Bone_Boom_1");
            Boom2 = Find("Bone_Boom_2");
            TailHub = Find("Bone_TailHub");
            Boom1Rest = Boom1.localRotation;
            Boom2Rest = Boom2.localRotation;

            for (int i = 0; i < DigitCount; i++)
            {
                TailDigits[i] = Find($"Bone_TailDigit_{i + 1}");
                TailDigitRest[i] = TailDigits[i].localRotation;
            }

            Right = BuildWing(Find, "R", +1f);
            Left = BuildWing(Find, "L", -1f);

            IsBuilt = true;
        }

        private static Wing BuildWing(System.Func<string, Transform> find, string tag, float sign)
        {
            var wing = new Wing { Sign = sign };

            wing.Shoulder = find($"Bone_Shoulder_{tag}");
            wing.Gear = find($"Bone_Gear_{tag}");
            wing.Crank = find($"Bone_Crank_{tag}");
            wing.Arm = find($"Bone_Arm_{tag}");

            wing.ShoulderRest = wing.Shoulder.localRotation;
            wing.GearRest = wing.Gear.localRotation;
            wing.CrankRest = wing.Crank.localRotation;
            wing.ArmRest = wing.Arm.localRotation;

            for (int i = 0; i < DigitCount; i++)
            {
                wing.Digits[i] = find($"Bone_Digit_{tag}_{i + 1}");
                wing.DigitRest[i] = wing.Digits[i].localRotation;
            }

            return wing;
        }

        /// <summary>Restore every bone to the pose the model was authored in.</summary>
        public void ResetToRest()
        {
            if (!IsBuilt) return;

            Boom1.localRotation = Boom1Rest;
            Boom2.localRotation = Boom2Rest;
            for (int i = 0; i < DigitCount; i++)
                TailDigits[i].localRotation = TailDigitRest[i];

            ResetWing(Left);
            ResetWing(Right);
        }

        private static void ResetWing(Wing w)
        {
            w.Shoulder.localRotation = w.ShoulderRest;
            w.Gear.localRotation = w.GearRest;
            w.Crank.localRotation = w.CrankRest;
            w.Arm.localRotation = w.ArmRest;
            for (int i = 0; i < DigitCount; i++)
                w.Digits[i].localRotation = w.DigitRest[i];
        }
    }
}
