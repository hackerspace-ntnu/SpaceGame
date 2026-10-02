using UnityEngine;
using SpaceGame.Items;

namespace SpaceGame.EditorTools
{
    /// <summary>What <see cref="ItemGrip"/> stores for one tool: the pose, and the offsets in the hand's frame.</summary>
    public readonly struct GripFit
    {
        public readonly ItemGrip.HoldStyle Pose;
        public readonly Vector3 Rotation;
        public readonly Vector3 Position;

        public GripFit(ItemGrip.HoldStyle pose, Vector3 rotation, Vector3 position)
        {
            Pose = pose;
            Rotation = rotation;
            Position = position;
        }
    }

    /// <summary>
    /// Solves a tool's <see cref="ItemGrip"/> offsets from its <see cref="CarryStance"/>.
    ///
    /// <para>
    /// <see cref="EquipItemSocket"/> seats an item at <c>handFrame * Euler(rotationOffset)</c>, and
    /// the hand frame turns with the arm, so the same offset points a tool differently in every
    /// pose. This holds the real pose on a real Raxy, reads the hand frame, and returns the offset
    /// that makes the tool's two reference axes point where the stance says. Offsets are therefore
    /// derived, never typed; change a pose or a rig and re-run <b>Build All</b>.
    /// </para>
    /// </summary>
    public static class GripFitter
    {
        public static GripFit Fit(HandToolRig rig, HandToolSpec spec)
        {
            StanceDefinition stance = CarryStances.Of(spec.Stance);
            ItemGrip.HoldStyle pose = spec.PoseOverride ?? stance.Pose;

            if (!rig.Pose(pose))
                throw new System.InvalidOperationException(
                    $"{spec.Id}: the humanoid animation profile has no pose for {pose}.");

            Vector3 along = spec.ItemAlong ?? stance.ItemAlong;
            Vector3 face = spec.ItemFace ?? stance.ItemFace;

            // The rotation that takes the tool's own axes onto the stance's axes, in the holder's space.
            Quaternion itemBasis = Quaternion.LookRotation(face, along);
            Quaternion bodyBasis = Quaternion.LookRotation(stance.BodyFace, stance.BodyAlong);
            Quaternion toolInBody = bodyBasis * Quaternion.Inverse(itemBasis);

            Quaternion handInBody = rig.GripFrameInBody();
            Quaternion offset = Quaternion.Inverse(handInBody) * toolInBody;

            // The nudge is authored in the holder's space (metres, +Y up) and stored in the hand's.
            Vector3 position = Quaternion.Inverse(handInBody) * spec.Nudge;

            return new GripFit(pose, Round(offset.eulerAngles, 10f), Round(position, 1000f));
        }

        /// <summary>
        /// How far, in degrees, the tool's along axis is from where its stance wants it once the REAL
        /// prefab is seated by <see cref="EquipItemSocket"/> in the pose it was built for. Reads the
        /// result back through the code path the game uses, so a fit that the seating does not honour
        /// shows up here and not in front of a player.
        /// </summary>
        public static float Residual(HandToolRig rig, HandToolSpec spec, GameObject prefab)
        {
            StanceDefinition stance = CarryStances.Of(spec.Stance);
            rig.Pose(prefab.GetComponent<ItemGrip>().Style);

            var socket = new EquipItemSocket(rig.Hand, rig.Frame, 1f);
            GameObject held = socket.Equip(prefab);
            try
            {
                Vector3 along = spec.ItemAlong ?? stance.ItemAlong;
                Vector3 inBody = Quaternion.Inverse(rig.Facing) * held.transform.TransformDirection(along);
                return Vector3.Angle(inBody, stance.BodyAlong);
            }
            finally
            {
                Object.DestroyImmediate(held);
            }
        }

        /// <summary>Rounded to 1/<paramref name="steps"/>, so a re-run that changes nothing writes nothing.</summary>
        private static Vector3 Round(Vector3 v, float steps) =>
            new Vector3(Mathf.Round(v.x * steps) / steps, Mathf.Round(v.y * steps) / steps, Mathf.Round(v.z * steps) / steps);
    }
}
