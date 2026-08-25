using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace SpaceGame.Items
{
    /// <summary>
    /// Which moving part of the rig a hinge is, so the unfold's beat sheet can give it its own
    /// window and its own curve.
    ///
    /// <para>
    /// <see cref="Generic"/> is deliberately zero, so every hinge authored before the beat sheet
    /// existed — the old clamshell's two doors, <c>ExpeditionBackpack</c>'s lid and panel — keeps
    /// the single shared smoothstep it was tuned against. A rig opts into the beat sheet by naming
    /// its parts, never by accident.
    /// </para>
    /// </summary>
    public enum BackpackHingePart
    {
        /// <summary>No named role: swings with every other generic hinge, on one shared ease.</summary>
        Generic = 0,

        /// <summary>The back panel the kickstands prop up. Tips to 65&#176;.</summary>
        Panel = 1,

        /// <summary>
        /// The front leaf. FALLS rather than eases — see the unfold in BackpackObject.
        ///
        /// <para>
        /// The one hinge with two callers. As well as the unfold, it carries the RACK: the leaf
        /// flipped up into a vertical panel for the biggest gear, at exactly the
        /// <see cref="BackpackHinge.foldAngle"/> below. Racked and stowed are the same place for
        /// this member, so the rack needed no hinge of its own — which is why nothing about it
        /// appears in this enum. <c>BackpackObject.LeafFromOpen</c> is where the two are combined,
        /// and it is the only thing that has to know.
        /// </para>
        /// </summary>
        Leaf = 2,

        /// <summary>
        /// The left side panel. Its pivot is a CHILD of <c>PIVOT_Leaf</c>, so this fold is
        /// relative to the board: ±90° stands the panel square up off it, and when the board
        /// rises the panel comes round to hug the pack's flank — the side of a box being closed.
        /// Leads the right one by 40 ms.
        /// </summary>
        WingLeft = 3,

        /// <summary>The right side panel. Mirrored fold, 40 ms behind the left.</summary>
        WingRight = 4,

        /// <summary>
        /// The lid apron on the board's leading edge (2026-08-25). Like the wings its pivot is a
        /// CHILD of <c>PIVOT_Leaf</c>, so its fold is relative to the board: -90&#176; stands it up
        /// as the stow's end wall, and riding the board's own -90&#176; it arrives flat on top,
        /// capping the closed pack — the box's top, where the wings are its sides. In the rack
        /// pose the same relative fold turns it into a hood over the board's top edge. On the
        /// beat sheet it is the last slot of the flap stagger chain — one 40 ms stagger behind
        /// the right wing, so the stow's fold-up wave runs lid, right wing, left wing and never
        /// interleaves — and it follows the rack clock with the wings, which is what carries the
        /// hood up with the racked board. See <c>BackpackObject.FlapFromOpen</c>.
        /// </summary>
        Lid = 5,
    }

    /// <summary>
    /// One part of a pack that moves when it opens: which empty drives it, which of that empty's
    /// own axes it turns about, how far, and which way round the model was authored.
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
        [Tooltip("The empty exported from Blender, e.g. PIVOT_Leaf. Its children swing with it.")]
        public Transform pivot;

        [Tooltip("Which moving part this is. Generic keeps the old single shared ease; naming a " +
                 "part opts it into the unfold beat sheet.")]
        public BackpackHingePart part;

        [Tooltip("Hinge line in the PIVOT's own local space. The expedition rig's panel and leaf " +
                 "use (1,0,0) and its wings (0,1,0); the older clamshell doors used (0,0,1). " +
                 "Normalised at use, so length is free.")]
        public Vector3 localAxis;

        [Tooltip("Degrees from the model's AUTHORED REST pose to its other pose, signed. With " +
                 "restIsOpen off (the default) rest is closed and this is the closed-to-open " +
                 "travel. With it on the model is authored deployed and this is the STOW travel: " +
                 "expedition_rig wants PIVOT_Back +25, PIVOT_Leaf -90, PIVOT_Wing_L -90, " +
                 "PIVOT_Wing_R +90, PIVOT_Lid -90 (relative to the board it rides).")]
        [FormerlySerializedAs("openAngle")]
        public float foldAngle;

        [Tooltip("Tick when the FBX is authored DEPLOYED, as expedition_rig.blend is — every " +
                 "pivot at rotation zero in the open pose, because that is the pose whose " +
                 "measurements the spec gives. Leave off for a model authored closed, like " +
                 "expedition_backpack.")]
        public bool restIsOpen;

        /// <summary>
        /// The travel between the two poses, as an offset from whatever the authored rest rotation
        /// happens to be. It is never absolute: an FBX hands empties back rotated
        /// (<c>PIVOT_Clamshell</c> arrived at euler (270.02, 0, 0)), and applying an absolute angle
        /// to that reorients the whole part instead of turning it about its hinge — which on the
        /// old pack buried a door 0.4 m underground.
        /// </summary>
        public Quaternion FoldOffset()
        {
            // A zero axis is an unwired inspector field, not an intention. Falling back to X keeps
            // the part moving in some plausible way rather than silently making the pack look
            // welded shut, which is far harder to spot than a wrong axis.
            Vector3 axis = localAxis.sqrMagnitude > 1e-6f ? localAxis.normalized : Vector3.right;
            return Quaternion.AngleAxis(foldAngle, axis);
        }

        /// <summary>
        /// The deployed pose, given the rest rotation captured off the model.
        ///
        /// Which of the two poses rest IS is the whole reason <see cref="restIsOpen"/> exists, and
        /// it is asked rather than assumed: expedition_rig is authored open and expedition_backpack
        /// is authored closed, and getting it backwards folds a rig inside out with no error.
        /// </summary>
        public Quaternion OpenPose(Quaternion rest) => restIsOpen ? rest : rest * FoldOffset();

        /// <summary>The stowed pose, given the rest rotation captured off the model.</summary>
        public Quaternion ClosedPose(Quaternion rest) => restIsOpen ? rest * FoldOffset() : rest;
    }
}
