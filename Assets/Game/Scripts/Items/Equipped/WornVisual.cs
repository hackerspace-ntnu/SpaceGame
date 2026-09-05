using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Swaps an item between the model it is CARRIED as, the model it is WORN as, and the model it
    /// is LOOKED AT as on the gear screen.
    ///
    /// <para>
    /// Two items need this and both need it for the same reason: what a thing looks like in your
    /// hand and what it looks like strapped to your back are different objects, not one object
    /// seen from further away. The wing pack held is a folded aircraft; worn it is two wings
    /// clamped to the expedition rig's bar tips. The wingsuit held is a bundle of cloth on a
    /// spar; worn it is a wing spread between the wearer's arms.
    /// </para>
    /// <para>
    /// <b>The third form is the gear screen's, and only the wing pack has one.</b> Its wings are
    /// stowed for ordinary play — folded shut, 1.97 m across, so a walking character is not
    /// wearing a wingspan — and spread on the gear screen, which is the one place a player looks
    /// AT their own back on purpose, with the camera flown round for it. Both models are authored
    /// on the same two rail tips at true wearer scale, so the swap moves nothing. An item with no
    /// <see cref="InspectChildName"/> child falls back to its worn model, which is every other
    /// item and is why nothing else had to change.
    /// </para>
    /// <para>
    /// <b>By child NAME, with no component of its own, and that is deliberate.</b> The body screen
    /// previews worn gear with <see cref="DisplayCopy"/> ghosts, and a display copy has every
    /// MonoBehaviour taken off it before it is ever active — so a component holding the
    /// references would be gone by the time the ghost needed seating, and the ghost would promise
    /// the carried model while the real thing wore another. A name survives the strip.
    /// </para>
    /// </summary>
    public static class WornVisual
    {
        /// <summary>Which of an item's models is showing.</summary>
        public enum Form
        {
            /// <summary>In the hand, on the ground, on the pack's mat: the item's ordinary model.</summary>
            Carried,

            /// <summary>On the body, out in the world.</summary>
            Worn,

            /// <summary>On the body, on the gear screen. Falls back to <see cref="Worn"/>.</summary>
            Inspected,
        }

        /// <summary>
        /// The child that holds the worn-only geometry. A prefab without one is unaffected by
        /// everything here, which is every item but two.
        ///
        /// <para>
        /// It ships <b>inactive</b> on the asset. <see cref="ItemBounds"/> measures only what is
        /// switched on within the item, and <see cref="ItemGrip.HoldSize"/> and
        /// <c>ItemGrip.PackSize</c> both scale from that measurement — so a worn model left
        /// visible on the asset would have the wing pack measure 3.5 m in the hand and be shrunk
        /// to a sliver to fit the 1.26 m hold size. The wingsuit's flight wings shipped exactly
        /// that bug once; see <c>WingsuitBuilder</c>.
        /// </para>
        /// </summary>
        public const string ChildName = "WornModel";

        /// <summary>
        /// The child that holds the gear screen's model, for the one item whose worn shape differs
        /// between the world and that screen. Ships inactive, for the same reason
        /// <see cref="ChildName"/> does.
        /// </summary>
        public const string InspectChildName = "InspectModel";

        /// <summary>The worn model on this item, or null for an item that has only one look.</summary>
        public static Transform Of(GameObject item) => Child(item, ChildName);

        /// <summary>The gear screen's model on this item, or null — which is every item but one.</summary>
        public static Transform InspectOf(GameObject item) => Child(item, InspectChildName);

        private static Transform Child(GameObject item, string name) =>
            item == null ? null : item.transform.Find(name);

        /// <summary>
        /// Show the worn model and hide the carried one, or the other way round.
        ///
        /// <para>Shorthand for <see cref="SetForm"/> over the two forms that existed before the
        /// gear screen got its own. Kept because most callers only ever mean these two.</para>
        /// </summary>
        public static void SetWorn(GameObject item, bool worn) =>
            SetForm(item, worn ? Form.Worn : Form.Carried);

        /// <summary>
        /// Show the model this <paramref name="form"/> calls for and hide the others.
        ///
        /// <para>
        /// Call it <b>before</b> anything measures the item. <see cref="WornSeat.Apply"/> does,
        /// which is why the size a worn item is drawn at is the showing model's size — and why
        /// the gear screen's 5.5 m wings are not squeezed into the stowed model's 1.97 m.
        /// </para>
        /// <para>
        /// Only children that actually draw something are switched: a child holding a collider, a
        /// grip point or a muzzle marker is left alone, because hiding a model is not the same as
        /// taking an item apart. The two variant children are switched whether or not they draw,
        /// so an empty one cannot be left on to be measured. Nothing recurses — the swap is
        /// between top-level models, and a deeper walk would reach the parts each of them
        /// switches for its own reasons (the wingsuit hides its membranes while folded).
        /// </para>
        /// </summary>
        public static void SetForm(GameObject item, Form form)
        {
            Transform worn = Of(item);
            Transform inspect = InspectOf(item);

            // An item with one look. Left completely alone rather than having its children
            // switched on: this runs over every worn item, and most of them have nothing to swap.
            if (worn == null && inspect == null) return;

            // Inspected falls back to the worn model, so an item that has no gear-screen shape
            // simply looks the same on that screen as it does in the world.
            Transform shown = form switch
            {
                Form.Worn => worn,
                Form.Inspected => inspect != null ? inspect : worn,
                _ => null,
            };

            Transform root = item.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                bool variant = child == worn || child == inspect;
                if (!variant && child.GetComponentInChildren<Renderer>(true) == null) continue;

                child.gameObject.SetActive(variant ? child == shown : shown == null);
            }
        }
    }
}
