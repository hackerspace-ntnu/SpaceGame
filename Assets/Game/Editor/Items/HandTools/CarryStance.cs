using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// How a carried tool is held, as one word. A roster row names a stance and
    /// <see cref="GripFitter"/> turns it into the numbers <see cref="ItemGrip"/> stores, measured
    /// against the pose the holder really takes — so nobody types a rotation and a new tool needs no
    /// tuning to point the right way.
    /// </summary>
    public enum CarryStance
    {
        /// <summary>A short tool worked from the grip: head up and forward, as if about to strike. One hand.</summary>
        Wield,

        /// <summary>A long haft stood upright at the holder's side, head at the top. One hand.</summary>
        Staff,

        /// <summary>A vessel hung from its handle, rim up, so it hangs below the fist. One hand.</summary>
        Hang,

        /// <summary>An item pointed away from the body: a gun, a spyglass, a shield's face. Hands per the pose.</summary>
        Aim,

        /// <summary>A cart pushed ahead of the holder by its handle, wheels on the ground. Both hands.</summary>
        Push,
    }

    /// <summary>What a stance means: the pose the body takes and where the tool's two reference axes point.</summary>
    public readonly struct StanceDefinition
    {
        /// <summary>The Upper Body pose played while the tool is held.</summary>
        public readonly ItemGrip.HoldStyle Pose;

        /// <summary>Item axis that runs along the tool, in item space. Unity +Y is the business end.</summary>
        public readonly Vector3 ItemAlong;

        /// <summary>Item axis that is the tool's working face, in item space, at right angles to <see cref="ItemAlong"/>.</summary>
        public readonly Vector3 ItemFace;

        /// <summary>Where <see cref="ItemAlong"/> points, in the holder's space (+Y up, +Z forward).</summary>
        public readonly Vector3 BodyAlong;

        /// <summary>Where <see cref="ItemFace"/> points, in the holder's space, at right angles to <see cref="BodyAlong"/>.</summary>
        public readonly Vector3 BodyFace;

        public StanceDefinition(ItemGrip.HoldStyle pose, Vector3 itemAlong, Vector3 itemFace,
                                Vector3 bodyAlong, Vector3 bodyFace)
        {
            Pose = pose;
            ItemAlong = itemAlong;
            ItemFace = itemFace;
            BodyAlong = bodyAlong;
            BodyFace = bodyFace;
        }
    }

    /// <summary>The one table that says what each <see cref="CarryStance"/> means.</summary>
    public static class CarryStances
    {
        /// <summary>Degrees above the horizon a wielded head is raised.</summary>
        private const float WieldPitch = 50f;

        /// <summary>Degrees a staff leans forward of vertical.</summary>
        private const float StaffLean = 10f;

        public static StanceDefinition Of(CarryStance stance)
        {
            switch (stance)
            {
                case CarryStance.Wield:
                    return Upright(ItemGrip.HoldStyle.Ready, WieldPitch);
                case CarryStance.Staff:
                    return Upright(ItemGrip.HoldStyle.Carry, 90f - StaffLean);
                case CarryStance.Hang:
                    return Upright(ItemGrip.HoldStyle.Carry, 90f);
                case CarryStance.Aim:
                    return new StanceDefinition(ItemGrip.HoldStyle.OneHanded,
                        Vector3.forward, Vector3.up, Vector3.forward, Vector3.up);
                case CarryStance.Push:
                    // The cart's body trails behind its handle along item -Z; pushing sends that
                    // way out in front of the holder.
                    return new StanceDefinition(ItemGrip.HoldStyle.Push,
                        Vector3.back, Vector3.up, Vector3.forward, Vector3.up);
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(stance), stance, null);
            }
        }

        /// <summary>Item +Y raised <paramref name="pitch"/> degrees off the horizon toward the front, +Z facing down-forward of it.</summary>
        private static StanceDefinition Upright(ItemGrip.HoldStyle pose, float pitch)
        {
            Quaternion lift = Quaternion.Euler(-pitch, 0f, 0f);
            return new StanceDefinition(pose, Vector3.up, Vector3.forward, lift * Vector3.forward, lift * Vector3.down);
        }
    }
}
