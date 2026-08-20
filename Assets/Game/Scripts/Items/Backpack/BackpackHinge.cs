using System;
using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// One part of a pack that moves when it opens: which empty drives it, which of that empty's
    /// own axes it turns about, and how far.
    ///
    /// This exists so the pack's opening lives in the PREFAB rather than in code. The first pack
    /// was a clamshell — two rear vertical hinges, mirrored — and the swing was written that way,
    /// as a hardcoded pair with a shared angle and a baked-in sign flip. The expedition pack opens
    /// nothing like it: a lid tips back about the top edge and a front panel folds down about the
    /// bottom one, same direction, different axes, different angles. Rather than a second special
    /// case, a pack now just lists its moving parts.
    ///
    /// The axis is in the PIVOT's local space, not the pack's. Blender empties arrive at their
    /// authored rest rotation, so a hinge lying along an empty's local X may point anywhere in the
    /// pack's frame — and it is the empty's own axis that the modeller aligned to the hinge line.
    /// </summary>
    [Serializable]
    public struct BackpackHinge
    {
        [Tooltip("The empty exported from Blender, e.g. PIVOT_Lid. Its children swing with it.")]
        public Transform pivot;

        [Tooltip("Hinge line in the PIVOT's own local space. Both expedition hinges use (1,0,0); " +
                 "the older clamshell doors used (0,0,1). Normalised at use, so length is free.")]
        public Vector3 localAxis;

        [Tooltip("Degrees from closed to open, signed — flip it to swing the part the other way. " +
                 "A part hinged on a REAR edge has to come past 90 before its inner face turns " +
                 "toward the player.")]
        public float openAngle;

        /// <summary>
        /// The open pose as an offset from whatever the authored rest rotation happens to be. It is
        /// never absolute: an FBX hands empties back rotated (PIVOT_Clamshell arrived at euler
        /// (270.02, 0, 0)), and applying an absolute angle to that reorients the whole part instead
        /// of turning it about its hinge.
        /// </summary>
        public Quaternion OpenOffset()
        {
            // A zero axis is an unwired inspector field, not an intention. Falling back to X keeps
            // the part moving in some plausible way rather than silently making the pack look
            // welded shut, which is far harder to spot than a wrong axis.
            Vector3 axis = localAxis.sqrMagnitude > 1e-6f ? localAxis.normalized : Vector3.right;
            return Quaternion.AngleAxis(openAngle, axis);
        }
    }
}
