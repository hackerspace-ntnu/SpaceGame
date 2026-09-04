using UnityEngine;

namespace SpaceGame.Items
{
    /// <summary>
    /// Swaps an item between the model it is CARRIED as and the model it is WORN as.
    ///
    /// <para>
    /// Two items need this and both need it for the same reason: what a thing looks like in your
    /// hand and what it looks like strapped to your back are different objects, not one object
    /// seen from further away. The wing pack held is a folded aircraft; worn it is two wings
    /// hanging off the expedition rig's bar tips. The wingsuit held is a bundle of cloth on a
    /// spar; worn it is a wing spread between the wearer's arms.
    /// </para>
    /// <para>
    /// <b>By child NAME, with no component of its own, and that is deliberate.</b> The body screen
    /// previews worn gear with <see cref="DisplayCopy"/> ghosts, and a display copy has every
    /// MonoBehaviour taken off it before it is ever active — so a component holding the two
    /// references would be gone by the time the ghost needed seating, and the ghost would promise
    /// the carried model while the real thing wore the other one. A name survives the strip.
    /// </para>
    /// </summary>
    public static class WornVisual
    {
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

        /// <summary>The worn model on this item, or null for an item that has only one look.</summary>
        public static Transform Of(GameObject item)
        {
            if (item == null) return null;
            return item.transform.Find(ChildName);
        }

        /// <summary>
        /// Show the worn model and hide the carried one, or the other way round.
        ///
        /// <para>
        /// Call it <b>before</b> anything measures the item. <see cref="WornSeat.Apply"/> does,
        /// which is why the size a worn item is drawn at is the worn model's size.
        /// </para>
        /// <para>
        /// Only children that actually draw something are switched: a child holding a collider, a
        /// grip point or a muzzle marker is left alone, because hiding a model is not the same as
        /// taking an item apart. Nothing recurses — the swap is between two top-level models, and
        /// a deeper walk would reach the parts each of them switches for its own reasons (the
        /// wingsuit hides its membranes while the wings are folded).
        /// </para>
        /// </summary>
        public static void SetWorn(GameObject item, bool worn)
        {
            Transform wornModel = Of(item);
            if (wornModel == null) return;

            wornModel.gameObject.SetActive(worn);

            Transform root = item.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child == wornModel) continue;
                if (child.GetComponentInChildren<Renderer>(true) == null) continue;

                child.gameObject.SetActive(!worn);
            }
        }
    }
}
