using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Marks a thing that is attached to a body but is not part of it.
    ///
    /// <para>
    /// A held item, a worn gauntlet, a torso item, the bracer, the shouldered pack: all of them are
    /// parented onto the wearer's skeleton, so from the hierarchy alone they are indistinguishable
    /// from the body's own parts. Anything that reasons about "what this creature is made of" has to
    /// be able to tell the difference, and until this existed nothing could.
    /// </para>
    /// <para>
    /// <b>The failure it was added for.</b> <c>RagdollRig</c> builds a skeleton by flattening every
    /// descendant and taking each node that draws nothing but has geometry beneath it — which is
    /// exactly the shape of a worn item's root. On a player wearing the jetpack with the pack
    /// shouldered, <b>nine of the fourteen candidate bones were gear</b>: the jetpack's root, its
    /// two models, and the pack's <c>PIVOT_Leaf</c>, <c>PIVOT_Lid</c> and <c>PIVOT_Wing_L/R</c> flap
    /// hinges. On death those are given a <c>Rigidbody</c> and a <c>CharacterJoint</c> and simulated
    /// as limbs, so the jetpack is flung off the body and the pack's flaps are driven by physics —
    /// reported as gear that vanished and a backpack whose parts had moved the wrong way.
    /// </para>
    /// <para>
    /// A marker rather than a name check or a layer, and rather than reusing
    /// <c>SimulationDrivers.BelongsTo</c>'s <c>NetworkObject</c> boundary: that boundary is about
    /// which machine simulates a thing, it answers "no" for anything worn WITHOUT a network identity
    /// (the bracer is pure geometry and has none), and borrowing it here would tie two unrelated
    /// questions together. This one is asked and answered in one place — the systems that attach
    /// something say so, and everything else can ask.
    /// </para>
    /// <para>
    /// Added at runtime by the attaching system, never authored on a prefab: the same prefab is a
    /// world object lying in the sand, and out there it is not attached to anything.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public class BodyAttachment : MonoBehaviour
    {
        /// <summary>
        /// Say that <paramref name="instance"/> is attached to a body rather than part of one.
        ///
        /// <para>
        /// Idempotent, and safe to call on something already marked — the equip paths run again on
        /// every slot change.
        /// </para>
        /// </summary>
        public static void Mark(GameObject instance)
        {
            if (instance == null) return;
            if (instance.GetComponent<BodyAttachment>() == null) instance.AddComponent<BodyAttachment>();
        }

        /// <summary>
        /// Whether <paramref name="node"/> is inside something attached to the body, at or below
        /// <paramref name="root"/>.
        ///
        /// <para>
        /// Walks up rather than testing the node itself, because the mark sits on the item's ROOT
        /// and the parts that draw meshes are below it. Stops at <paramref name="root"/> so a body
        /// that is itself attached to something else — a rider parented into a mount — does not
        /// report its own parts as attachments.
        /// </para>
        /// </summary>
        public static bool Covers(Transform node, Transform root)
        {
            for (Transform t = node; t != null && t != root; t = t.parent)
                if (t.GetComponent<BodyAttachment>() != null) return true;

            return false;
        }
    }
}
